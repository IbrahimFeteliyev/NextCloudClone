// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import GroupAccess from './GroupAccess';
import OrganizationDialog from './OrganizationDialog';
import ShareDialog from './ShareDialog';
import type { Resource, User } from '../types';
const api = vi.hoisted(() => ({ request: vi.fn(), resourcePath: () => '/resources/folder/root' }));
vi.mock('../api', () => api);
let container: HTMLDivElement; let root: Root;
const catalog = { departments: [{ id: 'finance', name: 'Finance' }, { id: 'hr', name: 'HR' }], groups: [{ id: 'staff', name: 'All Staff', isAllStaff: true }, { id: 'finance-group', name: 'Finance', departmentId: 'finance' }] };
const administration = { catalog, users: [{ id: 'user', name: 'Sarah', email: 'sarah@test', role: 'Employee', departmentId: 'finance', isActive: true }], memberships: [{ userId: 'user', groupId: 'staff' }] };
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); HTMLDialogElement.prototype.showModal = function () { this.open = true; }; HTMLDialogElement.prototype.close = function () { this.open = false; };
  container = document.createElement('div'); document.body.append(container); root = createRoot(container); api.request.mockReset();
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
async function click(text: string) { const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent === text)!; expect(button).toBeTruthy(); await act(async () => button.click()); }
it('shows group sources and saves a group grant separately from individual permissions', async () => {
  api.request.mockImplementation(async (path: string) => path === '/organization' ? catalog : [{ groupId: 'staff', name: 'All Staff', permissions: 1, inherited: true, source: 'Parent folder' }]);
  await act(async () => root.render(<GroupAccess resource={{ id: 'root', type: 'folder', permissions: 15 } as Resource} onChange={vi.fn()} />));
  expect(container.textContent).toContain('Inherited from Parent folder');
  await click('Add group');
  const select = container.querySelector('select')!;
  await act(async () => { select.value = 'finance-group'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  await click('Save group access');
  expect(api.request).toHaveBeenCalledWith('/resources/folder/root/group-shares', { method: 'PUT', body: JSON.stringify({ groupId: 'finance-group', permissions: 1 }) });
});
it('keeps group mutation controls hidden for read-only users', async () => {
  api.request.mockImplementation(async (path: string) => path === '/organization' ? catalog : []);
  await act(async () => root.render(<GroupAccess resource={{ id: 'root', type: 'folder', permissions: 1 } as Resource} onChange={vi.fn()} />));
  expect(container.querySelector('select')).toBeNull(); expect(container.textContent).not.toContain('Save group access');
});
it('allows department assignment in the local admin dialog without changing roles', async () => {
  api.request.mockResolvedValue(administration);
  await act(async () => root.render(<OrganizationDialog onClose={vi.fn()} onChange={vi.fn()} />));
  expect(container.textContent).toContain('No AD or ERP connection'); expect(container.textContent).toContain('Employee');
  const select = container.querySelector<HTMLSelectElement>('[aria-label="Department for Sarah"]')!;
  await act(async () => { select.value = 'hr'; select.dispatchEvent(new Event('change', { bubbles: true })); });
  expect(api.request).toHaveBeenCalledWith('/organization/users/user/department', { method: 'PUT', body: JSON.stringify({ departmentId: 'hr' }) });
  expect(container.textContent).toContain('Group changes do not change employee roles');
});
it('opens team sharing on groups and separates inherited people from individual exceptions', async () => {
  const shares = [{ userId: 'user', name: 'Sarah', email: 'sarah@test', permissions: 3, inherited: true, source: 'Group membership · Finance' }];
  api.request.mockImplementation(async (path: string) => path === '/organization' ? catalog : path.endsWith('/group-shares') ? [] : shares);
  await act(async () => root.render(<ShareDialog resource={{ id: 'root', type: 'folder', name: 'Budget', owner: 'Finance', ownerId: 'storage', permissions: 15, teamSpaceId: 'team', sharedWith: shares } as Resource} users={[]} currentUser={{ id: 'admin' } as User} preview={false} onClose={vi.fn()} onChange={vi.fn()} />));
  expect(container.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe('Groups & departments');
  await click('People');
  expect(container.textContent).toContain('No individual permissions added.');
  expect(container.textContent).toContain('Access from groups or parent folders');
  expect(container.textContent).toContain('Can edit');
  expect(container.textContent).not.toContain('Team workspace');
});
it('uses the simple edit preset when saving group access and preserves the permission bitmask', async () => {
  api.request.mockImplementation(async (path: string) => path === '/organization' ? catalog : []);
  await act(async () => root.render(<GroupAccess resource={{ id: 'root', type: 'folder', permissions: 3 | 8 } as Resource} onChange={vi.fn()} />));
  await click('Add group');
  const groupSelect = container.querySelectorAll('select')[0]; const accessSelect = container.querySelectorAll('select')[1];
  await act(async () => { groupSelect.value = 'staff'; groupSelect.dispatchEvent(new Event('change', { bubbles: true })); });
  expect(accessSelect.querySelector<HTMLOptionElement>('option[value="15"]')?.disabled).toBe(true);
  await act(async () => { accessSelect.value = '3'; accessSelect.dispatchEvent(new Event('change', { bubbles: true })); });
  await click('Save group access');
  expect(api.request).toHaveBeenCalledWith('/resources/folder/root/group-shares', { method: 'PUT', body: JSON.stringify({ groupId: 'staff', permissions: 3 }) });
});
