import { Check, RotateCcw, X } from 'lucide-react';
import { useState } from 'react';
import { formatSize } from '../../types';
import type { UploadEntry } from './useUploads';
import ProgressBar from './ProgressBar';
export default function UploadItem({ entry, onCancel, onRetry, onReplace }: { entry: UploadEntry; onCancel: (id: string) => void; onRetry: (id: string) => void; onReplace: (id: string) => void }) {
  const [confirm, setConfirm] = useState(false);
  const percent = entry.file.size ? Math.round(entry.uploaded / entry.file.size * 100) : entry.status === 'Completed' ? 100 : 0;
  return <li className="upload-item">
    <div className="upload-item-title"><strong title={entry.file.name}>{entry.file.name}</strong><span>{percent}%</span>{entry.status === 'Completed' ? <Check size={16} aria-label="Upload completed" /> : entry.status === 'Failed' ? <button className="icon-button" aria-label={`Retry upload ${entry.file.name}`} onClick={() => onRetry(entry.id)}><RotateCcw size={16} /></button> : ['Waiting', 'Uploading'].includes(entry.status) ? <button className="icon-button" aria-label={`Cancel upload ${entry.file.name}`} onClick={() => onCancel(entry.id)}><X size={16} /></button> : null}</div>
    <ProgressBar name={entry.file.name} percent={percent} status={entry.status} />
    <div className="upload-item-meta"><span>{formatSize(entry.uploaded)} / {formatSize(entry.file.size)}</span><span className={`upload-status ${entry.status.toLowerCase()}`}>{entry.status}</span></div>
    <div className="upload-destination" title={entry.destination}>To {entry.destination}</div>
    {entry.status === 'Uploading' && percent === 100 && <p className="upload-detail">Waiting for server confirmation…</p>}
    {entry.error && <p className="upload-error" role="alert">{entry.error}</p>}
    {entry.status === 'Conflict' && <div className="upload-conflict">{confirm ? <><p>Replace existing content? Unsaved versions will be lost; manually saved versions and sharing permissions stay.</p><button className="button primary" onClick={() => { setConfirm(false); onReplace(entry.id); }}>Confirm replace</button></> : <button className="button secondary" onClick={() => setConfirm(true)}>Replace existing file</button>}<button className="button secondary" onClick={() => { setConfirm(false); onCancel(entry.id); }}>Cancel upload</button></div>}
  </li>;
}
