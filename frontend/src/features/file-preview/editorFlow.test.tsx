// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import type { Root } from 'react-dom/client';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { EditorView } from '@codemirror/view';
import FilePreview from './FilePreview';
import type { PreviewTicket } from './content';
import type { Resource } from '../../types';

const api = vi.hoisted(() => ({ request: vi.fn(), readContent: vi.fn() }));
vi.mock('../../api', () => ({ request: api.request, download: vi.fn() }));
vi.mock('./content', () => ({ readContent: api.readContent }));

let container: HTMLDivElement; let root: Root; let close = vi.fn<() => void>();
const initial = '# Atlas\n\nOriginal draft.';
const ticket: PreviewTicket = { id: 'file', name: 'README.md', contentType: 'text/markdown', size: 24, version: 1, contentPath: '/files/file/content', expiresAt: '', permissions: 3 };
async function settle(check: () => void) {
  let failure: unknown;
  for (let attempt = 0; attempt < 30; attempt++) {
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 20)); });
    try { check(); return; } catch (error) { failure = error; }
  }
  throw failure;
}
async function open(overrides: Partial<PreviewTicket> = {}, text = initial) {
  const value = { ...ticket, ...overrides };
  api.request.mockResolvedValue(value);
  api.readContent.mockResolvedValue({ arrayBuffer: async () => new TextEncoder().encode(text).buffer });
  const resource = { ...value, type: 'file', ownerId: 'owner', owner: 'Owner', ownerEmail: '', modified: '', parentFolderId: null, sharedWith: [] } as Resource;
  await act(async () => root.render(<FilePreview resource={resource} onClose={close} />));
  await settle(() => { expect(container.querySelector('.text-editor-status')).not.toBeNull(); if (value.name.endsWith('.md')) expect(container.querySelector('h1')).not.toBeNull(); });
}
async function click(label: string) {
  const button = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(node => node.textContent === label || node.getAttribute('aria-label') === label)!;
  expect(button, label).toBeTruthy(); await act(async () => { button.click(); await new Promise(resolve => setTimeout(resolve, 20)); });
}
function view() { return EditorView.findFromDOM(container.querySelector('.cm-editor')!)!; }
async function append(text: string) { await act(async () => view().dispatch({ changes: { from: view().state.doc.length, insert: text } })); }
beforeAll(async () => { await import('./TextPreview'); await import('./MarkdownPreview'); });
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
  // jsdom has no layout engine; real layout is verified separately in the browser.
  Range.prototype.getClientRects = () => [] as unknown as DOMRectList;
  Range.prototype.getBoundingClientRect = () => new DOMRect();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container); close = vi.fn<() => void>();
  api.request.mockReset(); api.readContent.mockReset();
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('Document preview/edit flow', () => {
  it('retains Markdown drafts across Preview/Source and confirms close/cancel only when dirty', async () => {
    await open(); expect(container.querySelector('h1')?.textContent).toBe('Atlas'); expect(view().state.readOnly).toBe(true);
    const confirmation = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await click('Edit'); await append('\n\n## Unsaved'); await click('Preview');
    await settle(() => expect(container.querySelector('h2')?.textContent).toBe('Unsaved'));
    await click('Source'); expect(view().state.sliceDoc()).toContain('## Unsaved');
    const navigation = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(navigation); expect(navigation.defaultPrevented).toBe(true);
    await click('Close preview'); expect(close).not.toHaveBeenCalled(); expect(confirmation).toHaveBeenCalledOnce();
    await click('Cancel'); expect(view().state.sliceDoc()).toContain('## Unsaved');
    confirmation.mockReturnValue(true); await click('Cancel'); expect(view().state.sliceDoc()).toBe(initial); expect(view().state.readOnly).toBe(true);
    const cleanNavigation = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(cleanNavigation); expect(cleanNavigation.defaultPrevented).toBe(false);
    confirmation.mockClear(); await click('Close preview'); expect(close).toHaveBeenCalledOnce(); expect(confirmation).not.toHaveBeenCalled();
  });
  it('saves the retained Markdown source with its current version and returns to rendered preview', async () => {
    await open(); await click('Edit'); await append('\n\n## Saved'); await click('Preview');
    api.request.mockResolvedValueOnce({ ...ticket, version: 2, size: 35 });
    api.readContent.mockResolvedValueOnce({ arrayBuffer: async () => new TextEncoder().encode(initial + '\n\n## Saved').buffer });
    await click('Save');
    expect(api.request).toHaveBeenLastCalledWith('/files/file/content', { method: 'PUT', body: JSON.stringify({ content: initial + '\n\n## Saved', expectedVersion: 1 }) });
    await settle(() => expect(container.querySelector('.text-editor-status')?.textContent).toContain('Read-only'));
    expect(container.querySelector('h2')?.textContent).toBe('Saved'); expect(view().state.readOnly).toBe(true);
  });
  it('keeps READ-only source immutable and hides replacement even for Ctrl+H from the modal header', async () => {
    await open({ permissions: 1, name: 'config.json', contentType: 'application/json' }, '{"enabled":true}');
    expect(Array.from(container.querySelectorAll('button')).some(node => node.textContent === 'Edit')).toBe(false);
    await act(async () => container.querySelector('header button')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'h', ctrlKey: true, bubbles: true, cancelable: true })));
    expect(container.querySelector('.atlas-search')).not.toBeNull(); expect((container.querySelector('.atlas-search-replace') as HTMLElement).hidden).toBe(true);
    expect(view().state.readOnly).toBe(true); expect(view().contentDOM.getAttribute('contenteditable')).toBe('false');
    expect(api.request).toHaveBeenCalledOnce();
  });
  it('preserves rendered MIME-only Markdown while keeping unsupported extension saves unavailable', async () => {
    await open({ name: 'README', contentType: 'text/markdown' });
    await settle(() => expect(container.querySelector('h1')?.textContent).toBe('Atlas'));
    expect(Array.from(container.querySelectorAll('button')).some(node => node.textContent === 'Edit')).toBe(false);
  });
});
