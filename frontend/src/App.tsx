import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Activity, ArrowDown, ArrowDownUp, ArrowRight, Check, ChevronDown, ChevronRight, Clock3, CloudUpload, Download, Folder, FolderInput, FolderPlus, HardDrive, LayoutGrid, List, LoaderCircle, LogOut, Menu, MoreHorizontal, Pencil, Search, ShieldCheck, SlidersHorizontal, Star, Share2, Trash2, UsersRound, X } from 'lucide-react';
import { download, getToken, request, resourcePath } from './api';
import type { Audit, Explorer, Resource, Stats, TrashItem, User, View } from './types';
import { DELETE, SHARE, WRITE, fileCategory, formatSize, has } from './types';
import { previewAudit, previewExplorer, previewUser, previewUsers } from './preview';
import { AccessBadge, Avatar, ErrorMessage, FileIcon, Logo, Modal, SharedAvatars } from './components/UI';
import Login from './components/Login';
import VersionHistory from './components/VersionHistory';
import TrashPanel from './components/TrashPanel';
import ShareDialog from './components/ShareDialog';
import MoveDialog from './components/MoveDialog';
import FilePreview from './features/file-preview/FilePreview';
import UploadManager from './features/uploads/UploadManager';
import { useUploads } from './features/uploads/useUploads';

const views: { id: View; name: string; description: string; icon: typeof Folder }[] = [
  { id: 'files', name: 'My Files', description: 'Your documents, organized and ready to share.', icon: Folder },
  { id: 'favorites', name: 'Favorites', description: 'Keep your starred files and folders close.', icon: Star },
  { id: 'shared', name: 'Shared With Me', description: 'A shared space for your team’s best work.', icon: UsersRound },
  { id: 'shared-by-me', name: 'Shared by me', description: 'Files and folders you own and share with your team.', icon: Share2 },
  { id: 'recent', name: 'Recent', description: 'Pick up where you left off.', icon: Clock3 },
  { id: 'trash', name: 'Trash', description: 'Deleted items are kept for 30 days. Automatic deletion is disabled.', icon: Trash2 },
  { id: 'audit', name: 'Audit Logs', description: 'A clear record of what happens in your workspace.', icon: Activity },
];
type Dialog = { kind: 'create' } | { kind: 'rename' | 'delete' | 'move' | 'share' | 'versions'; resource: Resource } | { kind: 'help' };
const emptyExplorer: Explorer = { folderId: null, permissions: 0, breadcrumbs: [], items: [] };
const date = (value: string) => new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });

export default function App() {
  const [preview, setPreview] = useState(new URLSearchParams(location.search).get('preview') === '1');
  const [previewFile, setPreviewFile] = useState<Resource | null>(null);
  const [user, setUser] = useState<User | null>(null); const [booting, setBooting] = useState(!!getToken());
  const [view, setView] = useState<View>('files'); const [folderId, setFolderId] = useState<string | null>(null);
  const [search, setSearch] = useState(''); const [query, setQuery] = useState('');
  const [data, setData] = useState<Explorer>(emptyExplorer); const [users, setUsers] = useState<User[]>([]);
  const [stats, setStats] = useState<Stats>({ usedBytes: 0, fileCount: 0, folderCount: 0 });
  const [page, setPage] = useState(1); const [auditTotal, setAuditTotal] = useState(0); const [trash, setTrash] = useState<TrashItem[]>([]);
  const [audits, setAudits] = useState<Audit[]>([]); const [loading, setLoading] = useState(false);
  const [error, setError] = useState(''); const [toast, setToast] = useState(''); const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState<Dialog | null>(null); const [input, setInput] = useState(''); const [dialogError, setDialogError] = useState('');
  const [category, setCategory] = useState('All files'); const [sort, setSort] = useState('name'); const [layout, setLayout] = useState<'list' | 'grid'>('list');
  const [menu, setMenu] = useState<string | null>(null); const [account, setAccount] = useState(false); const [sidebar, setSidebar] = useState(false);
  const [dragging, setDragging] = useState(false); const dragCount = useRef(0);
  const fileInput = useRef<HTMLInputElement>(null); const searchInput = useRef<HTMLInputElement>(null); const loadSequence = useRef(0);
  const currentUser = preview ? previewUser : user;

  const logout = useCallback(() => { sessionStorage.removeItem('atlas-token'); const nextUrl = new URL(location.href); nextUrl.searchParams.delete('preview'); history.replaceState(null, '', nextUrl); setUser(null); setPreviewFile(null); setPreview(false); setData(emptyExplorer); setDialog(null); setSearch(''); setQuery(''); setView('files'); setFolderId(null); setAccount(false); setError(''); setToast('');  }, []);
  useEffect(() => { if (getToken()) request<User>('/auth/me').then(setUser).catch(() => sessionStorage.removeItem('atlas-token')).finally(() => setBooting(false)); }, []);
  useEffect(() => { window.addEventListener('atlas-session-expired', logout); return () => window.removeEventListener('atlas-session-expired', logout); }, [logout]);
  useEffect(() => { const timer = setTimeout(() => { setQuery(search.trim()); setPage(1); }, 260); return () => clearTimeout(timer); }, [search]);
  useEffect(() => { if (!toast) return; const timer = setTimeout(() => setToast(''), 4500); return () => clearTimeout(timer); }, [toast]);
  useEffect(() => { function key(e: KeyboardEvent) { if ((e.metaKey || e.ctrlKey) && e.key === 'k') { e.preventDefault(); searchInput.current?.focus(); } if (e.key === 'Escape') { setMenu(null); setAccount(false); setSidebar(false); } } document.addEventListener('keydown', key); return () => document.removeEventListener('keydown', key); }, []);
  useEffect(() => { if (!menu && !account) return; const close = () => { setMenu(null); setAccount(false); }; document.addEventListener('click', close); return () => document.removeEventListener('click', close); }, [menu, account]);
  const load = useCallback(async () => {
    if (!currentUser) return;
    const sequence = ++loadSequence.current; setLoading(true); setError('');
    try {
      if (preview) { setData(previewExplorer(view, folderId, query)); setUsers(previewUsers); setAudits(previewAudit.slice((page - 1) * 20, page * 20)); setAuditTotal(previewAudit.length); setTrash([]); setStats({ usedBytes: 31457280, fileCount: 9, folderCount: 5 }); }
      else {
        const [explorer, nextUsers, nextStats, logPage, trashItems] = await Promise.all([
          (view === 'audit' || view === 'trash') ? Promise.resolve(emptyExplorer) : request<Explorer>(`/explorer?${new URLSearchParams({ view, ...(view === 'recent' ? { page: String(page), pageSize: '20' } : {}), ...(folderId ? { folderId } : {}), ...(query ? { search: query } : {}) })}`),
          request<User[]>('/auth/users'), request<Stats>('/stats'), view === 'audit' ? request<{ items: Audit[]; total: number }>('/audit/page?page=' + page + '&pageSize=20') : Promise.resolve({ items: [], total: 0 }),
          view === 'trash' ? request<TrashItem[]>('/trash') : Promise.resolve([]),
        ]);
        if (sequence !== loadSequence.current) return;
        setData(explorer); setUsers(nextUsers); setStats(nextStats); setAudits(logPage.items); setAuditTotal(logPage.total); setTrash(trashItems);
      }
    } catch (e) { if (sequence === loadSequence.current) { setError((e as Error).message); setData(emptyExplorer); } }
    finally { if (sequence === loadSequence.current) setLoading(false); }
  }, [currentUser, folderId, preview, query, view, page]);
  useEffect(() => { void load(); return () => { loadSequence.current++; }; }, [load]);
  const navigate = (nextView: View, folder: string | null = null) => { setView(nextView); setPage(1); setFolderId(folder); setSearch(''); setQuery(''); setCategory('All files'); setSort(nextView === 'recent' ? 'modified' : 'name');  setSidebar(false); setMenu(null); };
  const openFolder = (resource: Resource) => navigate(view === 'favorites' || view === 'shared-by-me' ? view : resource.ownerId === currentUser?.id ? view === 'shared' ? 'files' : view : 'shared', resource.id);
  const openDialog = (next: Dialog) => { setDialog(next); setDialogError(''); setMenu(null); setInput(next.kind === 'rename' ? next.resource.name : ''); };
  const closeDialog = () => { if (!busy) { setDialog(null); setDialogError(''); } };
  const canWrite = has(data.permissions, WRITE) && !!data.folderId && !query && !preview && !busy;
  const items = data.items.filter(r => category === 'All files' || fileCategory(r) === category).sort((a, b) => sort === 'modified' ? b.modified.localeCompare(a.modified) : sort === 'size' ? b.size - a.size : (a.type === b.type ? a.name.localeCompare(b.name) : a.type === 'folder' ? -1 : 1));
  const activeView = views.find(v => v.id === view)!;
  const title = query ? `Search results for “${query}”` : folderId ? data.breadcrumbs.at(-1)?.name || activeView.name : activeView.name;

  const uploads = useUploads(user?.id, () => { void load(); });
  function upload(files: FileList | File[]) {
    if (!canWrite || !data.folderId) return;
    uploads.enqueue(Array.from(files), data.folderId, data.breadcrumbs.at(-1)?.name || 'this folder');
    if (fileInput.current) fileInput.current.value = '';
  }
  async function getDownload(r: Resource) { setMenu(null); if (preview) { setToast('Sign in to download files from your workspace.'); return; } setBusy(true); try { await download(r.id, r.name); setToast(`Downloaded ${r.name}`); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  function openResource(r: Resource) {
    setMenu(null);
    if (r.type === 'folder') { openFolder(r); return; }
    if (preview) setToast('Sign in to preview files from your workspace.'); else setPreviewFile(r);
  }
  async function submitDialog(e: FormEvent) {
    e.preventDefault(); if (!dialog || preview) return; setBusy(true); setDialogError('');
    try {
      if (dialog.kind === 'create') await request('/folders', { method: 'POST', body: JSON.stringify({ name: input.trim(), parentFolderId: data.folderId }) });
      if (dialog.kind === 'rename') await request(`${resourcePath(dialog.resource)}/name`, { method: 'PATCH', body: JSON.stringify({ name: input.trim() }) });
      if (dialog.kind === 'delete') await request(resourcePath(dialog.resource), { method: 'DELETE' });
      setToast(dialog.kind === 'create' ? 'Folder created' : dialog.kind === 'rename' ? 'Name updated' : 'Item moved to Trash'); setDialog(null); await load();
    } catch (e) { setDialogError((e as Error).message); } finally { setBusy(false); }
  }
  async function toggleFavorite(r: Resource) {
    setMenu(null); if (preview || busy) return; setBusy(true);
    try { await request(`${resourcePath(r)}/favorite`, { method: 'PUT', body: JSON.stringify({ isFavorite: !r.isFavorite }) }); setToast(r.isFavorite ? 'Removed from favorites' : 'Added to favorites'); await load(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  function favoriteButton(r: Resource) {
    return <button className={'favorite-toggle ' + (r.isFavorite ? 'is-favorite' : '')} disabled={preview || busy} aria-label={(r.isFavorite ? 'Remove ' : 'Add ') + r.name + (r.isFavorite ? ' from' : ' to') + ' favorites'} aria-pressed={!!r.isFavorite} onClick={e => { e.stopPropagation(); void toggleFavorite(r); }}><Star size={21} fill={r.isFavorite ? 'currentColor' : 'none'} /></button>;
  }
  async function restoreTrash(item: TrashItem) {
    if (busy) return; setBusy(true); setError('');
    try { await request('/trash/' + item.type + '/' + item.id + '/restore', { method: 'POST' }); setToast('Item restored'); await load(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  function actionMenu(r: Resource) {
    return <div className="action-menu" role="menu" onClick={e => e.stopPropagation()}>
      <button role="menuitem" disabled={preview || busy} onClick={() => void toggleFavorite(r)}><Star size={16} fill={r.isFavorite ? 'currentColor' : 'none'} />{r.isFavorite ? 'Remove from favorites' : 'Add to favorites'}</button>
      {r.type === 'file' && <button role="menuitem" onClick={() => openResource(r)} disabled={busy || preview}><ArrowRight size={16} />Open / Preview</button>}
      {r.type === 'file' && <button role="menuitem" disabled={preview || busy} onClick={() => openDialog({ kind: 'versions', resource: r })}><Clock3 size={16} />Version history</button>}
      {r.type === 'folder' ? <button role="menuitem" onClick={() => openFolder(r)}><Folder size={16} />Open folder</button> : <button role="menuitem" onClick={() => void getDownload(r)} disabled={busy}><Download size={16} />Download</button>}
      <button role="menuitem" onClick={() => openDialog({ kind: 'share', resource: r })}><UsersRound size={16} />{has(r.permissions, SHARE) ? 'Share & manage access' : 'View access'}</button>
      <div className="menu-divider" /><button role="menuitem" disabled={!has(r.permissions, WRITE) || preview || busy} onClick={() => openDialog({ kind: 'rename', resource: r })}><Pencil size={16} />Rename</button>
      <button role="menuitem" disabled={r.ownerId !== currentUser?.id || preview || busy} onClick={() => openDialog({ kind: 'move', resource: r })}><FolderInput size={16} />Move to…</button>
      <button role="menuitem" className="danger-text" disabled={!has(r.permissions, DELETE) || preview || busy} onClick={() => openDialog({ kind: 'delete', resource: r })}><Trash2 size={16} />Delete</button>
    </div>;
  }
  if (booting) return <div className="boot-screen"><Logo /><LoaderCircle className="spin" /><p>Opening your workspace…</p></div>;
  if (!currentUser) return <Login onLogin={u => { setUser(u); navigate('files'); }} onPreview={() => { const nextUrl = new URL(location.href); nextUrl.searchParams.set('preview', '1'); history.replaceState(null, '', nextUrl); setPreview(true); navigate('files'); }} />;

  return <div className="app-shell">
    {sidebar && <button className="sidebar-scrim" aria-label="Close navigation" onClick={() => setSidebar(false)} />}
    <aside className={`sidebar ${sidebar ? 'sidebar-open' : ''}`}><Logo /><div className="workspace-switch"><span className="workspace-icon">A</span><div><strong>Acme Workspace</strong><span>Team document hub</span></div><ShieldCheck size={16} /></div><span className="nav-label">WORKSPACE</span><nav>{views.map(v => <button key={v.id} onClick={() => navigate(v.id)} className={view === v.id ? 'active' : ''}><v.icon size={19} strokeWidth={1.8} /><span>{v.name}</span>{view === v.id && <span className="nav-active-dot" />}</button>)}</nav><div className="sidebar-bottom"><div className="storage-heading"><HardDrive size={16} /><strong>Workspace capacity</strong><span>{formatSize(stats.usedBytes)}</span></div><p>Used by your workspace</p><div className="local-status"><span />{preview ? 'Design preview' : 'Local workspace'}<span className="version">v1.0</span></div></div></aside>
    <div className="workspace-main"><header className="topbar"><button className="icon-button mobile-menu" onClick={() => setSidebar(true)} aria-label="Open navigation"><Menu size={21} /></button><div className="global-search"><Search size={18} /><input ref={searchInput} value={search} onChange={e => { setSearch(e.target.value); if (view === 'audit') setView('files'); }} placeholder="Search your workspace…" aria-label="Search your workspace" />{search ? <button className="icon-button" onClick={() => setSearch('')} aria-label="Clear search"><X size={14} /></button> : <kbd>Ctrl K</kbd>}</div><div className="topbar-right"><span className="secure-label"><ShieldCheck size={15} />Private workspace</span><span className="topbar-divider" /><div className="account-wrap"><button className="account-button" onClick={e => { e.stopPropagation(); setAccount(!account); setMenu(null); }} aria-expanded={account} aria-label="Account menu"><Avatar name={currentUser.name} /><div><strong>{currentUser.name}</strong><span>{preview ? 'Preview account' : currentUser.role}</span></div><ChevronDown size={14} /></button>{account && <div className="account-menu" onClick={e => e.stopPropagation()}><strong>{currentUser.name}</strong><span>{currentUser.email}</span><div className="menu-divider" /><button onClick={logout}><LogOut size={16} />{preview ? 'Back to sign in' : 'Sign out'}</button></div>}</div></div></header>
    {preview && <div className="preview-banner"><span><span className="preview-dot" />You’re exploring a read-only design preview.</span><button onClick={logout}>Sign in to your workspace<ArrowRight size={14} /></button></div>}
    <main className={`content ${dragging ? 'is-dragging' : ''}`} onDragEnter={e => { e.preventDefault(); if (e.dataTransfer.types.includes('Files') && canWrite) { dragCount.current++; setDragging(true); } }} onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = canWrite ? 'copy' : 'none'; }} onDragLeave={e => { e.preventDefault(); dragCount.current = Math.max(0, dragCount.current - 1); if (!dragCount.current) setDragging(false); }} onDrop={e => { e.preventDefault(); dragCount.current = 0; setDragging(false); if (e.dataTransfer.files.length) void upload(e.dataTransfer.files); }}>
      {dragging && <div className="drag-overlay"><CloudUpload size={52} /><h2>Drop your files here</h2><p>Upload to {data.breadcrumbs.at(-1)?.name || 'this folder'}</p></div>}
      <div className="breadcrumbs"><button onClick={() => navigate('files')} aria-label="Workspace home"><HardDrive size={14} />Workspace</button><ChevronRight size={13} />{view !== 'files' && <><button onClick={() => navigate(view)}>{activeView.name}</button>{data.breadcrumbs.length > 0 && <ChevronRight size={13} />}</>}{data.breadcrumbs.map((c, i) => <span key={c.id}><button className={i === data.breadcrumbs.length - 1 ? 'crumb-current' : ''} onClick={() => navigate(view, c.id)}>{c.name}</button>{i < data.breadcrumbs.length - 1 && <ChevronRight size={13} />}</span>)}{view === 'files' && !data.breadcrumbs.length && <span className="crumb-current">{query ? 'Search' : 'My Files'}</span>}</div>
      <div className="page-heading"><div><div className="heading-line"><h1>{title}</h1>{folderId && <AccessBadge permissions={data.permissions} owner={data.permissions === 15} />}</div><p>{folderId ? 'Everything you need, right where it belongs.' : activeView.description}</p></div>{view !== 'audit' && view !== 'trash' && <div className="heading-actions"><button className="button secondary" disabled={!canWrite || loading} onClick={() => openDialog({ kind: 'create' })} title={preview ? 'Sign in to create folders' : !has(data.permissions, WRITE) ? 'WRITE access is required in an open folder' : undefined}><FolderPlus size={17} />New folder</button><button className="button primary" disabled={!canWrite || loading} onClick={() => fileInput.current?.click()} title={preview ? 'Sign in to upload files' : undefined}>{busy ? <LoaderCircle size={17} className="spin" /> : <CloudUpload size={17} />}Upload files</button><input ref={fileInput} type="file" multiple hidden onChange={e => { if (e.target.files) void upload(e.target.files); }} /></div>}</div>
      <ErrorMessage message={error} />{error && <button className="text-button retry" onClick={() => void load()}>Try again</button>}
      {view === 'trash' ? <TrashPanel items={trash} query={query} loading={loading} busy={busy} preview={preview} onRestore={item => void restoreTrash(item)} /> : view === 'audit' ? <div className="audit-panel"><div className="section-heading"><h2>Workspace activity</h2><span className="subtle-badge">{auditTotal || audits.length} events</span></div><p className="audit-description">{currentUser.role === 'Admin' ? 'All workspace events are visible to administrators.' : 'Your activity and events on documents you can access.'}</p>{loading ? <Loading /> : audits.length ? <div className="table-scroll"><table className="audit-table"><thead><tr><th>Action</th><th>User</th><th>Details</th><th>Timestamp</th></tr></thead><tbody>{audits.map(a => <tr key={a.id}><td><span className={`audit-action ${a.action.includes('DELETE') ? 'audit-danger' : ''}`}>{a.action.toLowerCase().replaceAll('_', ' ')}</span></td><td><div className="owner-cell"><Avatar name={a.user} small /><div><strong>{a.user}</strong><span>{a.email}</span></div></div></td><td>{a.details}</td><td className="nowrap">{new Date(a.timestamp).toLocaleString()}</td></tr>)}</tbody></table></div> : <Empty icon="audit" title="Your workspace story starts here" description="Sign-ins, uploads, and sharing changes will appear in this activity log." />}</div> : <>
      <section className="file-panel"><div className="file-panel-toolbar"><div className="section-heading"><h2>{query ? 'Matching items' : view === 'recent' && !folderId ? 'Recently modified' : 'All items'}<span>{items.length}</span></h2></div><div className="table-tools"><div className="sort-control"><ArrowDownUp size={14} /><select value={sort} onChange={e => setSort(e.target.value)} aria-label="Sort files"><option value="name">Name A–Z</option><option value="modified">Latest modified</option><option value="size">Largest first</option></select><ChevronDown size={12} /></div><div className="view-toggle"><button className={layout === 'list' ? 'active' : ''} onClick={() => setLayout('list')} aria-label="List view" aria-pressed={layout === 'list'}><List size={17} /></button><button className={layout === 'grid' ? 'active' : ''} onClick={() => setLayout('grid')} aria-label="Grid view" aria-pressed={layout === 'grid'}><LayoutGrid size={16} /></button></div></div></div>
      <div className="filter-row"><div className="filter-tabs">{['All files', 'Documents', 'Spreadsheets', 'Images', 'Folders'].map(c => <button key={c} className={category === c ? 'active' : ''} onClick={() => { setCategory(c);  }}>{c}</button>)}</div><span className="filter-hint"><SlidersHorizontal size={14} />Organized your way</span></div>
      {loading ? <Loading /> : !items.length ? <Empty title={query ? 'No matching documents' : category !== 'All files' ? `No ${category.toLowerCase()} here` : view === 'favorites' && !folderId ? 'Keep your favorites close' : view === 'shared-by-me' && !folderId ? 'Nothing shared yet' : view === 'shared' && !folderId ? 'Good work is better together' : 'Room for your next idea'} description={query ? 'Try a different name or clear your search.' : view === 'favorites' && !folderId ? 'Use an item’s three-dot menu to add it to favorites.' : view === 'shared-by-me' && !folderId ? 'Files and folders you own with active sharing will appear here.' : view === 'shared' && !folderId ? 'Files and folders shared with you will appear here.' : canWrite ? 'Create a folder or drop files here to get started.' : 'Files will appear here when they are added or shared with you.'} /> : layout === 'grid' ? <div className="resource-grid">{items.map(r => <article className="resource-card" key={r.id}><div className="resource-card-top"><FileIcon resource={r} large /><div className="row-action"><button className="icon-button" aria-label={`Actions for ${r.name}`} onClick={e => { e.stopPropagation(); setMenu(menu === r.id ? null : r.id); }}><MoreHorizontal size={19} /></button>{menu === r.id && actionMenu(r)}</div></div><button className="resource-name" onClick={() => openResource(r)}>{r.name}</button>{favoriteButton(r)}<p>{r.type === 'folder' ? 'Folder' : formatSize(r.size)} · {date(r.modified)}</p><div className="resource-card-bottom"><SharedAvatars resource={r} onClick={() => openDialog({ kind: 'share', resource: r })} /><AccessBadge permissions={r.permissions} owner={r.ownerId === currentUser.id} /></div></article>)}</div> : <div className="table-scroll"><table className="file-table"><thead><tr><th><button className="column-sort" onClick={() => setSort('name')}>Name<ArrowDown size={12} /></button></th><th>Owner</th><th>Shared With</th><th><button className="column-sort" onClick={() => setSort('modified')}>Modified{sort === 'modified' && <ArrowDown size={12} />}</button></th><th>Size</th><th className="action-column">Actions</th></tr></thead><tbody>{items.map(r => <tr key={r.id}><td><div className="name-cell"><FileIcon resource={r} /><div><button className="resource-name" onClick={() => openResource(r)}>{r.name}</button>{favoriteButton(r)}<span className="file-meta">{r.type === 'folder' ? 'Folder' : r.name.split('.').at(-1)?.toUpperCase() + ' document'}<AccessBadge permissions={r.permissions} owner={r.ownerId === currentUser.id} /></span></div></div></td><td><div className="owner-cell"><Avatar name={r.owner} small index={r.ownerId === currentUser.id ? 0 : 1} /><span>{r.ownerId === currentUser.id ? 'You' : r.owner}</span></div></td><td><SharedAvatars resource={r} onClick={() => openDialog({ kind: 'share', resource: r })} /></td><td className="date-cell">{date(r.modified)}</td><td className="size-cell">{r.type === 'folder' ? '—' : formatSize(r.size)}</td><td><div className="row-action"><button className="icon-button" aria-label={`Actions for ${r.name}`} aria-expanded={menu === r.id} onClick={e => { e.stopPropagation(); setMenu(menu === r.id ? null : r.id); setAccount(false); }}><MoreHorizontal size={20} /></button>{menu === r.id && actionMenu(r)}</div></td></tr>)}</tbody></table></div>}
      <div className="table-footer"><span>{items.length} {items.length === 1 ? 'item' : 'items'}{query ? ' found' : ' in this view'}</span><span><ShieldCheck size={13} />Only people you choose have access</span></div></section>
      {!query && (has(data.permissions, WRITE) || preview) && <button className="upload-zone" disabled={!canWrite} onClick={() => fileInput.current?.click()}><span className="upload-zone-icon"><CloudUpload size={23} /></span><span><strong>Drop files here to upload</strong><span>or <em>browse files</em> from your computer</span></span><span className="upload-limit">Up to 100 MB per file</span></button>}
      {!query && !!data.folderId && !has(data.permissions, WRITE) && <div className="read-only-notice"><ShieldCheck size={16} />You have read-only access. You can view Office documents, open folders, and download files.</div>}
      </>}
      {(view === 'recent' || view === 'audit') && <Pagination page={page} total={view === 'audit' ? auditTotal : data.total ?? data.items.length} onChange={setPage} disabled={loading} />}
      <footer className="workspace-footer"><span>Thoughtfully organized. Securely shared.</span><span><span className="footer-dot" />Atlas Workspace</span></footer>
    </main></div>
    {toast && <div className="toast" role="status"><span><Check size={15} /></span>{toast}<button onClick={() => setToast('')} aria-label="Dismiss notification"><X size={15} /></button></div>}
    <UploadManager entries={uploads.entries} cancel={uploads.cancel} retry={uploads.retry} replace={uploads.replace} clearFinished={uploads.clearFinished} />
    {previewFile && <FilePreview key={previewFile.id} resource={previewFile} onClose={() => { setPreviewFile(null); void load(); }} />}
    {dialog?.kind === 'share' && <ShareDialog resource={dialog.resource} users={users} currentUser={currentUser} preview={preview} onClose={closeDialog} onChange={() => void load()} />}
    {dialog?.kind === 'versions' && <VersionHistory key={dialog.resource.id} resource={dialog.resource} onClose={closeDialog} onChange={() => void load()} />}
    {dialog?.kind === 'move' && <MoveDialog resource={dialog.resource} onClose={closeDialog} onChange={() => { setToast('Item moved'); void load(); }} />}
    {dialog && ['create', 'rename', 'delete'].includes(dialog.kind) && <Modal title={dialog.kind === 'create' ? 'Create a new folder' : dialog.kind === 'rename' ? 'Rename item' : 'Delete this item?'} subtitle={dialog.kind === 'create' ? `Add a little order to ${data.breadcrumbs.at(-1)?.name || 'your files'}.` : dialog.kind === 'delete' ? 'This item will move to Trash for its owner. Automatic deletion is disabled.' : 'Give it a name that’s easy to find.'} onClose={closeDialog}><form onSubmit={submitDialog}><div className="simple-dialog-content">{dialog.kind === 'delete' ? <><div className="delete-resource"><FileIcon resource={dialog.resource} /><strong>{dialog.resource.name}</strong></div>{dialog.resource.type === 'folder' && <div className="notice danger-notice">All files and subfolders inside will also move to Trash.</div>}</> : <label>Folder or file name<input autoFocus value={input} onChange={e => setInput(e.target.value)} maxLength={255} required placeholder="e.g. Annual reports" /></label>}<ErrorMessage message={dialogError} /></div><div className="modal-footer"><button className="button secondary" type="button" onClick={closeDialog} disabled={busy}>Cancel</button><button className={`button ${dialog.kind === 'delete' ? 'danger' : 'primary'}`} disabled={busy || (dialog.kind !== 'delete' && !input.trim())}>{busy && <LoaderCircle size={16} className="spin" />}{dialog.kind === 'create' ? 'Create folder' : dialog.kind === 'rename' ? 'Save changes' : 'Move to Trash'}</button></div></form></Modal>}
    {dialog?.kind === 'help' && <Modal title="Sharing, with clarity" subtitle="Every person gets exactly the access you choose." onClose={closeDialog}><div className="help-content"><div><ShieldCheck size={22} /><h3>Four simple permissions</h3><p><strong>Read</strong> lets teammates browse and download. <strong>Write</strong> adds upload and rename. <strong>Delete</strong> allows removal. <strong>Share</strong> lets them manage access.</p></div><div><Folder size={22} /><h3>Folders keep everyone in sync</h3><p>Sharing a folder also shares its contents. A specific permission on a child folder or file replaces inherited access.</p></div><div><UsersRound size={22} /><h3>Always know who has access</h3><p>Use the three-dot menu or teammate avatars to view access. Select a teammate to update their permissions. Remove a direct grant to restore inheritance, or block access with no permissions.</p></div></div><div className="modal-footer"><span>All changes are recorded in Audit Logs.</span><button className="button primary" onClick={closeDialog}>Got it</button></div></Modal>}
  </div>;
}
function Loading() { return <div className="loading-state"><LoaderCircle size={24} className="spin" /><span>Gathering your documents…</span></div>; }
function Empty({ title, description, icon = 'folder' }: { title: string; description: string; icon?: string }) { return <div className="empty-state"><span>{icon === 'audit' ? <Activity size={29} /> : <Folder size={29} />}</span><h3>{title}</h3><p>{description}</p></div>; }


function Pagination({ page, total, onChange, disabled }: { page: number; total: number; onChange: (page: number) => void; disabled: boolean }) {
  const pages = Math.max(1, Math.ceil(total / 20));
  return <nav className="pagination" aria-label="Pagination"><span>{total} items · Page {page} of {pages}</span><button className="button secondary" disabled={disabled || page <= 1} onClick={() => onChange(page - 1)}>Previous</button><button className="button secondary" disabled={disabled || page >= pages} onClick={() => onChange(page + 1)}>Next</button></nav>;
}