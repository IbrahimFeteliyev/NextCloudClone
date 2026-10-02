import Papa from 'papaparse';
export function parseCsv(text: string) {
  const parsed = Papa.parse<string[]>(text, { skipEmptyLines: 'greedy', preview: 5001 });
  const rows = parsed.data.map(row => row.slice(0, 100));
  const first = rows[0] || [];
  const hasHeader = first.length > 0 && first.every(cell => cell.trim() && !/^[-+]?\d+(\.\d+)?$/.test(cell.trim())) && new Set(first).size === first.length;
  return { rows, hasHeader, limited: parsed.meta.truncated || parsed.data.some(row => row.length > 100), errors: parsed.errors.filter(error => error.code !== 'UndetectableDelimiter') };
}
