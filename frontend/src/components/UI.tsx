import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import { FileText, Folder, Sheet, Image, File, LockKeyhole, X } from 'lucide-react';
import type { Resource } from '../types';
import { fileCategory, initials, WRITE, has } from '../types';

export function Logo() { return <div className="brand"><img src="/atlas.svg" alt="" /><span>atlas<span className="brand-dot">.</span></span></div>; }
export function Avatar({ name, small = false, index = 0 }: { name: string; small?: boolean; index?: number }) {
  return <span className={`avatar avatar-${index % 4} ${small ? 'avatar-small' : ''}`} title={name}>{initials(name)}</span>;
}
export function FileIcon({ resource, large = false }: { resource: Pick<Resource, 'type' | 'name'>; large?: boolean }) {
  const category = fileCategory(resource);
  const Icon = resource.type === 'folder' ? Folder : category === 'Spreadsheets' ? Sheet : category === 'Images' ? Image : /\.(pdf|docx?|md|txt)$/i.test(resource.name) ? FileText : File;
  const color = resource.type === 'folder' ? 'folder' : /\.pdf$/i.test(resource.name) ? 'pdf' : category === 'Spreadsheets' ? 'sheet' : category === 'Images' ? 'image' : 'doc';
  return <span className={`file-icon file-icon-${color} ${large ? 'file-icon-large' : ''}`}><Icon size={large ? 26 : 21} strokeWidth={1.7} fill={resource.type === 'folder' ? 'currentColor' : 'none'} /></span>;
}
export function AccessBadge({ permissions, owner }: { permissions: number; owner?: boolean }) {
  if (owner) return null;
  return <span className={`permission-badge ${has(permissions, WRITE) ? 'editable' : ''}`}><LockKeyhole size={10} />{has(permissions, WRITE) ? 'Can edit' : 'Read only'}</span>;
}
export function SharedAvatars({ resource, onClick }: { resource: Resource; onClick?: () => void }) {
  const shares = resource.sharedWith.filter(x => x.permissions > 0);
  if (!shares.length) return <span className="muted private-label">Only you</span>;
  return <button className="shared-avatars" onClick={onClick} aria-label={`View access for ${resource.name}: ${shares.map(s => s.name).join(', ')}`}>
    {shares.slice(0, 3).map((s, i) => <Avatar key={s.userId} name={s.name} small index={i + 1} />)}
    {shares.length > 3 && <span className="avatar avatar-small avatar-more">+{shares.length - 3}</span>}
  </button>;
}
export function Modal({ title, subtitle, children, onClose, wide = false }: { title: string; subtitle?: string; children: ReactNode; onClose: () => void; wide?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { const dialog = ref.current!; dialog.showModal(); return () => dialog.close(); }, []);
  return <dialog ref={ref} className={`modal ${wide ? 'modal-wide' : ''}`} aria-label={title} onCancel={e => { e.preventDefault(); onClose(); }} onClick={e => { if (e.target === e.currentTarget) { const box = e.currentTarget.getBoundingClientRect(); if (e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom) onClose(); } }}>
    <div className="modal-header"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div><button className="icon-button" onClick={onClose} aria-label="Close dialog"><X size={19} /></button></div>{children}
  </dialog>;
}
export function ErrorMessage({ message }: { message: string }) { return message ? <div className="error-message" role="alert">{message}</div> : null; }
