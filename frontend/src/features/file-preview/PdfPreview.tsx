import { useEffect, useId, useRef, useState } from 'react';
import { getDocument, GlobalWorkerOptions } from 'pdfjs-dist';
import type { PDFDocumentProxy, RenderTask } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { Minus, Plus } from 'lucide-react';
import { getToken } from '../../api';
import { apiBase } from './content';
import type { PreviewTicket } from './content';
import { PreviewError, PreviewLoading } from './PreviewState';
GlobalWorkerOptions.workerSrc = workerUrl;
export default function PdfPreview({ ticket }: { ticket: PreviewTicket }) {
  const [pdf, setPdf] = useState<PDFDocumentProxy | null>(null); const [error, setError] = useState(''); const [zoom, setZoom] = useState(1); const [page, setPage] = useState(1);
  const scroll = useRef<HTMLDivElement>(null); const [width, setWidth] = useState(800); const id = useId();
  useEffect(() => {
    const base = `${import.meta.env.BASE_URL}pdfjs/`;
    const task = getDocument({ url: `${apiBase}/files/${ticket.id}/content`, httpHeaders: { Authorization: `Bearer ${getToken()}` }, cMapUrl: `${base}cmaps/`, cMapPacked: true, standardFontDataUrl: `${base}standard_fonts/`, wasmUrl: `${base}wasm/`, disableAutoFetch: true, disableStream: true });
    let active = true;
    task.promise.then(document => { if (active) setPdf(document); }).catch(e => { if (active) setError(e.message || 'This PDF could not be opened.'); });
    return () => { active = false; void task.destroy(); };
  }, [ticket]);
  useEffect(() => {
    if (!pdf || !scroll.current) return;
    const observer = new ResizeObserver(entries => setWidth(Math.max(240, Math.min(900, entries[0].contentRect.width - 48))));
    observer.observe(scroll.current); return () => observer.disconnect();
  }, [pdf]);
  if (error) return <PreviewError message={error} />; if (!pdf) return <PreviewLoading />;
  const count = Math.min(pdf.numPages, 200);
  function jump(value: number) { const next = Math.max(1, Math.min(count, value || 1)); setPage(next); document.getElementById(`${id}-page-${next}`)?.scrollIntoView({ block: 'start' }); }
  return <div className="pdf-preview"><div className="preview-toolbar"><label>Page <input aria-label="PDF page" type="number" min={1} max={count} value={page} onChange={e => jump(Number(e.target.value))} /> of {pdf.numPages}</label><div><button className="icon-button" aria-label="Zoom out PDF" disabled={zoom <= 0.5} onClick={() => setZoom(x => x - 0.25)}><Minus size={18} /></button><span>{Math.round(zoom * 100)}%</span><button className="icon-button" aria-label="Zoom in PDF" disabled={zoom >= 2} onClick={() => setZoom(x => x + 0.25)}><Plus size={18} /></button></div></div>
    {pdf.numPages > count && <div className="preview-warning">Showing the first 200 pages. Download for the full PDF.</div>}
    <div ref={scroll} className="pdf-scroll">{Array.from({ length: count }, (_, i) => <PdfPage key={i} pdf={pdf} number={i + 1} width={width * zoom} id={`${id}-page-${i + 1}`} onVisible={() => setPage(i + 1)} />)}</div></div>;
}
function PdfPage({ pdf, number, width, id, onVisible }: { pdf: PDFDocumentProxy; number: number; width: number; id: string; onVisible: () => void }) {
  const box = useRef<HTMLDivElement>(null); const canvas = useRef<HTMLCanvasElement>(null); const [visible, setVisible] = useState(false); const [ratio, setRatio] = useState(1.414); const [error, setError] = useState(''); const onVisibleRef = useRef(onVisible); onVisibleRef.current = onVisible;
  useEffect(() => {
    const observer = new IntersectionObserver(entries => { setVisible(entries[0].isIntersecting); if (entries[0].isIntersecting) onVisibleRef.current(); }, { rootMargin: '350px 0px' });
    observer.observe(box.current!); return () => observer.disconnect();
  }, []);
  useEffect(() => {
    if (!visible) { if (canvas.current) { canvas.current.width = 0; canvas.current.height = 0; } return; }
    let active = true; let render: RenderTask | undefined;
    void pdf.getPage(number).then(async page => {
      if (!active) return;
      const natural = page.getViewport({ scale: 1 }); setRatio(natural.height / natural.width);
      const viewport = page.getViewport({ scale: width / natural.width }); const scale = Math.min(window.devicePixelRatio || 1, 2);
      const element = canvas.current!; element.width = Math.floor(viewport.width * scale); element.height = Math.floor(viewport.height * scale);
      render = page.render({ canvas: element, viewport, transform: [scale, 0, 0, scale, 0, 0] }); await render.promise;
    }).catch(e => { if (active && e.name !== 'RenderingCancelledException') setError('This page could not be rendered.'); });
    return () => { active = false; render?.cancel(); };
  }, [pdf, number, width, visible]);
  return <div ref={box} id={id} className="pdf-page" style={{ width, minHeight: width * ratio }} aria-label={`PDF page ${number}`}><canvas ref={canvas} style={{ width, height: width * ratio }} />{error && <p role="alert">{error}</p>}<span className="pdf-page-number">{number}</span></div>;
}

