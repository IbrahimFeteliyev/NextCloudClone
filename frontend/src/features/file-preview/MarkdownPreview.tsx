import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useTextContent } from './useTextContent';
import type { PreviewTicket } from './content';
import { PreviewError, PreviewLoading } from './PreviewState';
export default function MarkdownPreview({ ticket }: { ticket: PreviewTicket }) {
  const { text, error, truncated } = useTextContent(ticket, 256 * 1024);
  if (error) return <PreviewError message={error} />; if (text === null) return <PreviewLoading />;
  return <>{truncated && <div className="preview-warning">Showing the first 256 KB. The final paragraph may be incomplete. Download for the full document.</div>}<MarkdownContent text={text} /></>;
}
export function MarkdownContent({ text }: { text: string }) {
  return <div className="markdown-scroll"><article className="markdown-preview"><ReactMarkdown skipHtml remarkPlugins={[remarkGfm]} components={{ a: props => <a href={props.href} target="_blank" rel="noopener noreferrer">{props.children}</a>, img: props => <span className="markdown-image-note">[Image: {props.alt || 'external image'}]</span> }}>{text || '(Empty file)'}</ReactMarkdown></article></div>;
}
