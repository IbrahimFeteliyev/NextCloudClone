export type View = 'files' | 'shared' | 'shared-by-me' | 'favorites' | 'recent' | 'audit' | 'trash';
export type User = { id: string; name: string; email: string; role: string };
export type Share = { userId: string; name: string; email: string; permissions: number; inherited: boolean; source: string };
export type Resource = { id: string; name: string; contentType?: string | null; type: 'folder' | 'file'; ownerId: string; owner: string; ownerEmail: string; modified: string; size: number; parentFolderId: string | null; permissions: number; sharedWith: Share[]; isFavorite?: boolean };
export type Explorer = { folderId: string | null; permissions: number; breadcrumbs: { id: string; name: string }[]; items: Resource[]; total?: number };
export type TrashItem = { id: string; name: string; type: 'file' | 'folder'; size: number; deletedAt: string; retainUntil: string };
export type Stats = { usedBytes: number; fileCount: number; folderCount: number };
export type Audit = { id: number; user: string; email: string; action: string; timestamp: string; details: string; resourceType: string; resourceId: string | null };
export const READ = 1, WRITE = 2, DELETE = 4, SHARE = 8;
export const has = (permissions: number, bit: number) => (permissions & bit) === bit;
export const initials = (name: string) => name.split(' ').map(x => x[0]).slice(0, 2).join('').toUpperCase();
export function formatSize(bytes: number): string {
  if (!bytes) return '0 B';
  const unit = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), 3);
  return `${(bytes / 1024 ** unit).toLocaleString(undefined, { maximumFractionDigits: unit ? 1 : 0 })} ${['B', 'KB', 'MB', 'GB'][unit]}`;
}
export const fileCategory = (r: Pick<Resource, 'type' | 'name'>) => r.type === 'folder' ? 'Folders' : /\.(xlsx?|csv)$/i.test(r.name) ? 'Spreadsheets' : /\.(png|jpg|jpeg|svg|webp)$/i.test(r.name) ? 'Images' : 'Documents';
