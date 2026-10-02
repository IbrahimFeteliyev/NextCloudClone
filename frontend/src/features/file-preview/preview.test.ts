import { describe, expect, it } from 'vitest';
import { resolvePreview } from './previewResolver';
import { parseCsv } from './csv';
describe('Preview selection', () => {
  it.each([['photo.jpg', 'image'], ['demo.webm', 'video'], ['demo.ogg', 'video'], ['report.PDF', 'pdf'], ['notes.markdown', 'markdown'], ['app.log', 'text'], ['data.csv', 'csv'], ['Report.docx', 'office'], ['Budget.xlsx', 'office'], ['Slides.pptx', 'office'], ['archive.zip', 'unsupported']])('resolves %s with generic MIME', (name, kind) => expect(resolvePreview({ name, contentType: 'application/octet-stream' })).toBe(kind));
  it('uses specific MIME before a conflicting extension', () => { expect(resolvePreview({ name: 'photo.pdf', contentType: 'image/png' })).toBe('image'); expect(resolvePreview({ name: 'fake.jpg', contentType: 'text/html' })).toBe('unsupported'); });
  it('recognizes structured plain-text uploads', () => { expect(resolvePreview({ name: 'notes.md', contentType: 'text/plain; charset=utf-8' })).toBe('markdown'); expect(resolvePreview({ name: 'expenses.csv', contentType: 'text/plain' })).toBe('csv'); });
  it('handles common desktop MIME aliases', () => { expect(resolvePreview({ name: 'data.csv', contentType: 'application/vnd.ms-excel' })).toBe('csv'); expect(resolvePreview({ name: 'movie.ogg', contentType: 'application/ogg' })).toBe('video'); expect(resolvePreview({ name: 'movie.ogg', contentType: 'audio/ogg' })).toBe('video'); });
});
describe('CSV data', () => {
  it('preserves quoted delimiters, newlines and empty cells', () => {
    const result = parseCsv('Department,Note,Amount\r\nFinance,"Travel, hotel",12400\r\nIT,"Line one\nLine two",\r\n');
    expect(result.hasHeader).toBe(true); expect(result.rows[1]).toEqual(['Finance', 'Travel, hotel', '12400']); expect(result.rows[2]).toEqual(['IT', 'Line one\nLine two', '']);
  });
  it('detects semicolons and retains numeric first records', () => { const result = parseCsv('1;2;3\n4;5;6'); expect(result.hasHeader).toBe(false); expect(result.rows[1]).toEqual(['4', '5', '6']); });
  it('caps large tables', () => { const result = parseCsv('ID,Name\n' + Array.from({ length: 6000 }, (_, i) => `${i},Item`).join('\n')); expect(result.rows).toHaveLength(5001); expect(result.limited).toBe(true); });
});
