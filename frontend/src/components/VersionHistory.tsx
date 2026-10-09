import { useEffect, useState } from 'react';
import { Download, LoaderCircle, RotateCcw, Trash2 } from 'lucide-react';
import { download, request } from '../api';
import type { Resource } from '../types';
import { DELETE, formatSize, has, WRITE } from '../types';
import { ErrorMessage, Modal } from './UI';

type Version = { number: number; size: number; createdAt: string; createdBy: string; isCurrent: boolean; currentVersion: number };
export default function VersionHistory({ resource, onClose, onChange }: { resource: Resource; onClose: () => void; onChange: () => void }) {
  const [versions, setVersions] = useState<Version[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [restore, setRestore] = useState<number | null>(null);
  const [remove, setRemove] = useState<number | null>(null);
  async function refresh() {
    setLoading(true); setError('');
    try { setVersions(await request<Version[]>(`/files/${resource.id}/versions`)); }
    catch (e) { setError((e as Error).message); }
    finally { setLoading(false); }
  }
  useEffect(() => {
    let active = true;
    request<Version[]>(`/files/${resource.id}/versions`).then(v => { if (active) setVersions(v); })
      .catch(e => { if (active) setError((e as Error).message); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [resource.id]);
  async function restoreVersion() {
    if (restore === null || !versions.length) return;
    setBusy(true); setError(''); setSuccess('');
    try {
      await request(`/files/${resource.id}/versions/${restore}/restore`, { method: 'POST', body: JSON.stringify({ expectedVersion: versions[0].currentVersion }) });
      setRestore(null); setSuccess('Restored the selected saved version. Saved versions are preserved.'); onChange(); await refresh();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function downloadVersion(number: number) {
    setBusy(true); setError('');
    try { await download(resource.id, resource.name, number); } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function saveVersion() {
    setBusy(true); setError(''); setSuccess('');
    try { await request(`/files/${resource.id}/versions`, { method: 'POST' }); setSuccess('Current content saved as a version.'); await refresh(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function deleteVersion() {
    if (remove === null) return; setBusy(true); setError(''); setSuccess('');
    try { await request(`/files/${resource.id}/versions/${remove}`, { method: 'DELETE' }); setRemove(null); setSuccess('Saved version deleted. Current file is unchanged.'); await refresh(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <Modal title="Version history" subtitle={resource.name} wide onClose={() => { if (!busy) onClose(); }}>
    <div className="version-history">
      <p>Versions are saved manually. Use Save current version to keep a copy before editing.</p>
      {has(resource.permissions, WRITE) && <button className="button primary" disabled={busy || loading} onClick={() => void saveVersion()}>Save current version</button>}
      <ErrorMessage message={error} />
      {success && <p role="status" className="notice">{success}</p>}
      {loading ? <p role="status"><LoaderCircle size={18} className="spin" /> Loading versions…</p> :
        versions.length ? <div className="table-scroll"><table className="version-table"><thead><tr><th>Version</th><th>Saved by / date</th><th>Size</th><th>Actions</th></tr></thead><tbody>
          {versions.map(v => <tr key={v.number}><td><strong>v{v.number}</strong>{v.isCurrent && <span className="subtle-badge">Current</span>}</td><td>{v.createdBy}<small>{new Date(v.createdAt).toLocaleString()}</small></td><td>{formatSize(v.size)}</td><td><div className="version-actions">
            <button className="button secondary" disabled={busy} onClick={() => void downloadVersion(v.number)} aria-label={`Download version ${v.number}`}><Download size={15} />Download</button>
            {!v.isCurrent && has(resource.permissions, WRITE) && <button className="button secondary" disabled={busy} onClick={() => { setRemove(null); setRestore(v.number); setError(''); }}><RotateCcw size={15} />Restore v{v.number}</button>}
            {has(resource.permissions, DELETE) && <button className="button secondary danger-text" disabled={busy} onClick={() => { setRestore(null); setRemove(v.number); setError(''); }} aria-label={`Delete version ${v.number}`}><Trash2 size={15} />Delete</button>}
          </div></td></tr>)}
        </tbody></table></div> : !error && <p>No saved versions available.</p>}
      {restore !== null && <div className="notice"><p>Restore version {restore}? Unsaved current content will be replaced. Save current version first if you want to keep it.</p><div className="version-actions"><button className="button secondary" disabled={busy} onClick={() => setRestore(null)}>Cancel</button><button className="button primary" disabled={busy || loading} onClick={() => void restoreVersion()}>{busy && <LoaderCircle size={15} className="spin" />}Confirm restore</button></div></div>}
      {remove !== null && <div className="notice danger-notice"><p>Permanently delete saved version {remove}? This removes the history entry, not the current file.</p><div className="version-actions"><button className="button secondary" disabled={busy} onClick={() => setRemove(null)}>Cancel</button><button className="button danger" disabled={busy || loading} onClick={() => void deleteVersion()}>Confirm delete version</button></div></div>}
      {!has(resource.permissions, WRITE) && <p>You have read-only access. Restoring requires Write permission.</p>}
    </div>
    <div className="modal-footer"><button className="button secondary" disabled={loading || busy} onClick={() => { setRestore(null); void refresh(); }}>Refresh</button><button className="button primary" disabled={busy} onClick={onClose}>Done</button></div>
  </Modal>;
}
