import { ApiError, apiBase, getToken } from '../../api';
import type { Resource } from '../../types';

export const MAX_UPLOAD_BYTES = 100 * 1024 * 1024;
export type Replacement = { fileId: string; version: number };
export class UploadConflict extends ApiError { constructor(message: string, public replacement: Replacement) { super(message, 409); } }
type Options = { signal: AbortSignal; onProgress: (uploadedBytes: number) => void; replacement?: Replacement };

// XHR exposes real upload progress; resolution waits for the API's persisted resource.
export function uploadFile(file: File, parentFolderId: string, { signal, onProgress, replacement }: Options): Promise<Resource> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) { reject(new DOMException('Upload cancelled.', 'AbortError')); return; }
    if (file.size > MAX_UPLOAD_BYTES) { reject(new ApiError('Files must be 100 MB or smaller.', 413)); return; }
    const xhr = new XMLHttpRequest(); let settled = false;
    const abort = () => xhr.abort();
    const finish = (error?: Error, resource?: Resource) => {
      if (settled) return; settled = true; signal.removeEventListener('abort', abort);
      if (error) reject(error); else resolve(resource!);
    };
    xhr.upload.addEventListener('progress', event => {
      if (!settled && event.lengthComputable) onProgress(Math.min(file.size, Math.round(file.size * event.loaded / event.total)));
    });
    xhr.addEventListener('load', () => {
      let data: { message?: string; title?: string; id?: string; details?: Replacement & { canReplace: boolean } } = {};
      try { data = JSON.parse(xhr.responseText); } catch { /* Invalid server responses are failures. */ }
      if (xhr.status >= 200 && xhr.status < 300 && data.id) finish(undefined, data as Resource);
      else {
        if (xhr.status === 401) { sessionStorage.removeItem('atlas-token'); window.dispatchEvent(new Event('atlas-session-expired')); }
        if (xhr.status === 409 && data.details?.canReplace && data.details.fileId && Number.isInteger(data.details.version)) finish(new UploadConflict(data.message || 'File already exists.', data.details));
        else finish(new ApiError(data.message || data.title || `Upload failed (${xhr.status}).`, xhr.status));
      }
    });
    xhr.addEventListener('error', () => finish(new ApiError('Unable to reach Atlas. Check your connection and the API, then retry.', 0)));
    xhr.addEventListener('timeout', () => finish(new ApiError('Upload timed out. Check your connection, then retry.', 0)));
    xhr.addEventListener('abort', () => finish(new DOMException('Upload cancelled.', 'AbortError')));
    try {
      xhr.open('POST', `${apiBase}/files/upload`); xhr.timeout = 10 * 60 * 1000;
      const token = getToken(); if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
      const body = new FormData(); body.set('parentFolderId', parentFolderId); body.set('file', file);
      if (replacement) { body.set('replace', 'true'); body.set('replaceFileId', replacement.fileId); body.set('expectedVersion', String(replacement.version)); }
      signal.addEventListener('abort', abort, { once: true }); xhr.send(body);
    } catch (e) { finish(e as Error); }
  });
}
