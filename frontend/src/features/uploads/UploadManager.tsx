import { useState } from 'react';
import { ChevronDown, ChevronUp, CloudUpload } from 'lucide-react';
import type { UploadEntry } from './useUploads';
import UploadItem from './UploadItem';
import './uploads.css';
type Props = { entries: UploadEntry[]; cancel: (id: string) => void; retry: (id: string) => void; replace: (id: string) => void; clearFinished: () => void };
export default function UploadManager({ entries, cancel, retry, replace, clearFinished }: Props) {
  const [collapsed, setCollapsed] = useState(false);
  if (!entries.length) return null;
  const active = entries.filter(entry => ['Waiting', 'Uploading'].includes(entry.status)).length;
  return <aside className="upload-manager" aria-label="File uploads">
    <header><CloudUpload size={18} /><strong>Uploads{active ? ` (${active} active)` : ''}</strong><button className="upload-clear" disabled={!entries.some(entry => ['Completed', 'Cancelled'].includes(entry.status))} onClick={clearFinished}>Clear finished</button><button className="icon-button" aria-label={collapsed ? 'Expand uploads' : 'Collapse uploads'} aria-expanded={!collapsed} onClick={() => setCollapsed(value => !value)}>{collapsed ? <ChevronUp size={18} /> : <ChevronDown size={18} />}</button></header>
    {!collapsed && <ul>{entries.map(entry => <UploadItem key={entry.id} entry={entry} onCancel={cancel} onRetry={retry} onReplace={replace} />)}</ul>}
  </aside>;
}
