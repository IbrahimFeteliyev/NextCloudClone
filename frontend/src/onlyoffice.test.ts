import { describe, expect, it } from 'vitest';
import { isOfficeFile } from './onlyoffice';
describe('Office file routing', () => {
  it.each(['Plan.docx', 'Budget.XLSX', 'Review.pptx'])('opens %s in the editor', name => expect(isOfficeFile({ type: 'file', name })).toBe(true));
  it.each(['Legacy.doc', 'macro.xlsm', 'Notes.txt', 'Office.docx.exe'])('keeps %s on the download flow', name => expect(isOfficeFile({ type: 'file', name })).toBe(false));
  it('does not treat a folder named Office.docx as a document', () => expect(isOfficeFile({ type: 'folder', name: 'Office.docx' })).toBe(false));
});
