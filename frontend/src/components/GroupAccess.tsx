import { useEffect, useRef, useState } from 'react';
import { Plus, Trash2, UsersRound } from 'lucide-react';
import { request, resourcePath } from '../api';
import type { GroupShare, Organization, Resource } from '../types';
import { READ, SHARE, has } from '../types';
import { ErrorMessage } from './UI';
import PermissionControl, { accessDetail, accessName } from './PermissionControl';
export default function GroupAccess({ resource, onChange }: { resource: Resource; onChange: () => void }) {
  const [catalog, setCatalog] = useState<Organization>({ departments: [], groups: [] });
  const [shares, setShares] = useState<GroupShare[]>([]); const [group, setGroup] = useState(''); const [bits, setBits] = useState(READ); const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [adding, setAdding] = useState(false); const [loading, setLoading] = useState(true);
  const form = useRef<HTMLDivElement>(null); const canShare = has(resource.permissions, SHARE); const path = resourcePath(resource) + '/group-shares';
  useEffect(() => { let active = true; Promise.all([request<Organization>('/organization'), request<GroupShare[]>(path)]).then(([c,s]) => { if (active) { setCatalog(c); setShares(s); } }).catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; }; }, [path]);
  useEffect(() => { if (adding) form.current?.scrollIntoView?.({ block: 'nearest' }); }, [adding,group]);
  async function change(remove?: string) {
    setBusy(true); setError('');
    try { await request(remove ? path + '/' + remove : path, { method: remove ? 'DELETE' : 'PUT', ...(remove ? {} : { body: JSON.stringify({ groupId: group, permissions: bits }) }) }); setShares(await request<GroupShare[]>(path)); setGroup(''); setBits(READ); setAdding(false); onChange(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  const existing = shares.find(x => x.groupId === group);
  return <div className="group-access access-section" role="tabpanel" aria-label="Groups and departments"><div className="sharing-section-heading"><div><h3>Groups and departments</h3><p>Members get access automatically. Individual exceptions are managed under People.</p></div>{canShare && <button className="button secondary" disabled={busy || loading} onClick={() => { setAdding(!adding); setGroup(''); setBits(READ); }}><Plus size={15} />Add group</button>}</div><ErrorMessage message={error} />
    {loading ? <p className="sharing-empty">Loading group access…</p> : !shares.length && <p className="sharing-empty">No group permissions added.</p>}
    {shares.map(x => <div className="access-row" key={x.groupId}><span className="sharing-group-icon"><UsersRound size={19} /></span><div className="access-person"><strong>{x.name}</strong><span>{catalog.groups.find(g => g.id === x.groupId)?.departmentId ? 'Department' : 'Group'}</span><small>{x.inherited ? 'Inherited from ' + x.source : 'Access set on this item'}</small></div><div className="access-row-actions"><span className="access-label" title={accessDetail(x.permissions)}>{accessName(x.permissions)}</span>{canShare && <button className="text-button" disabled={busy} onClick={() => { setAdding(true); setGroup(x.groupId); setBits(x.permissions); }}>{x.inherited ? 'Override' : 'Edit'}</button>}{!x.inherited && canShare && <button className="icon-button danger-text" disabled={busy} aria-label={'Remove group access for ' + x.name} title="Remove direct grant; inherited access may remain" onClick={() => void change(x.groupId)}><Trash2 size={15} /></button>}</div></div>)}
    {canShare && adding && <div className="sharing-editor" ref={form}><h4>{existing ? 'Change group access' : 'Add a group or department'}</h4><label>Group<select value={group} disabled={busy} onChange={e => { setGroup(e.target.value); setBits(shares.find(x => x.groupId === e.target.value)?.permissions ?? READ); }}><option value="">Select a group</option>{catalog.groups.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><PermissionControl bits={bits} available={resource.permissions} disabled={busy} onChange={setBits} />
      <p className="sharing-explanation">{existing?.inherited ? 'This creates an override on this item. The parent folder remains unchanged.' : 'Applies to members of the selected group. Individual permissions take priority.'}</p><div className="sharing-editor-actions"><button className="button secondary" disabled={busy} onClick={() => { setAdding(false); setGroup(''); }}>Cancel</button><button className="button primary" disabled={!group || busy} onClick={() => void change()}>{bits === 0 ? 'Block group' : 'Save group access'}</button></div></div>}
  </div>;
}
