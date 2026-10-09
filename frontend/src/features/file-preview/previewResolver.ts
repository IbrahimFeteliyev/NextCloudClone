import { resolveTextFileType } from './textFileTypes';
export type PreviewKind = 'video' | 'image' | 'pdf' | 'markdown' | 'text' | 'csv' | 'office' | 'unsupported';
const officeMimes = new Set(['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'application/vnd.openxmlformats-officedocument.presentationml.presentation']);
const extensions: Record<string, PreviewKind> = { mp4: 'video', m4v: 'video', webm: 'video', ogg: 'video', ogv: 'video', mov: 'video', jpg: 'image', jpeg: 'image', png: 'image', webp: 'image', gif: 'image', svg: 'image', pdf: 'pdf', md: 'markdown', markdown: 'markdown', txt: 'text', log: 'text', csv: 'csv', docx: 'office', xlsx: 'office', pptx: 'office' };
export function resolvePreview(file: { name: string; contentType?: string | null }): PreviewKind {
  const textType = resolveTextFileType(file);
  if (textType) return textType.renderedPreview ? 'markdown' : 'text';
  const mime = file.contentType?.split(';')[0].trim().toLowerCase();
  const extension = file.name.split('.').at(-1)?.toLowerCase() || '';
  if (mime?.startsWith('video/')) return 'video';
  if (mime?.startsWith('image/')) return 'image';
  if (mime === 'application/pdf') return 'pdf';
  if (mime === 'text/markdown' || mime === 'text/x-markdown') return 'markdown';
  if (mime === 'text/csv' || mime === 'application/csv') return 'csv';
  if (extension === 'csv' && mime === 'application/vnd.ms-excel') return 'csv';
  if ((extension === 'ogg' || extension === 'ogv') && (mime === 'application/ogg' || mime === 'audio/ogg')) return 'video';
  // Browsers commonly upload Markdown and CSV as plain text.
  if (mime === 'text/plain') return extension === 'md' || extension === 'markdown' ? 'markdown' : extension === 'csv' ? 'csv' : 'text';
  if (mime && officeMimes.has(mime)) return 'office';
  if (!mime || mime === 'application/octet-stream') return extensions[extension] || 'unsupported';
  return 'unsupported';
}
