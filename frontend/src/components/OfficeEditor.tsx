import { useEffect, useId, useRef, useState } from 'react';
import { ArrowLeft, Download, LoaderCircle, LockKeyhole, Pencil, RotateCcw } from 'lucide-react';
import { download, request } from '../api';
import { loadOfficeApi } from '../onlyoffice';
import type { OfficeEditor as EditorInstance, OfficeResponse } from '../onlyoffice';
import type { Resource } from '../types';
import { formatSize } from '../types';
import { ErrorMessage } from './UI';

export default function OfficeEditor({ resource, onClose }: { resource: Resource; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const editorId = `office-${useId().replace(/[^a-zA-Z0-9-]/g, '')}`;
  const [response, setResponse] = useState<OfficeResponse | null>(null);
  const [ready, setReady] = useState(false); const [error, setError] = useState(''); const [attempt, setAttempt] = useState(0);
  const [downloading, setDownloading] = useState(false); const [downloadError, setDownloadError] = useState('');
  async function saveDownload() { setDownloading(true); setDownloadError(''); try { await download(resource.id, resource.name); } catch (e) { setDownloadError((e as Error).message); } finally { setDownloading(false); } }
  useEffect(() => { const element = dialog.current!; element.showModal(); return () => element.close(); }, []);
  useEffect(() => {
    let disposed = false; let editor: EditorInstance | undefined; let timer: number | undefined;
    setError(''); setReady(false); setResponse(null);
    async function open() {
      try {
        const result = await request<OfficeResponse>(`/onlyoffice/files/${resource.id}/config`);
        if (disposed) return;
        setResponse(result); await loadOfficeApi(result.documentServerUrl);
        if (disposed) return;
        timer = window.setTimeout(() => setError('ONLYOFFICE is taking too long to open this document. Check Document Server and try again.'), 60000);
        // All document permissions, URLs, user identity, and mode come from the signed backend config.
        editor = new window.DocsAPI!.DocEditor(editorId, { ...result.config, events: {
          onDocumentReady: () => { if (!disposed) { clearTimeout(timer); setReady(true); setError(''); } },
          onError: event => { if (!disposed) { clearTimeout(timer); setError(event.data?.errorDescription || 'ONLYOFFICE could not open or save the document. Check the server and retry.'); } },
        } });
      } catch (e) { if (!disposed) setError((e as Error).message); }
    }
    void open();
    return () => { disposed = true; clearTimeout(timer); editor?.destroyEditor(); };
  }, [resource.id, editorId, attempt]);
  return <dialog ref={dialog} className="office-dialog" aria-label={`Office editor: ${resource.name}`} onCancel={e => { e.preventDefault(); onClose(); }}>
    <header className="office-header"><button className="button secondary" onClick={onClose}><ArrowLeft size={16} />Back to files</button><strong title={resource.name}>{resource.name}</strong>
      {response && <span className={`permission-badge ${response.mode === 'edit' ? 'editable' : ''}`}>{response.mode === 'edit' ? <Pencil size={12} /> : <LockKeyhole size={12} />}{response.mode === 'edit' ? 'Can edit' : 'Read only'}</span>}
      <span className="office-file-size">{resource.name.split('.').at(-1)?.toUpperCase()} · {formatSize(resource.size)}</span><button className="button secondary office-download" disabled={downloading} onClick={() => void saveDownload()}><Download size={16} />Download</button>
    </header>
    {downloadError && <div className="preview-warning" role="alert">{downloadError}</div>}
    <div className="office-notice">{response?.mode === 'view' ? 'You can view, download, and print. Editing requires WRITE permission.' : 'Changes save to Atlas when you press Save, and after all editors close the document.'}</div>
    <div className="office-body"><div id={editorId} />
      {!ready && !error && <div className="office-loading" role="status"><LoaderCircle size={28} className="spin" /><span>Opening your document…</span></div>}
      {error && <div className="office-error"><ErrorMessage message={error} /><button className="button secondary" onClick={() => setAttempt(x => x + 1)}><RotateCcw size={16} />Try again</button><p>Your last saved version is retained in Atlas.</p></div>}
    </div>
  </dialog>;
}
