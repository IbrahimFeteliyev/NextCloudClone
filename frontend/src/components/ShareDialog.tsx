import { useEffect, useRef, useState } from 'react';
import { LoaderCircle, Plus, ShieldCheck, Trash2, UsersRound } from 'lucide-react';
import { request, resourcePath } from '../api';
import type { Resource, Share, User } from '../types';
import { has, READ, SHARE } from '../types';
import { Avatar, ErrorMessage, FileIcon, Modal } from './UI';
import GroupAccess from './GroupAccess';
import PermissionControl, { accessDetail, accessName } from './PermissionControl';
export default function ShareDialog({ resource, users, currentUser, preview, onClose, onChange }: { resource: Resource; users: User[]; currentUser: User; preview: boolean; onClose: () => void; onChange: () => void }) {
  const [shares, setShares] = useState(resource.sharedWith); const [recipient, setRecipient] = useState(''); const [bits, setBits] = useState(READ);
  const [tab, setTab] = useState<'people' | 'groups'>(resource.teamSpaceId && !preview ? 'groups' : 'people');
  const [adding, setAdding] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const form = useRef<HTMLDivElement>(null); const path = resourcePath(resource) + '/shares';
  const canShare = has(resource.permissions, SHARE) && !preview;
  useEffect(() => { let active = true; if (!preview) request<Share[]>(path).then(s => { if (active) setShares(s); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [path, preview]);
  useEffect(() => { if (adding) form.current?.scrollIntoView?.({ block: 'nearest' }); }, [adding, recipient]);
  const choose = (id: string) => { setAdding(true); setRecipient(id); setBits(shares.find(x => x.userId === id)?.permissions ?? READ); setError(''); };
  async function refresh() { setShares(await request<Share[]>(path)); onChange(); }
  async function save() { setBusy(true); setError(''); try { await request(path, { method: 'PUT', body: JSON.stringify({ userId: recipient, permissions: bits }) }); await refresh(); setRecipient(''); setBits(READ); setAdding(false); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  async function remove(id: string) { setBusy(true); setError(''); try { await request(path + '/' + id, { method: 'DELETE' }); await refresh(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  const existing = shares.find(s => s.userId === recipient);
  const direct = shares.filter(s => !s.inherited); const automatic = shares.filter(s => s.inherited);
  const renderPerson = (s: Share) => <div className="access-row" key={s.userId}><Avatar name={s.name} index={1} /><div className="access-person"><strong>{s.name}{s.userId === currentUser.id ? ' (you)' : ''}</strong><span>{s.email}</span><small>{s.inherited ? 'Via ' + s.source.replace('Group membership · ', '').replace('Group membership', 'a group') : 'Individual permission'}</small></div><div className="access-row-actions"><span className="access-label" title={accessDetail(s.permissions)}>{accessName(s.permissions)}</span>{canShare && s.userId !== currentUser.id && <button className="text-button" disabled={busy} onClick={() => choose(s.userId)}>{s.inherited ? 'Set exception' : 'Edit'}</button>}{!s.inherited && canShare && s.userId !== currentUser.id && <button className="icon-button danger-text" onClick={() => remove(s.userId)} disabled={busy} aria-label={'Remove direct access for ' + s.name} title="Remove individual permission; group or parent access may remain"><Trash2 size={15} /></button>}</div></div>;
  return <Modal title="Manage access" subtitle="See who has access and choose how to share." onClose={() => { if (!busy) onClose(); }} wide>
    <div className="shared-resource"><FileIcon resource={resource} /><div><strong>{resource.name}</strong><span>{resource.teamSpaceId ? 'Team Folder · ' + resource.owner : 'Owned by ' + resource.owner}{resource.type === 'folder' ? ' · Applies to contents unless overridden' : ''}</span></div><ShieldCheck size={20} /></div>
    {preview && <div className="notice">Preview mode · Sharing changes are available after sign-in.</div>}
    <div className="sharing-tabs" role="tablist" aria-label="Access type"><button role="tab" aria-selected={tab === 'groups'} disabled={preview || busy} onClick={() => setTab('groups')}>Groups & departments</button><button role="tab" aria-selected={tab === 'people'} disabled={busy} onClick={() => setTab('people')}>People</button></div>
    <ErrorMessage message={error} />
    {tab === 'groups' && !preview ? <GroupAccess resource={resource} onChange={() => { void refresh(); }} /> : <div className="access-section sharing-people" role="tabpanel" aria-label="People">
      <div className="sharing-section-heading"><div><h3>Individual access <span>{direct.length}</span></h3><p>Extra access or exceptions for specific people.</p></div>{canShare && <button className="button secondary" disabled={busy} onClick={() => { setAdding(!adding); setRecipient(''); setBits(READ); }}><Plus size={15} />Add person</button>}</div>
      {!resource.teamSpaceId && <div className="access-row"><Avatar name={resource.owner} /><div className="access-person"><strong>{resource.owner}{resource.ownerId === currentUser.id ? ' (you)' : ''}</strong><span>{resource.ownerEmail}</span></div><span className="owner-label">Owner</span></div>}
      {direct.map(renderPerson)}{!direct.length && <p className="sharing-empty">No individual permissions added.</p>}
      {adding && <div className="sharing-editor" ref={form}><h4>{existing ? 'Change individual access' : 'Add a person'}</h4><label>Person<select value={recipient} onChange={e => choose(e.target.value)} disabled={!canShare || busy}><option value="">Select a teammate</option>{users.filter(u => u.id !== resource.ownerId && u.id !== currentUser.id).map(u => <option key={u.id} value={u.id}>{u.name} · {u.email}</option>)}</select></label><PermissionControl bits={bits} available={resource.permissions} disabled={!canShare || busy} onChange={setBits} />
        <p className="sharing-explanation">{existing?.inherited ? 'This individual permission replaces their group or parent access on this item.' : 'Other people keep their existing group and individual access.'}</p><div className="sharing-editor-actions"><button className="button secondary" disabled={busy} onClick={() => { setAdding(false); setRecipient(''); }}>Cancel</button><button className="button primary" disabled={!recipient || !canShare || busy} onClick={save}>{busy ? <LoaderCircle size={16} className="spin" /> : <UsersRound size={16} />}{bits === 0 ? 'Block access' : existing ? 'Update access' : 'Grant access'}</button></div></div>}
      <div className="sharing-automatic"><h3>Access from groups or parent folders <span>{automatic.length}</span></h3><p>These people already have access through membership or inheritance.</p>{automatic.map(renderPerson)}{!automatic.length && <p className="sharing-empty">No inherited access.</p>}</div>
    </div>}
    <div className="modal-footer"><span><ShieldCheck size={14} />{canShare ? 'Access is checked by the server.' : 'You can view access. Sharing requires permission.'}</span><button className="button secondary" disabled={busy} onClick={onClose}>Done</button></div>
  </Modal>;
}
