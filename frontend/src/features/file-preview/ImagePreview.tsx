import { useEffect, useRef, useState } from 'react';
import PhotoSwipe from 'photoswipe';
import DOMPurify from 'dompurify';
import 'photoswipe/style.css';
import { readContent } from './content';
import type { PreviewTicket } from './content';
import { PreviewError, PreviewLoading } from './PreviewState';
export function sanitizeSvg(text: string): string {
  const cleaned = DOMPurify.sanitize(text, { USE_PROFILES: { svg: true, svgFilters: true }, FORBID_TAGS: ['foreignObject', 'style', 'animate', 'set', 'animateTransform', 'animateMotion'], FORBID_ATTR: ['style'] });
  const doc = new DOMParser().parseFromString(cleaned, 'image/svg+xml');
  if (doc.querySelector('parsererror') || doc.documentElement.localName !== 'svg') throw new Error('This SVG could not be displayed safely.');
  for (const element of doc.querySelectorAll('*')) for (const attr of Array.from(element.attributes)) {
    if ((attr.localName === 'href' && !attr.value.startsWith('#')) || /url\(\s*['"]?(?!#)/i.test(attr.value)) element.removeAttributeNode(attr);
  }
  return new XMLSerializer().serializeToString(doc.documentElement);
}
export default function ImagePreview({ ticket }: { ticket: PreviewTicket }) {
  const container = useRef<HTMLDivElement>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState('');
  useEffect(() => {
    const abort = new AbortController(); let url: string | undefined; let swipe: PhotoSwipe | undefined; let disposed = false;
    async function load() {
      if (ticket.size > 30 * 1024 * 1024) throw new Error('This image is too large for an in-browser preview (30 MB limit). Use Download.');
      if (ticket.contentType === 'image/svg+xml' && ticket.size > 2 * 1024 * 1024) throw new Error('This SVG exceeds the 2 MB preview limit. Use Download.');
      let blob = await readContent(ticket, abort.signal);
      if (ticket.contentType === 'image/svg+xml') blob = new Blob([sanitizeSvg(await blob.text())], { type: 'image/svg+xml' });
      if (disposed) return;
      url = URL.createObjectURL(blob); const image = new Image(); image.src = url; await image.decode();
      if (disposed) return;
      swipe = new PhotoSwipe({ dataSource: [{ src: url, width: image.naturalWidth || 1000, height: image.naturalHeight || 700, alt: ticket.name }], appendToEl: container.current!, bgOpacity: 1, showHideAnimationType: 'none', close: false, arrowPrev: false, arrowNext: false, counter: false, escKey: false, trapFocus: false, returnFocus: false, bgClickAction: false, imageClickAction: 'zoom', tapAction: 'zoom', wheelToZoom: true, secondaryZoomLevel: 2, maxZoomLevel: 8 });
      swipe.options.getViewportSizeFn = () => ({ x: container.current!.clientWidth, y: container.current!.clientHeight });
      swipe.init(); setLoading(false);
    }
    void load().catch(e => { if (!disposed) { setLoading(false); setError(e.message || 'The image could not be decoded.'); } });
    return () => { disposed = true; abort.abort(); swipe?.destroy(); if (url) URL.revokeObjectURL(url); };
  }, [ticket]);
  return <div ref={container} className="image-preview">{error ? <PreviewError message={error} /> : loading && <PreviewLoading />}</div>;
}

