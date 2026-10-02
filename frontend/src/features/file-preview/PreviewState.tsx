import { LoaderCircle } from 'lucide-react';
export function PreviewLoading() { return <div className="preview-placeholder" role="status"><LoaderCircle className="spin" size={26} /><p>Loading preview…</p></div>; }
export function PreviewError({ message }: { message: string }) { return <div className="preview-placeholder" role="alert"><h2>Unable to open preview</h2><p>{message}</p></div>; }
