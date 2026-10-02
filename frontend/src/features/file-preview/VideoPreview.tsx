import { useEffect, useRef, useState } from 'react';
import videojs from 'video.js';
import 'video.js/dist/video-js.css';
import { contentUrl } from './content';
import type { PreviewTicket } from './content';
export default function VideoPreview({ ticket }: { ticket: PreviewTicket }) {
  const container = useRef<HTMLDivElement>(null); const [error, setError] = useState('');
  useEffect(() => {
    const element = document.createElement('video-js'); element.className = 'video-js vjs-big-play-centered';
    container.current!.appendChild(element);
    const player = videojs(element, { controls: true, responsive: true, fill: true, preload: 'metadata', playbackRates: [0.5, 1, 1.5, 2], sources: [{ src: contentUrl(ticket), type: ticket.contentType }] });
    player.on('error', () => setError('This video could not be played. The codec may not be supported, access may have changed, or the preview link expired. Close and reopen the preview to retry.'));
    return () => { if (!player.isDisposed()) player.dispose(); };
  }, [ticket]);
  return <div className="video-preview">{error && <div className="preview-warning" role="alert">{error}</div>}<div ref={container} className="video-container" data-vjs-player /></div>;
}
