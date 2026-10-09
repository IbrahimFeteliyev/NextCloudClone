// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import App from './App';
const api = vi.hoisted(() => ({ request: vi.fn(), download: vi.fn(), getToken: () => 'test', resourcePath: (r: { type: string; id: string }) => '/resources/' + r.type + '/' + r.id }));
vi.mock('./api', () => api);
vi.mock('./features/file-preview/FilePreview', () => ({ default: () => null }));
vi.mock('./features/uploads/UploadManager', () => ({ default: () => null }));
vi.mock('./features/uploads/useUploads', () => ({ useUploads: () => ({ entries: [], enqueue: vi.fn(), cancel: vi.fn(), retry: vi.fn(), clearFinished: vi.fn() }) }));
let container: HTMLDivElement; let root: Root; let favorite = false;
const user = { id: 'owner', name: 'Owner', email: 'owner@test.local', role: 'Employee' };
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); favorite = false;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container); api.request.mockReset();
  api.request.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path === '/auth/me') return user;
    if (path === '/auth/users') return [user];
    if (path === '/stats') return { usedBytes: 10, fileCount: 1, folderCount: 0 };
    if (path.endsWith('/favorite')) { favorite = JSON.parse(options!.body as string).isFavorite; return; }
    if (path.startsWith('/explorer')) return { folderId: null, permissions: 15, breadcrumbs: [], total: 45, items: [{ id: 'file', name: 'report.txt', type: 'file', ownerId: 'owner', owner: 'Owner', ownerEmail: user.email, permissions: 15, modified: '2026-10-08', size: 10, sharedWith: [], isFavorite: favorite }] };
    if (path.startsWith('/audit/page')) return { items: [], total: 45 };
    if (path === '/trash') return [{ id: 'deleted', name: 'deleted.txt', type: 'file', size: 10, deletedAt: '2026-10-08', retainUntil: '2026-11-07' }];
  });
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
async function click(label: string) {
  const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent === label || b.getAttribute('aria-label') === label)!;
  expect(button).toBeTruthy(); await act(async () => { button.click(); });
}
async function open() { await act(async () => root.render(<App />)); }
it('removes the sidebar promotion and toggles favorites directly on the file', async () => {
  await open(); expect(container.textContent).not.toContain('Better, together.');
  expect(container.querySelector('[aria-label="Select all files"]')).toBeNull();
  await click('Add report.txt to favorites'); expect(favorite).toBe(true);
  await click('Remove report.txt from favorites'); expect(favorite).toBe(false);
});
it('requests successive server pages for Recent and Audit Logs', async () => {
  await open(); await click('Recent'); await click('Next');
  expect(api.request.mock.calls.some(([path]) => path.includes('view=recent') && path.includes('page=2'))).toBe(true);
  await click('Audit Logs'); expect(container.textContent).toContain('Page 1 of 3'); await click('Next');
  expect(api.request).toHaveBeenCalledWith('/audit/page?page=2&pageSize=20');
});
it('shows retention details and restores deleted items', async () => {
  await open(); await click('Trash'); expect(container.textContent).toContain('Automatic deletion is disabled');
  expect(container.textContent).toContain('deleted.txt'); await click('Actions for deleted.txt'); await click('Restore');
  expect(api.request).toHaveBeenCalledWith('/trash/file/deleted/restore', { method: 'POST' });
});
it('uses explorer filters and grid/list controls in Trash', async () => {
  await open(); await click('Trash'); await click('Grid view'); expect(container.querySelector('.resource-grid')).not.toBeNull();
  await click('Folders'); expect(container.textContent).toContain('No matching deleted items');
  await click('Files'); await click('List view'); expect(container.querySelector('.trash-panel .file-table')).not.toBeNull();
});
