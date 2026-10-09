import { Component, lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { RotateCcw } from 'lucide-react';
import { request } from '../../api';
import type { Resource } from '../../types';
import OfficeEditor from '../../components/OfficeEditor';
import PreviewModal from './PreviewModal';
import UnsupportedPreview from './UnsupportedPreview';
import { resolvePreview } from './previewResolver';
import type { PreviewTicket } from './content';
import type { EditorSearchCommand } from './TextPreview';
import { PreviewError, PreviewLoading } from './PreviewState';
import './preview.css';
const VideoPreview = lazy(() => import('./VideoPreview'));
const ImagePreview = lazy(() => import('./ImagePreview'));
const PdfPreview = lazy(() => import('./PdfPreview'));
const TextPreview = lazy(() => import('./TextPreview'));
const CsvPreview = lazy(() => import('./CsvPreview'));
class PreviewBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? <PreviewError message="This file could not be displayed. Close and reopen the preview to retry, or use Download." /> : this.props.children; }
}
export default function FilePreview({ resource, onClose }: { resource: Resource; onClose: () => void }) {
  const office = resolvePreview(resource) === 'office';
  const [ticket, setTicket] = useState<PreviewTicket | null>(null); const [error, setError] = useState(''); const [attempt, setAttempt] = useState(0);
  const [dirty, setDirty] = useState(false); const [saving, setSaving] = useState(false);
  const editorSearch = useRef<EditorSearchCommand>(null);
  const registerSearch = useCallback((command: EditorSearchCommand) => { editorSearch.current = command; }, []);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn); return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);
  const close = () => { if (!saving && (!dirty || window.confirm('Discard your unsaved changes and close this file?'))) onClose(); };
  useEffect(() => {
    if (office) return;
    const abort = new AbortController(); setTicket(null); setError('');
    request<PreviewTicket>(`/files/${resource.id}/preview`, { method: 'POST', signal: abort.signal }).then(value => { if (!abort.signal.aborted) setTicket(value); }).catch(e => { if (!abort.signal.aborted) setError(e.message); });
    return () => abort.abort();
  }, [resource.id, office, attempt]);
  if (office || (ticket && resolvePreview(ticket) === 'office')) return <OfficeEditor resource={resource} onClose={onClose} />;
  const kind = ticket && resolvePreview(ticket);
  return <PreviewModal file={ticket || resource} onClose={close} closeDisabled={saving} onSearch={kind === 'text' || kind === 'markdown' ? replace => editorSearch.current?.(replace) : undefined}>{error ? <><PreviewError message={error} /><button className="button secondary preview-retry" onClick={() => setAttempt(x => x + 1)}><RotateCcw size={16} />Try again</button></> : !ticket ? <PreviewLoading /> : <PreviewBoundary key={resource.id}><Suspense fallback={<PreviewLoading />}>
    {kind === 'video' ? <VideoPreview ticket={ticket} /> : kind === 'image' ? <ImagePreview ticket={ticket} /> : kind === 'pdf' ? <PdfPreview ticket={ticket} /> : kind === 'text' || kind === 'markdown' ? <TextPreview ticket={ticket} onDirtyChange={setDirty} onBusyChange={setSaving} onSaved={setTicket} onSearchReady={registerSearch} /> : kind === 'csv' ? <CsvPreview ticket={ticket} /> : <UnsupportedPreview />}
  </Suspense></PreviewBoundary>}</PreviewModal>;
}
