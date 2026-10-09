// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import type { Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { uploadFile } from './uploadService';
import { useUploads } from './useUploads';
import UploadManager from './UploadManager';

class TestXHR extends EventTarget {
  static requests: TestXHR[] = [];
  upload = new EventTarget(); status = 0; responseText = ''; timeout = 0; body?: FormData; headers: Record<string, string> = {}; aborted = false;
  open(method: string, url: string) { expect(method).toBe('POST'); expect(url).toMatch(/\/api\/files\/upload$/); }
  setRequestHeader(key: string, value: string) { this.headers[key] = value; }
  send(body: FormData) { this.body = body; TestXHR.requests.push(this); }
  abort() { this.aborted = true; this.dispatchEvent(new Event('abort')); }
  progress(percent: number) { this.upload.dispatchEvent(new ProgressEvent('progress', { lengthComputable: true, loaded: percent, total: 100 })); }
  finish(status = 200, value = { id: 'saved-id' } as object) { this.status = status; this.responseText = JSON.stringify(value); this.dispatchEvent(new Event('load')); }
}
let root: Root | undefined; let host: HTMLDivElement | undefined;
beforeEach(() => { TestXHR.requests = []; vi.stubGlobal('XMLHttpRequest', TestXHR); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); sessionStorage.setItem('atlas-token', 'session'); });
afterEach(async () => { if (root) await act(async () => root!.unmount()); root = undefined; host?.remove(); host = undefined; vi.unstubAllGlobals(); sessionStorage.clear(); });
const file = (name = 'sample.txt') => new File(['a'.repeat(1000)], name, { type: 'text/plain' });
it('waits for explicit Replace confirmation and sends the observed file/revision', async () => {
  let queue!: ReturnType<typeof useUploads>;
  function Demo() { queue = useUploads('user', vi.fn()); return <UploadManager {...queue} />; }
  host = document.createElement('div'); document.body.append(host); root = createRoot(host);
  await act(async () => root!.render(<Demo />));
  await act(async () => queue.enqueue([file()], 'folder', 'Documents'));
  await act(async () => TestXHR.requests[0].finish(409, { message: 'File exists.', details: { canReplace: true, fileId: 'existing-file', version: 4 } }));
  expect(queue.entries[0].status).toBe('Conflict'); expect(TestXHR.requests).toHaveLength(1);
  await act(async () => Array.from(host!.querySelectorAll('button')).find(b => b.textContent === 'Replace existing file')!.click());
  expect(TestXHR.requests).toHaveLength(1);
  await act(async () => Array.from(host!.querySelectorAll('button')).find(b => b.textContent === 'Confirm replace')!.click());
  expect(TestXHR.requests).toHaveLength(2); expect(TestXHR.requests[1].body?.get('replace')).toBe('true');
  expect(TestXHR.requests[1].body?.get('replaceFileId')).toBe('existing-file'); expect(TestXHR.requests[1].body?.get('expectedVersion')).toBe('4');
  await act(async () => TestXHR.requests[1].finish()); expect(queue.entries[0].status).toBe('Completed');
});
describe('Upload transport', () => {
  it('reports independent byte progress and resolves only after successful server confirmation', async () => {
    const progress = vi.fn(); let done = false;
    const result = uploadFile(file(), 'folder-id', { signal: new AbortController().signal, onProgress: progress }); result.then(() => { done = true; });
    const xhr = TestXHR.requests[0]; expect(xhr.headers.Authorization).toBe('Bearer session'); expect(xhr.body!.get('parentFolderId')).toBe('folder-id');
    expect(xhr.headers['Content-Type']).toBeUndefined(); xhr.progress(25); expect(progress).toHaveBeenLastCalledWith(250);
    xhr.progress(100); await Promise.resolve(); expect(done).toBe(false);
    xhr.finish(); expect((await result).id).toBe('saved-id'); expect(done).toBe(true);
  });
  it('rejects a 100% transfer when the backend rejects it and exposes its useful message', async () => {
    const result = uploadFile(file(), 'folder', { signal: new AbortController().signal, onProgress: vi.fn() });
    TestXHR.requests[0].progress(100); TestXHR.requests[0].finish(403, { message: 'WRITE permission is required.' });
    await expect(result).rejects.toMatchObject({ status: 403, message: 'WRITE permission is required.' });
  });
  it('aborts the active HTTP request and rejects cancellation', async () => {
    const controller = new AbortController(); const result = uploadFile(file(), 'folder', { signal: controller.signal, onProgress: vi.fn() });
    controller.abort(); expect(TestXHR.requests[0].aborted).toBe(true); await expect(result).rejects.toMatchObject({ name: 'AbortError' });
  });
  it('handles network failures and invalid successful responses', async () => {
    const a = uploadFile(file(), 'folder', { signal: new AbortController().signal, onProgress: vi.fn() });
    TestXHR.requests[0].dispatchEvent(new Event('error')); await expect(a).rejects.toMatchObject({ status: 0 });
    const b = uploadFile(file(), 'folder', { signal: new AbortController().signal, onProgress: vi.fn() });
    TestXHR.requests[1].finish(200, {}); await expect(b).rejects.toThrow('Upload failed');
  });
  it('expires the session on unauthorized responses', async () => {
    const expired = vi.fn(); window.addEventListener('atlas-session-expired', expired);
    const result = uploadFile(file(), 'folder', { signal: new AbortController().signal, onProgress: vi.fn() }); TestXHR.requests[0].finish(401, { message: 'Sign in.' });
    await expect(result).rejects.toMatchObject({ status: 401 }); expect(expired).toHaveBeenCalledOnce(); expect(sessionStorage.getItem('atlas-token')).toBeNull(); window.removeEventListener('atlas-session-expired', expired);
  });
});
describe('Upload queue and panel', () => {
  it('shows separate progress, waiting cancellation, failed retry, completion refresh and clear finished', async () => {
    let queue!: ReturnType<typeof useUploads>; const refresh = vi.fn();
    function Demo() { queue = useUploads('user', refresh); return <UploadManager {...queue} />; }
    host = document.createElement('div'); document.body.append(host); root = createRoot(host);
    await act(async () => root!.render(<Demo />)); await act(async () => queue.enqueue([file('one.txt'), file('two.txt'), file('three.txt')], 'folder', 'Documents'));
    expect(TestXHR.requests).toHaveLength(2); expect(queue.entries.map(e => e.status)).toEqual(['Uploading', 'Uploading', 'Waiting']);
    await act(async () => { TestXHR.requests[0].progress(50); TestXHR.requests[1].progress(10); });
    expect(queue.entries.map(e => e.uploaded)).toEqual([500, 100, 0]); expect(host.querySelectorAll('[role="progressbar"]')[0].getAttribute('aria-valuenow')).toBe('50');
    await act(async () => queue.cancel(queue.entries[2].id)); expect(TestXHR.requests).toHaveLength(2); expect(queue.entries[2].status).toBe('Cancelled');
    await act(async () => { TestXHR.requests[0].progress(100); TestXHR.requests[0].finish(409, { message: 'Name already exists.' }); });
    expect(queue.entries[0].status).toBe('Failed'); expect(host.textContent).toContain('Name already exists.'); expect(refresh).not.toHaveBeenCalled();
    await act(async () => { const retry = host!.querySelector<HTMLButtonElement>('[aria-label="Retry upload one.txt"]')!; retry.click(); });
    expect(TestXHR.requests).toHaveLength(3); expect(queue.entries[0].uploaded).toBe(0);
    await act(async () => { TestXHR.requests[2].progress(100); }); expect(queue.entries[0].status).toBe('Uploading'); expect(host.textContent).toContain('Waiting for server confirmation');
    await act(async () => TestXHR.requests[2].finish()); expect(queue.entries[0].status).toBe('Completed'); expect(refresh).toHaveBeenCalledOnce();
    await act(async () => queue.cancel(queue.entries[1].id)); expect(TestXHR.requests[1].aborted).toBe(true);
    await act(async () => queue.clearFinished()); expect(queue.entries).toHaveLength(0); expect(host.textContent).toBe('');
  });
  it('aborts active transfers and removes file details on logout', async () => {
    let queue!: ReturnType<typeof useUploads>; const refresh = vi.fn();
    function Demo({ session }: { session?: string }) { queue = useUploads(session, refresh); return <UploadManager {...queue} />; }
    host = document.createElement('div'); document.body.append(host); root = createRoot(host);
    await act(async () => root!.render(<Demo session="owner" />)); await act(async () => queue.enqueue([file()], 'folder', 'Documents'));
    await act(async () => root!.render(<Demo />)); expect(TestXHR.requests[0].aborted).toBe(true); expect(queue.entries).toHaveLength(0); expect(refresh).not.toHaveBeenCalled();
  });
});
