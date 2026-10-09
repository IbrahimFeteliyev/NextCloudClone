// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { SearchQuery, setSearchQuery } from '@codemirror/search';
import { documentSearch, openDocumentSearch, searchMatches } from './editorSearch';

let view: EditorView | undefined;
afterEach(() => { view?.destroy(); view?.dom.remove(); view = undefined; });
function editor(readOnly = false) {
  view = new EditorView({ parent: document.body, state: EditorState.create({ doc: 'Atlas atlas Atlas\nAtlasDrive atlas', extensions: [documentSearch, EditorState.readOnly.of(readOnly)] }) }); return view;
}
function input(name: string, value: string) {
  const field = view!.dom.querySelector<HTMLInputElement>(`[aria-label="${name}"]`)!; field.value = value; field.dispatchEvent(new Event('input', { bubbles: true })); return field;
}
function click(name: string) { view!.dom.querySelector<HTMLButtonElement>(`[aria-label="${name}"]`)!.click(); }
describe('Document search panel', () => {
  it('uses a top panel, counts matches and navigates with Enter/Shift+Enter', () => {
    const editorView = editor(); openDocumentSearch(editorView); const field = input('Find', 'atlas');
    expect(editorView.dom.querySelector('.cm-panels-top .atlas-search')).not.toBeNull();
    expect(editorView.dom.querySelector('[role="status"]')!.textContent).toBe('5 matches');
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); expect(editorView.dom.querySelector('[role="status"]')!.textContent).toBe('1 of 5');
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); expect(editorView.dom.querySelector('[role="status"]')!.textContent).toBe('2 of 5');
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true })); expect(editorView.dom.querySelector('[role="status"]')!.textContent).toBe('1 of 5');
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); expect(editorView.dom.querySelector('.atlas-search')).toBeNull();
  });
  it('supports compact case/word/regex toggles and reports invalid regex without changing content', () => {
    editor(); openDocumentSearch(view!); input('Find', 'atlas'); click('Match case'); click('Whole word');
    expect(view!.dom.querySelector('[role="status"]')!.textContent).toBe('2 matches');
    expect(view!.dom.querySelector('[aria-label="Match case"]')!.getAttribute('aria-pressed')).toBe('true');
    click('Regex'); input('Find', '['); expect(view!.dom.querySelector('[role="status"]')!.textContent).toBe('Invalid regex'); expect(view!.state.doc.toString()).toContain('Atlas atlas Atlas');
    expect(view!.dom.querySelectorAll('input[type="checkbox"]')).toHaveLength(0);
  });
  it('uses CodeMirror replace commands and never offers replacement in a read-only document', () => {
    editor(); openDocumentSearch(view!, true); input('Find', 'atlas'); input('Replace with', 'Team'); click('Replace all matches');
    expect(view!.state.doc.toString()).toBe('Team Team Team\nTeamDrive Team');
    view!.destroy(); view!.dom.remove(); editor(true); openDocumentSearch(view!, true); input('Find', 'atlas');
    const row = view!.dom.querySelector<HTMLElement>('.atlas-search-replace')!; expect(row.hidden).toBe(true); click('Replace all matches'); expect(view!.state.doc.toString()).toContain('Atlas atlas Atlas');
  });
  it('hides replacement immediately when editor mode returns to read-only', () => {
    const mode = new Compartment(); view = new EditorView({ parent: document.body, state: EditorState.create({ doc: 'one', extensions: [documentSearch, mode.of(EditorState.readOnly.of(false))] }) });
    openDocumentSearch(view, true); expect(view.dom.querySelector<HTMLElement>('.atlas-search-replace')!.hidden).toBe(false);
    view.dispatch({ effects: mode.reconfigure(EditorState.readOnly.of(true)) }); expect(view.dom.querySelector<HTMLElement>('.atlas-search-replace')!.hidden).toBe(true);
  });
  it('reflects external query changes and safely caps result counts', () => {
    editor(); openDocumentSearch(view!); view!.dispatch({ effects: setSearchQuery.of(new SearchQuery({ search: 'Atlas', caseSensitive: true })) }); expect(view!.dom.querySelector<HTMLInputElement>('[aria-label="Find"]')!.value).toBe('Atlas');
    const result = searchMatches(EditorState.create({ doc: 'a '.repeat(12000) }), new SearchQuery({ search: 'a' })); expect(result.matches).toHaveLength(10000); expect(result.capped).toBe(true);
  });
});
