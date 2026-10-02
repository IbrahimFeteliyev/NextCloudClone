import { useMemo, useState } from 'react';
import { flexRender, getCoreRowModel, getPaginationRowModel, useReactTable } from '@tanstack/react-table';
import { useTextContent } from './useTextContent';
import { parseCsv } from './csv';
import type { PreviewTicket } from './content';
import { PreviewError, PreviewLoading } from './PreviewState';
export default function CsvPreview({ ticket }: { ticket: PreviewTicket }) {
  const { text, error, truncated } = useTextContent(ticket, 1024 * 1024);
  if (error) return <PreviewError message={error} />; if (text === null) return <PreviewLoading />;
  return <CsvTable text={text} truncated={truncated} />;
}
function CsvTable({ text, truncated }: { text: string; truncated: boolean }) {
  const parsed = useMemo(() => parseCsv(text), [text]); const [header, setHeader] = useState(parsed.hasHeader);
  // A byte-limited last record may be incomplete; don't present it as a valid row.
  const rows = useMemo(() => { const result = parsed.rows.slice(header ? 1 : 0); return truncated ? result.slice(0, -1) : result; }, [parsed, header, truncated]);
  const count = Math.max(0, ...parsed.rows.map(row => row.length));
  const columns = useMemo(() => Array.from({ length: count }, (_, i) => ({ id: String(i), header: header ? parsed.rows[0]?.[i] || `Column ${i + 1}` : `Column ${i + 1}`, accessorFn: (row: string[]) => row[i] || '' })), [count, header, parsed]);
  const table = useReactTable({ data: rows, columns, getCoreRowModel: getCoreRowModel(), getPaginationRowModel: getPaginationRowModel(), initialState: { pagination: { pageSize: 100 } } });
  return <div className="csv-preview"><div className="preview-toolbar"><label><input type="checkbox" checked={header} onChange={e => { setHeader(e.target.checked); table.setPageIndex(0); }} />First row contains headers</label><span>{rows.length.toLocaleString()} rows · {count} columns</span></div>
    {(truncated || parsed.limited) && <div className="preview-warning">Preview limited to 1 MB, 5,000 rows and 100 columns. Download for the complete data.</div>}{parsed.errors.length > 0 && <div className="preview-warning">Some CSV records are malformed: {parsed.errors[0].message}</div>}
    <div className="csv-scroll"><table><thead>{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(cell => <th key={cell.id}>{flexRender(cell.column.columnDef.header, cell.getContext())}</th>)}</tr>)}</thead><tbody>{table.getRowModel().rows.map(row => <tr key={row.id}>{row.getVisibleCells().map(cell => <td key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody></table>{!rows.length && <p className="preview-empty">No data rows in this file.</p>}</div>
    <div className="preview-toolbar csv-pagination"><button className="button secondary" disabled={!table.getCanPreviousPage()} onClick={() => table.previousPage()}>Previous</button><span>Page {table.getState().pagination.pageIndex + 1} of {Math.max(1, table.getPageCount())}</span><button className="button secondary" disabled={!table.getCanNextPage()} onClick={() => table.nextPage()}>Next</button></div></div>;
}
