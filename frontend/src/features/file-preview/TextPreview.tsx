import { useTextContent } from './useTextContent';
import type { PreviewTicket } from './content';
import { PreviewError, PreviewLoading } from './PreviewState';
export default function TextPreview({ ticket }: { ticket: PreviewTicket }) {
  const { text, error, truncated } = useTextContent(ticket, 512 * 1024);
  if (error) return <PreviewError message={error} />; if (text === null) return <PreviewLoading />;
  return <div className="text-preview">{truncated && <div className="preview-warning">Showing the first 512 KB. Download to read the complete file.</div>}<pre>{text || '(Empty file)'}</pre></div>;
}
