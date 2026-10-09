import { useEffect, useRef, useState } from 'react';
import { MAX_UPLOAD_BYTES, uploadFile, UploadConflict, type Replacement } from './uploadService';
export type UploadStatus = 'Waiting' | 'Uploading' | 'Completed' | 'Failed' | 'Cancelled' | 'Conflict';
export type UploadEntry = { id: string; file: File; parentFolderId: string; destination: string; status: UploadStatus; uploaded: number; error?: string; replacement?: Replacement };
const CONCURRENCY = 2;

export function useUploads(sessionKey: string | undefined, onCompleted: () => void) {
  const [entries, setEntries] = useState<UploadEntry[]>([]);
  const items = useRef(entries); const active = useRef(new Map<string, AbortController>());
  const completed = useRef(onCompleted); completed.current = onCompleted;
  const generation = useRef(0);
  function update(transform: (old: UploadEntry[]) => UploadEntry[]) { items.current = transform(items.current); setEntries(items.current); }
  function change(id: string, patch: Partial<UploadEntry>) { update(old => old.map(entry => entry.id === id ? { ...entry, ...patch } : entry)); }
  useEffect(() => {
    generation.current++; for (const controller of active.current.values()) controller.abort(); active.current.clear(); update(() => []);
    return () => { generation.current++; for (const controller of active.current.values()) controller.abort(); active.current.clear(); };
  }, [sessionKey]);
  useEffect(() => {
    for (const item of items.current) {
      if (active.current.size >= CONCURRENCY) break;
      if (item.status !== 'Waiting' || active.current.has(item.id)) continue;
      const controller = new AbortController(); const epoch = generation.current;
      active.current.set(item.id, controller); change(item.id, { status: 'Uploading', uploaded: 0 });
      uploadFile(item.file, item.parentFolderId, { signal: controller.signal, replacement: item.replacement, onProgress: uploaded => {
        if (epoch === generation.current && !controller.signal.aborted) change(item.id, { uploaded });
      } }).then(() => {
        if (epoch !== generation.current) return;
        change(item.id, { status: 'Completed', uploaded: item.file.size, error: undefined }); completed.current();
      }).catch(error => {
        if (epoch !== generation.current) return;
        change(item.id, { status: error.name === 'AbortError' ? 'Cancelled' : error instanceof UploadConflict ? 'Conflict' : 'Failed', replacement: error instanceof UploadConflict ? error.replacement : undefined, error: error.name === 'AbortError' ? undefined : error.message });
      }).finally(() => {
        if (epoch !== generation.current) return;
        active.current.delete(item.id); update(old => [...old]);
      });
    }
  }, [entries]);
  function enqueue(files: File[], parentFolderId: string, destination: string) {
    update(old => [...old, ...files.map(file => ({ id: crypto.randomUUID(), file, parentFolderId, destination, uploaded: 0,
      status: (file.size > MAX_UPLOAD_BYTES ? 'Failed' : 'Waiting') as UploadStatus,
      error: file.size > MAX_UPLOAD_BYTES ? 'Files must be 100 MB or smaller.' : undefined }))]);
  }
  function cancel(id: string) {
    const item = items.current.find(entry => entry.id === id);
    if (!item || !['Waiting', 'Uploading', 'Conflict'].includes(item.status)) return;
    active.current.get(id)?.abort(); change(id, { status: 'Cancelled', error: undefined });
  }
  function retry(id: string) {
    const item = items.current.find(entry => entry.id === id);
    if (item?.status === 'Failed') change(id, { status: 'Waiting', uploaded: 0, error: undefined, replacement: undefined });
  }
  function replace(id: string) {
    const item = items.current.find(entry => entry.id === id);
    if (item?.status === 'Conflict' && item.replacement) change(id, { status: 'Waiting', uploaded: 0, error: undefined });
  }
  const clearFinished = () => update(old => old.filter(item => !['Completed', 'Cancelled'].includes(item.status)));
  return { entries, enqueue, cancel, retry, replace, clearFinished };
}
