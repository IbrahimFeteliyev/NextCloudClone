// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import VersionHistory from './VersionHistory';
import type { Resource } from '../types';
const api = vi.hoisted(() => ({ request: vi.fn(), download: vi.fn() }));
vi.mock('../api', () => api);
let container: HTMLDivElement; let root: Root;
const versions = [2, 1].map(number => ({ number, size: 10, createdAt: '2026-10-08T10:00:00Z', createdBy: 'Owner', isCurrent: number === 2, currentVersion: 2 }));
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  api.request.mockReset(); api.request.mockResolvedValue(versions); api.download.mockReset(); api.download.mockResolvedValue(undefined);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
async function open(permissions: number) {
  await act(async () => root.render(<VersionHistory resource={{ id: 'file', name: 'report.txt', permissions } as Resource} onClose={vi.fn()} onChange={vi.fn()} />));
}
async function click(text: string) {
  const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent === text || b.getAttribute('aria-label') === text)!;
  expect(button).toBeTruthy(); await act(async () => button.click());
}
it('allows readers to download historical content and hides restore', async () => {
  await open(1); expect(container.textContent).toContain('Current'); expect(container.textContent).toContain('read-only');
  expect(container.textContent).not.toContain('Restore v1');
  await act(async () => (container.querySelector('[aria-label="Download version 1"]') as HTMLButtonElement).click());
  expect(api.download).toHaveBeenCalledWith('file', 'report.txt', 1);
});
it('requires confirmation and sends the current version for optimistic concurrency', async () => {
  await open(3); await click('Restore v1');
  expect(api.request).toHaveBeenCalledTimes(1);
  await click('Confirm restore');
  expect(api.request).toHaveBeenCalledWith('/files/file/versions/1/restore', { method: 'POST', body: JSON.stringify({ expectedVersion: 2 }) });
  expect(container.textContent).toContain('Saved versions are preserved');
});
it('shows backend errors without losing history', async () => {
  await open(3); await click('Restore v1'); api.request.mockRejectedValueOnce(new Error('The file changed. Refresh history before restoring.'));
  await click('Confirm restore'); expect(container.querySelector('[role="alert"]')?.textContent).toContain('The file changed');
  expect(container.textContent).toContain('v2');
});
it('creates a version only when explicitly requested', async () => {
  await open(3); expect(api.request).toHaveBeenCalledTimes(1);
  await click('Save current version');
  expect(api.request).toHaveBeenCalledWith('/files/file/versions', { method: 'POST' });
  expect(container.textContent).toContain('Current content saved as a version');
});
it('requires confirmation before deleting a saved version', async () => {
  await open(15); await click('Delete version 1'); expect(api.request).toHaveBeenCalledTimes(1);
  await click('Confirm delete version');
  expect(api.request).toHaveBeenCalledWith('/files/file/versions/1', { method: 'DELETE' });
  expect(container.textContent).toContain('Current file is unchanged');
});
