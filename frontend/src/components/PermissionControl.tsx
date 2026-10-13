import { useState } from 'react';
import { DELETE, READ, SHARE, WRITE, has } from '../types';
export function accessName(bits: number) { return bits === 0 ? 'No access' : bits === 1 ? 'Can view' : bits === 3 ? 'Can edit' : bits === 15 ? 'Full access' : 'Custom access'; }
export function accessDetail(bits: number) { return !bits ? 'Blocks access here, including inherited access.' : ['View & download', ...(has(bits, WRITE) ? ['Upload & edit'] : []), ...(has(bits, DELETE) ? ['Delete items'] : []), ...(has(bits, SHARE) ? ['Manage sharing'] : [])].join(' · '); }
export default function PermissionControl({ bits, available, disabled, onChange }: { bits: number; available: number; disabled: boolean; onChange: (bits: number) => void }) {
  const [advanced, setAdvanced] = useState(false);
  return <div className="access-control"><label>Access level<select value={[0,1,3,15].includes(bits) ? bits : 'custom'} disabled={disabled} onChange={e => { if (e.target.value === 'custom') setAdvanced(true); else onChange(Number(e.target.value)); }}>
    {[{ value: 1, label: 'Can view' }, { value: 3, label: 'Can edit' }, { value: 15, label: 'Full access' }, { value: 0, label: 'No access' }].map(x => <option key={x.value} value={x.value} disabled={(available & x.value) !== x.value}>{x.label}</option>)}<option value="custom">Custom access</option>
  </select></label><p>{accessDetail(bits)}</p><button type="button" className="text-button" aria-expanded={advanced} onClick={() => setAdvanced(!advanced)}>{advanced ? 'Hide individual permissions' : 'Customize permissions'}</button>
    {(advanced || ![0,1,3,15].includes(bits)) && <div className="access-checkboxes">{[{ bit: READ, name: 'View & download' }, { bit: WRITE, name: 'Upload & edit' }, { bit: DELETE, name: 'Delete items' }, { bit: SHARE, name: 'Manage sharing' }].map(x => <label key={x.bit}><input type="checkbox" checked={has(bits,x.bit)} disabled={disabled || !has(available,x.bit)} onChange={e => onChange(x.bit === READ && !e.target.checked ? 0 : e.target.checked ? bits | x.bit | READ : bits & ~x.bit)} />{x.name}</label>)}</div>}
  </div>;
}
