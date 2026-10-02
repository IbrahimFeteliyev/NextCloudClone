import { describe, expect, it } from 'vitest';
import { DELETE, READ, SHARE, WRITE, has, formatSize } from './types';
import { previewExplorer } from './preview';
describe('permission-driven controls', () => {
  it('read-only grants allow viewing and disable mutating controls', () => { expect(has(READ, READ)).toBe(true); for (const bit of [WRITE, DELETE, SHARE]) expect(has(READ, bit)).toBe(false); });
  it('READ + WRITE enables uploads without deletion or resharing', () => { expect(has(READ | WRITE, WRITE)).toBe(true); expect(has(READ | WRITE, DELETE)).toBe(false); expect(has(READ | WRITE, SHARE)).toBe(false); });
});
describe('read-only interface preview', () => {
  it('shows a shared folder and restricts its permissions', () => { const shared = previewExplorer('shared', null, ''); expect(shared.items[0].name).toBe('Operations'); const nested = previewExplorer('shared', shared.items[0].id, ''); expect(nested.permissions).toBe(READ); expect(nested.breadcrumbs.map(x => x.name)).toEqual(['Operations']); });
  it('navigates nested folder breadcrumbs', () => { expect(previewExplorer('files', 'budget-folder', '').breadcrumbs.map(x => x.name)).toEqual(['My Files', 'Finance', 'Budget']); });
  it('filters a workspace search', () => { const data = previewExplorer('files', null, 'Budget'); expect(data.items.length).toBeGreaterThan(0); expect(data.items.every(x => x.name.toLowerCase().includes('budget'))).toBe(true); });
});
it('formats file sizes consistently', () => { expect(formatSize(0)).toBe('0 B'); expect(formatSize(1024)).toBe('1 KB'); expect(formatSize(1024 ** 2)).toBe('1 MB'); });
