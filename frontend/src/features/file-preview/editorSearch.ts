import { Prec } from '@codemirror/state';
import type { EditorState } from '@codemirror/state';
import { keymap, runScopeHandlers } from '@codemirror/view';
import type { EditorView, Panel, ViewUpdate } from '@codemirror/view';
import { closeSearchPanel, findNext, findPrevious, getSearchQuery, gotoLine, openSearchPanel, replaceAll, replaceNext, search, SearchQuery, setSearchQuery } from '@codemirror/search';
import './editorSearch.css';

const panels = new WeakMap<EditorView, DocumentSearchPanel>();
const MAX_COUNTED_MATCHES = 10000;
export function searchMatches(state: EditorState, query: SearchQuery) {
  if (!query.valid) return { matches: [] as { from: number; to: number }[], capped: false };
  const cursor = query.getCursor(state); const matches: { from: number; to: number }[] = [];
  for (let match = cursor.next(); !match.done; match = cursor.next()) {
    if (matches.length === MAX_COUNTED_MATCHES) return { matches, capped: true };
    matches.push(match.value);
  }
  return { matches, capped: false };
}
function element<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag); node.className = className; if (text) node.textContent = text; return node;
}
function button(text: string, label: string, action: () => void, className = 'button secondary') {
  const node = element('button', className, text); node.type = 'button'; node.setAttribute('aria-label', label); node.addEventListener('click', action); return node;
}

// This replaces only the panel UI. Query parsing, matching, highlights and replacement
// all remain CodeMirror commands/state, including safe read-only handling.
class DocumentSearchPanel implements Panel {
  readonly dom = element('div', 'atlas-search'); readonly top = true;
  private readonly find = element('input', 'cm-textfield atlas-search-input');
  private readonly replacement = element('input', 'cm-textfield atlas-search-input');
  private readonly count = element('span', 'atlas-search-count');
  private readonly replaceRow = element('div', 'atlas-search-replace');
  private readonly toggles = new Map<'caseSensitive' | 'wholeWord' | 'regexp', HTMLButtonElement>();
  private readonly replaceButton: HTMLButtonElement; private readonly replaceAllButton: HTMLButtonElement;
  private query: SearchQuery; private cacheDoc: EditorState['doc'] | null = null;
  private matches: { from: number; to: number }[] = []; private capped = false; private replaceVisible = false;
  constructor(private readonly view: EditorView) {
    this.query = getSearchQuery(view.state); panels.set(view, this);
    this.dom.setAttribute('role', 'search'); this.dom.setAttribute('aria-label', 'Find in document');
    this.find.type = this.replacement.type = 'text'; this.find.setAttribute('aria-label', 'Find'); this.find.setAttribute('main-field', 'true');
    this.find.placeholder = 'Find in document'; this.replacement.setAttribute('aria-label', 'Replace with'); this.replacement.placeholder = 'Replace with';
    this.find.autocomplete = this.replacement.autocomplete = 'off'; this.find.spellcheck = this.replacement.spellcheck = false;
    this.count.setAttribute('role', 'status'); this.count.setAttribute('aria-live', 'polite');
    const first = element('div', 'atlas-search-find');
    first.append(element('span', 'atlas-search-label', 'Find'), this.find,
      button('↑', 'Previous match (Shift+Enter)', () => findPrevious(view), 'button secondary atlas-search-nav'),
      button('↓', 'Next match (Enter)', () => findNext(view), 'button secondary atlas-search-nav'), this.count,
      button('×', 'Close search', () => closeSearchPanel(view), 'icon-button atlas-search-close'));
    this.replaceButton = button('Replace', 'Replace match', () => replaceNext(view));
    this.replaceAllButton = button('Replace all', 'Replace all matches', () => replaceAll(view));
    this.replaceRow.append(element('span', 'atlas-search-label', 'Replace'), this.replacement, this.replaceButton, this.replaceAllButton);
    const options = element('div', 'atlas-search-options');
    for (const [key, title, icon] of [['caseSensitive', 'Match case', 'Aa'], ['wholeWord', 'Whole word', 'W'], ['regexp', 'Regex', '.*']] as const) {
      const toggle = button(`${icon}  ${title}`, title, () => { this.commit({ [key]: !this.query[key] }); }, 'atlas-search-option');
      this.toggles.set(key, toggle); options.append(toggle);
    }
    this.dom.append(first, this.replaceRow, options);
    this.find.addEventListener('input', () => this.commit()); this.replacement.addEventListener('input', () => this.commit());
    this.dom.addEventListener('keydown', event => {
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeSearchPanel(view); }
      else if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); (event.shiftKey ? findPrevious : findNext)(view); }
      else if (runScopeHandlers(view, event, 'search-panel')) { event.preventDefault(); event.stopPropagation(); }
    });
    this.sync();
  }
  mount() { this.find.focus(); this.find.select(); }
  destroy() { panels.delete(this.view); }
  showReplace(visible: boolean) { this.replaceVisible = visible && !this.view.state.readOnly; this.sync(); }
  private commit(patch: Partial<{ caseSensitive: boolean; wholeWord: boolean; regexp: boolean }> = {}) {
    const query = new SearchQuery({ search: this.find.value, replace: this.replacement.value, literal: this.query.literal,
      caseSensitive: this.query.caseSensitive, wholeWord: this.query.wholeWord, regexp: this.query.regexp, ...patch });
    if (!query.eq(this.query)) this.view.dispatch({ effects: setSearchQuery.of(query) });
  }
  update(update: ViewUpdate) {
    const next = getSearchQuery(update.state);
    if (!next.eq(this.query)) { this.query = next; this.cacheDoc = null; }
    this.sync();
  }
  private sync() {
    const { state } = this.view;
    if (this.find.value !== this.query.search) this.find.value = this.query.search;
    if (this.replacement.value !== this.query.replace) this.replacement.value = this.query.replace;
    for (const [key, toggle] of this.toggles) toggle.setAttribute('aria-pressed', String(this.query[key]));
    this.replaceRow.hidden = !this.replaceVisible || state.readOnly;
    this.replaceButton.disabled = this.replaceAllButton.disabled = state.readOnly || !this.query.valid;
    if (this.cacheDoc !== state.doc) { const found = searchMatches(state, this.query); this.matches = found.matches; this.capped = found.capped; this.cacheDoc = state.doc; }
    const selection = state.selection.main;
    const current = this.matches.findIndex(match => match.from === selection.from && match.to === selection.to);
    this.count.textContent = !this.query.search ? 'Type to search' : !this.query.valid ? 'Invalid regex' : this.capped ? '10,000+ matches' : current >= 0 ? `${current + 1} of ${this.matches.length}` : `${this.matches.length} matches`;
    this.count.classList.toggle('invalid', !!this.query.search && !this.query.valid);
  }
}

export function openDocumentSearch(view: EditorView, replace = false) {
  openSearchPanel(view); panels.get(view)?.showReplace(replace); return true;
}
export const documentSearch = [search({ top: true, createPanel: view => new DocumentSearchPanel(view) }), Prec.highest(keymap.of([
  { key: 'Mod-f', run: view => openDocumentSearch(view), scope: 'editor search-panel' },
  { key: 'Mod-h', run: view => openDocumentSearch(view, true), scope: 'editor search-panel', preventDefault: true },
  { key: 'Mod-g', run: gotoLine },
]))];
