import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { Download, X } from 'lucide-react';
import { download } from '../../api';
import { formatSize } from '../../types';
export default function PreviewModal({ file, onClose, children }: { file: { id: string; name: string; contentType?: string | null; size: number }; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  useEffect(() => { const element = dialog.current!; const previous = document.documentElement.style.overflow; document.documentElement.style.overflow = 'hidden'; element.showModal(); return () => { element.close(); document.documentElement.style.overflow = previous; }; }, []);
  async function save() { setBusy(true); setError(''); try { await download(file.id, file.name); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  return <dialog ref={dialog} className="preview-dialog" aria-label={`Preview: ${file.name}`} onCancel={e => { e.preventDefault(); onClose(); }}>
    <header className="preview-header"><div className="preview-title"><strong title={file.name}>{file.name}</strong><span>{file.contentType || file.name.split('.').at(-1)?.toUpperCase()} · {formatSize(file.size)}</span></div><button className="button secondary" disabled={busy} onClick={() => void save()}><Download size={16} />{busy ? 'Downloading…' : 'Download'}</button><button className="icon-button" aria-label="Close preview" onClick={onClose}><X size={22} /></button></header>
    {error && <div className="preview-warning" role="alert">{error}</div>}
    <div className="preview-body">{children}</div>
  </dialog>;
}
