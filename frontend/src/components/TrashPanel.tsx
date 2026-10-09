import { useEffect, useState } from 'react';
import { ArrowDownUp, ChevronDown, LayoutGrid, List, LoaderCircle, MoreHorizontal, RotateCcw, ShieldCheck, Trash2 } from 'lucide-react';
import type { TrashItem } from '../types';
import { formatSize } from '../types';
import { FileIcon } from './UI';

export default function TrashPanel({ items, query, loading, busy, preview, onRestore }: { items: TrashItem[]; query: string; loading: boolean; busy: boolean; preview: boolean; onRestore: (item: TrashItem) => void }) {
  const [filter, setFilter] = useState('All items');
  const [sort, setSort] = useState('deleted');
  const [layout, setLayout] = useState<'list' | 'grid'>('list');
  const [menu, setMenu] = useState<string | null>(null);
  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    const key = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    document.addEventListener('click', close); document.addEventListener('keydown', key);
    return () => { document.removeEventListener('click', close); document.removeEventListener('keydown', key); };
  }, [menu]);
  const visible = items.filter(x => (!query || x.name.toLowerCase().includes(query.toLowerCase())) && (filter === 'All items' || (filter === 'Folders' ? x.type === 'folder' : x.type === 'file')))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name) : sort === 'size' ? b.size - a.size : b.deletedAt.localeCompare(a.deletedAt));
  const date = (value: string) => new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  const restore = (item: TrashItem) => { setMenu(null); onRestore(item); };
  function actions(item: TrashItem) {
    return <div className="row-action" onClick={e => e.stopPropagation()}><button className="icon-button" aria-label={`Actions for ${item.name}`} aria-expanded={menu === item.id} onClick={() => setMenu(menu === item.id ? null : item.id)}><MoreHorizontal size={20} /></button>
      {menu === item.id && <div className="action-menu" role="menu"><button role="menuitem" disabled={busy || preview} onClick={() => restore(item)}><RotateCcw size={16} />Restore</button></div>}</div>;
  }
  return <section className="file-panel trash-panel">
    <div className="file-panel-toolbar"><div className="section-heading"><h2>Deleted items <span>{visible.length}</span></h2></div><div className="table-tools"><div className="sort-control"><ArrowDownUp size={14} /><select aria-label="Sort deleted items" value={sort} onChange={e => setSort(e.target.value)}><option value="deleted">Recently deleted</option><option value="name">Name A–Z</option><option value="size">Largest first</option></select><ChevronDown size={12} /></div><div className="view-toggle"><button aria-label="List view" aria-pressed={layout === 'list'} className={layout === 'list' ? 'active' : ''} onClick={() => setLayout('list')}><List size={17} /></button><button aria-label="Grid view" aria-pressed={layout === 'grid'} className={layout === 'grid' ? 'active' : ''} onClick={() => setLayout('grid')}><LayoutGrid size={16} /></button></div></div></div>
    <div className="filter-row"><div className="filter-tabs">{['All items', 'Files', 'Folders'].map(value => <button key={value} className={filter === value ? 'active' : ''} onClick={() => { setFilter(value); setMenu(null); }}>{value}</button>)}</div><span className="filter-hint"><ShieldCheck size={14} />Recover deleted items</span></div>
    <div className="trash-retention"><Trash2 size={17} /><p><strong>30-day retention</strong><span>Automatic deletion is disabled. Items remain recoverable after the retention date.</span></p></div>
    {loading ? <div className="loading-state"><LoaderCircle className="spin" size={24} /><span>Loading deleted items…</span></div> : !visible.length ? <div className="empty-state"><span><Trash2 size={29} /></span><h3>{items.length ? 'No matching deleted items' : 'Trash is empty'}</h3><p>{items.length ? 'Try another filter or search.' : 'Deleted files and folders you own will appear here.'}</p></div> : layout === 'grid' ? <div className="resource-grid">{visible.map(item => <article className="resource-card" key={item.id}><div className="resource-card-top"><FileIcon resource={item} large />{actions(item)}</div><strong className="resource-name">{item.name}</strong><p>{item.type === 'folder' ? 'Folder' : formatSize(item.size)} · Deleted {date(item.deletedAt)}</p><div className="resource-card-bottom"><span className="muted">Until {date(item.retainUntil)}</span><button className="text-button" disabled={busy || preview} onClick={() => restore(item)}>Restore</button></div></article>)}</div> : <div className="table-scroll"><table className="file-table"><thead><tr><th>Name</th><th>Deleted</th><th>Retention date</th><th>Size</th><th className="action-column">Actions</th></tr></thead><tbody>{visible.map(item => <tr key={item.id}><td><div className="name-cell"><FileIcon resource={item} /><div><strong className="resource-name">{item.name}</strong><span className="file-meta">{item.type === 'folder' ? 'Folder' : item.name.split('.').at(-1)?.toUpperCase() + ' document'}</span></div></div></td><td className="date-cell">{date(item.deletedAt)}</td><td className="date-cell">{date(item.retainUntil)}</td><td className="size-cell">{item.type === 'folder' ? '—' : formatSize(item.size)}</td><td>{actions(item)}</td></tr>)}</tbody></table></div>}
    <div className="table-footer"><span>{visible.length} {visible.length === 1 ? 'item' : 'items'} in this view</span><span><ShieldCheck size={13} />Restore to the original folder</span></div>
  </section>;
}
