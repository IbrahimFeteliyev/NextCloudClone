import { ApiError, getToken } from '../../api';
export type PreviewTicket = { id: string; name: string; contentType: string; size: number; version: number; contentPath: string; expiresAt: string };
export const apiBase = (import.meta.env.VITE_API_URL || 'http://localhost:5050/api').replace(/\/$/, '');
export const contentUrl = (ticket: PreviewTicket) => `${apiBase}${ticket.contentPath}`;
export async function readContent(ticket: PreviewTicket, signal: AbortSignal, limit?: number): Promise<Blob> {
  const headers = new Headers({ Authorization: `Bearer ${getToken()}` });
  if (limit && ticket.size > limit) headers.set('Range', `bytes=0-${limit - 1}`);
  const response = await fetch(`${apiBase}/files/${ticket.id}/content`, { headers, signal });
  if (!response.ok) {
    if (response.status === 401) { sessionStorage.removeItem('atlas-token'); window.dispatchEvent(new Event('atlas-session-expired')); }
    const error = await response.json().catch(() => ({}));
    throw new ApiError(error.message || `Preview could not be loaded (${response.status}).`, response.status);
  }
  return response.blob();
}
export async function readText(ticket: PreviewTicket, signal: AbortSignal, limit: number) {
  const buffer = await (await readContent(ticket, signal, limit)).arrayBuffer();
  const bytes = new Uint8Array(buffer);
  const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf-16le' : bytes[0] === 0xfe && bytes[1] === 0xff ? 'utf-16be' : 'utf-8';
  return new TextDecoder(encoding).decode(buffer);
}
