import type { Audit, Explorer, Resource, Share, User, View } from './types';
export const previewUser: User = { id: 'finance', name: 'Sarah Wilson', email: 'finance@demo.local', role: 'Employee' };
export const previewUsers: User[] = [previewUser, { id: 'manager', name: 'James Chen', email: 'manager@demo.local', role: 'Manager' }, { id: 'employee', name: 'Emma Davis', email: 'employee@demo.local', role: 'Employee' }, { id: 'admin', name: 'Alex Morgan', email: 'admin@demo.local', role: 'Admin' }];
const share = (id: string, p: number, source: string, inherited = false): Share => ({ userId: id, name: previewUsers.find(u => u.id === id)!.name, email: previewUsers.find(u => u.id === id)!.email, permissions: p, source, inherited });
const item = (id: string, name: string, type: 'file' | 'folder', size = 0, parent = 'root', sharedWith: Share[] = [], owner = previewUser, days = 0): Resource => ({ id, name, type, size, parentFolderId: parent, ownerId: owner.id, owner: owner.name, ownerEmail: owner.email, modified: new Date(Date.now() - days * 86400000).toISOString(), permissions: owner.id === 'finance' ? 15 : 1, sharedWith });
const previewItems: Resource[] = [
  item('finance-folder', 'Finance', 'folder', 0, 'root', [share('manager', 3, 'Finance'), share('employee', 1, 'Finance')]),
  item('projects', 'Projects', 'folder', 0, 'root', [share('manager', 3, 'Projects'), share('employee', 3, 'Projects')]),
  item('documents', 'Documents', 'folder', 0, 'root'),
  item('brand', 'Brand assets', 'folder', 0, 'root', [share('employee', 1, 'Brand assets')]),
  item('report', 'Q3 financial report.pdf', 'file', 2457600, 'root', [share('manager', 1, 'Q3 financial report.pdf')], previewUser, 1),
  item('budget', 'Budget overview 2027.xlsx', 'file', 348160, 'root', [share('manager', 3, 'Budget overview 2027.xlsx'), share('employee', 1, 'Budget overview 2027.xlsx')], previewUser, 1),
  item('plan', 'Project roadmap.docx', 'file', 184320, 'root', [], previewUser, 2),
  item('notes', 'Team meeting notes.md', 'file', 6144, 'root', [share('employee', 3, 'Team meeting notes.md')], previewUser, 3),
  item('expenses', 'Expense summary.csv', 'file', 47104, 'root', [], previewUser, 4),
  item('budget-folder', 'Budget', 'folder', 0, 'finance-folder', [share('manager', 3, 'Finance', true)]),
  item('child-budget', 'Budget_2027.xlsx', 'file', 145408, 'budget-folder', [share('manager', 3, 'Finance', true)]),
  item('project-note', 'Project kickoff.md', 'file', 8192, 'projects'),
  item('welcome', 'Welcome to Atlas.txt', 'file', 1024, 'documents'),
  item('logo', 'Brand guidelines.pdf', 'file', 1572864, 'brand'),
  item('shared-project', 'Operations', 'folder', 0, 'manager-root', [share('finance', 1, 'Operations')], previewUsers[1]),
  item('shared-file', 'Operations handbook.pdf', 'file', 987136, 'shared-project', [share('finance', 1, 'Operations', true)], previewUsers[1]),
];
export function previewExplorer(view: View, folderId: string | null, search: string): Explorer {
  const folder = folderId ?? (view === 'files' ? 'root' : null);
  const breadcrumbs: Explorer['breadcrumbs'] = [];
  let current = folder;
  while (current && current !== 'manager-root') {
    if (current === 'root') { breadcrumbs.unshift({ id: 'root', name: 'My Files' }); break; }
    const f = previewItems.find(x => x.id === current); if (!f) break;
    breadcrumbs.unshift({ id: f.id, name: f.name }); current = f.parentFolderId;
  }
  const scope = folder ? previewItems : view === 'favorites' ? previewItems.filter(x => ['report', 'documents'].includes(x.id)).map(x => ({ ...x, isFavorite: true })) : view === 'shared-by-me' ? previewItems.filter(x => x.ownerId === previewUser.id && x.sharedWith.some(s => !s.inherited && s.permissions > 0)) : previewItems;
  const items = search ? scope.filter(x => x.name.toLowerCase().includes(search.toLowerCase()))
    : folder ? previewItems.filter(x => x.parentFolderId === folder)
    : view === 'shared' ? previewItems.filter(x => x.id === 'shared-project')
    : view === 'favorites' || view === 'shared-by-me' ? scope
    : previewItems.filter(x => x.type === 'file').sort((a, b) => b.modified.localeCompare(a.modified));
  return { folderId: folder, permissions: folder === 'shared-project' ? 1 : folder ? 15 : 0, breadcrumbs, items };
}
export const previewAudit: Audit[] = [
  { id: 4, user: 'Sarah Wilson', email: 'finance@demo.local', action: 'CHANGE_PERMISSION', timestamp: new Date().toISOString(), details: 'Set manager@demo.local access to Read, Write', resourceType: 'folder', resourceId: 'finance-folder' },
  { id: 3, user: 'James Chen', email: 'manager@demo.local', action: 'DOWNLOAD_FILE', timestamp: new Date(Date.now() - 3600000).toISOString(), details: 'Downloaded Budget_2027.xlsx', resourceType: 'file', resourceId: 'child-budget' },
  { id: 2, user: 'Sarah Wilson', email: 'finance@demo.local', action: 'SHARE_FOLDER', timestamp: new Date(Date.now() - 7200000).toISOString(), details: 'Set manager@demo.local access to Read', resourceType: 'folder', resourceId: 'finance-folder' },
  { id: 1, user: 'Sarah Wilson', email: 'finance@demo.local', action: 'UPLOAD_FILE', timestamp: new Date(Date.now() - 10800000).toISOString(), details: 'Uploaded Budget_2027.xlsx to Budget', resourceType: 'file', resourceId: 'child-budget' },
];
