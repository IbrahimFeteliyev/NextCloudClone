import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { basicSetup } from 'codemirror';
import { Compartment, EditorState } from '@codemirror/state';
import type { Extension } from '@codemirror/state';
import { EditorView, keymap } from '@codemirror/view';
import { indentWithTab, redo, undo } from '@codemirror/commands';
import { gotoLine } from '@codemirror/search';
import { Pencil, Save, Undo2, Redo2, Search, WrapText, Braces } from 'lucide-react';
import { request } from '../../api';
import { has, WRITE } from '../../types';
import { readContent } from './content';
import type { PreviewTicket } from './content';
import { decodeText, textBytes } from './textEncoding';
import { PreviewError, PreviewLoading } from './PreviewState';
import { previewTextFileType, resolveTextFileType } from './textFileTypes';
import { loadEditorLanguage } from './editorLanguages';
import { documentSearch, openDocumentSearch } from './editorSearch';
import { formatJson, jsonValidation } from './jsonTools';

const RenderedMarkdown = lazy(() => import('./MarkdownPreview').then(module => ({ default: module.MarkdownContent })));
export type EditorSearchCommand = ((replace: boolean) => void) | null;

const theme = EditorView.theme({
  '&': { height: '100%', fontSize: '13px', backgroundColor: '#fff', color: '#24382e' },
  '.cm-scroller': { overflow: 'auto', fontFamily: 'Consolas, "SFMono-Regular", monospace', lineHeight: '1.8' },
  '.cm-content': { padding: '16px 0', minHeight: '100%' },
  '.cm-line': { padding: '0 20px' },
  '.cm-gutters': { backgroundColor: '#f5f8f6', color: '#819087', borderRight: '1px solid #e3eae5' },
  '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: '#f0f7f2' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': { backgroundColor: '#d8e9dc' },
  '.cm-panels': { backgroundColor: '#f5f8f6', color: '#24382e' },
  '.cm-textfield': { backgroundColor: '#fff', border: '1px solid #cbd8ce', borderRadius: '4px' },
  '.cm-button': { backgroundImage: 'none', backgroundColor: '#fff', border: '1px solid #cbd8ce', borderRadius: '4px' },
});
type Props = { ticket: PreviewTicket; onDirtyChange: (dirty: boolean) => void; onBusyChange: (busy: boolean) => void; onSaved: (ticket: PreviewTicket) => void; onSearchReady?: (command: EditorSearchCommand) => void };
export default function TextPreview({ ticket, onDirtyChange, onBusyChange, onSaved, onSearchReady }: Props) {
  const supportedType = resolveTextFileType(ticket); const type = previewTextFileType(ticket);
  const markdown = !!type.renderedPreview; const json = type.language === 'json';
  const host = useRef<HTMLDivElement>(null); const editor = useRef<EditorView | null>(null);
  const editMode = useRef(new Compartment()); const wrapping = useRef(new Compartment());
  const original = useRef(''); const bom = useRef(false); const dirtyRef = useRef(false);
  const dirtyCallback = useRef(onDirtyChange); dirtyCallback.current = onDirtyChange;
  const searchCallback = useRef(onSearchReady); searchCallback.current = onSearchReady;
  const [loaded, setLoaded] = useState<{ text: string; encoding: string; language: Extension } | null>(null);
  const [rendered, setRendered] = useState(markdown); const renderedRef = useRef(rendered); renderedRef.current = rendered;
  const pendingSearch = useRef<boolean | null>(null); const [draft, setDraft] = useState(''); const [jsonError, setJsonError] = useState('');
  const [error, setError] = useState(''); const [saveError, setSaveError] = useState('');
  const [editing, setEditing] = useState(false); const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false); const [wrap, setWrap] = useState(false); const [position, setPosition] = useState({ line: 1, column: 1 });
  const limit = ticket.textEditLimit || 512 * 1024; const large = ticket.size > limit;
  const allowed = has(ticket.permissions || 0, WRITE) && !!supportedType && !large && loaded?.encoding === 'utf-8';
  useEffect(() => {
    const controller = new AbortController(); setLoaded(null); setError(''); setEditing(false); setSaveError(''); setRendered(markdown);
    Promise.all([readContent(ticket, controller.signal, limit).then(blob => blob.arrayBuffer()), loadEditorLanguage(type.language)]).then(([buffer, language]) => {
      if (controller.signal.aborted) return;
      const decoded = decodeText(new Uint8Array(buffer), large); bom.current = decoded.bom; original.current = decoded.text;
      setDraft(decoded.text); setJsonError(json && !large ? jsonValidation(decoded.text) : ''); setLoaded({ ...decoded, language });
    }).catch(e => { if (!controller.signal.aborted) setError(e instanceof TypeError ? 'This text could not be decoded. Download it to inspect it locally.' : e.message); });
    return () => controller.abort();
  }, [ticket, limit, large, type.language, markdown, json]);
  useEffect(() => {
    if (!loaded || !host.current) return;
    dirtyRef.current = false; setDirty(false); dirtyCallback.current(false);
    const view = new EditorView({ parent: host.current, state: EditorState.create({ doc: loaded.text, extensions: [
      basicSetup, theme, loaded.language, documentSearch,
      ...(type.language === 'plain' ? [EditorState.languageData.of(() => [{ closeBrackets: { brackets: [] } }])] : []),
      EditorState.lineSeparator.of(loaded.text.includes('\r\n') ? '\r\n' : '\n'),
      keymap.of([indentWithTab]),
      editMode.current.of([EditorState.readOnly.of(true), EditorView.editable.of(false)]), wrapping.current.of([]),
      EditorView.contentAttributes.of({ 'aria-label': 'Text content', tabindex: '0' }),
      EditorView.updateListener.of(update => {
        if (update.docChanged) {
          const text = update.state.sliceDoc(); const changed = text !== original.current; dirtyRef.current = changed; setDirty(changed); dirtyCallback.current(changed);
          if (markdown) setDraft(text); if (json && !large) setJsonError(jsonValidation(text));
        }
        if (update.selectionSet || update.docChanged) { const cursor = update.state.selection.main.head; const line = update.state.doc.lineAt(cursor); setPosition({ line: line.number, column: cursor - line.from + 1 }); }
      }),
    ] }) });
    editor.current = view; setWrap(false); setPosition({ line: 1, column: 1 });
    searchCallback.current?.(replace => { if (renderedRef.current) { pendingSearch.current = replace; setRendered(false); } else openDocumentSearch(view, replace); });
    return () => { view.destroy(); editor.current = null; dirtyCallback.current(false); searchCallback.current?.(null); };
  }, [loaded, type.language, markdown, json, large]);
  useEffect(() => { editor.current?.dispatch({ effects: editMode.current.reconfigure([EditorState.readOnly.of(!editing), EditorView.editable.of(editing)]) }); if (editing) editor.current?.focus(); }, [editing, loaded]);
  useEffect(() => { editor.current?.dispatch({ effects: wrapping.current.reconfigure(wrap ? EditorView.lineWrapping : []) }); }, [wrap, loaded]);
  useEffect(() => {
    if (rendered || !editor.current) return;
    editor.current.requestMeasure();
    if (pendingSearch.current !== null) { openDocumentSearch(editor.current, pendingSearch.current); pendingSearch.current = null; }
  }, [rendered, loaded]);
  function startEdit() { setRendered(false); setEditing(true); }
  function cancel() {
    if (dirtyRef.current && !window.confirm('Discard your unsaved changes?')) return;
    setDraft(original.current); setLoaded({ ...loaded!, text: original.current }); setEditing(false); setRendered(markdown); setSaveError('');
  }
  function beautify() {
    const view = editor.current!; const result = formatJson(view.state.sliceDoc(), view.state.lineBreak);
    if (result.error) { setJsonError(result.error); return; }
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: result.formatted! }, selection: { anchor: 0 }, userEvent: 'input.format' }); view.focus();
  }
  async function save() {
    const text = editor.current!.state.sliceDoc();
    if (textBytes(text, bom.current) > limit) { setSaveError('The edited text exceeds 512 KB. Reduce its size before saving.'); return; }
    setSaving(true); onBusyChange(true); setSaveError('');
    try {
      const next = await request<PreviewTicket>(`/files/${ticket.id}/content`, { method: 'PUT', body: JSON.stringify({ content: (bom.current ? '\uFEFF' : '') + text, expectedVersion: ticket.version }) });
      original.current = text; dirtyRef.current = false; setDirty(false); dirtyCallback.current(false); setEditing(false); onSaved(next);
    } catch (e) { setSaveError((e as Error).message); }
    finally { setSaving(false); onBusyChange(false); }
  }
  if (error) return <PreviewError message={error} />; if (!loaded) return <PreviewLoading />;
  return <div className="text-editor-preview">
    <div className="text-editor-toolbar">
      {editing ? <><button className="button primary" disabled={saving || !dirty} onClick={() => void save()}><Save size={15} />{saving ? 'Saving…' : 'Save'}</button><button className="button secondary" disabled={saving} onClick={cancel}>Cancel</button>{!rendered && <><button className="icon-button" aria-label="Undo (Ctrl+Z)" disabled={saving} onClick={() => { undo(editor.current!); editor.current!.focus(); }}><Undo2 size={16} /></button><button className="icon-button" aria-label="Redo (Ctrl+Y)" disabled={saving} onClick={() => { redo(editor.current!); editor.current!.focus(); }}><Redo2 size={16} /></button></>}</> : allowed && <button className="button secondary" onClick={startEdit}><Pencil size={15} />Edit</button>}
      {markdown && <div className="markdown-view-toggle" role="group" aria-label="Markdown view"><button className="button secondary" aria-pressed={rendered} disabled={saving} onClick={() => setRendered(true)}>Preview</button><button className="button secondary" aria-pressed={!rendered} disabled={saving} onClick={() => setRendered(false)}>Source</button></div>}
      {!rendered && <><button className="button secondary" onClick={() => openDocumentSearch(editor.current!)} title="Find (Ctrl+F)"><Search size={15} />Find</button>
        {editing && <button className="button secondary" disabled={saving} onClick={() => openDocumentSearch(editor.current!, true)} title="Replace (Ctrl+H)">Replace</button>}
        <button className="button secondary" onClick={() => gotoLine(editor.current!)} title="Go to line (Ctrl+G)">Go to line</button>
        <button className="button secondary" aria-pressed={wrap} onClick={() => setWrap(value => !value)}><WrapText size={15} />Word wrap</button>
        {json && editing && <button className="button secondary" disabled={saving} onClick={beautify}><Braces size={15} />Format JSON</button>}
      </>}
    </div>
    {large && <div className="preview-warning">Showing the first 512 KB in read-only mode. This file exceeds the editing limit; use Download for the complete file.</div>}
    {!large && loaded.encoding !== 'utf-8' && <div className="preview-warning">This {loaded.encoding.toUpperCase()} file is read-only. Download it to edit locally.</div>}
    {saveError && <div className="preview-warning" role="alert">{saveError}</div>}
    {jsonError && <div className="preview-warning json-validation" role="alert"><strong>Invalid JSON:</strong> {jsonError} Content is preserved.</div>}
    <div className="text-editor-host" ref={host} hidden={rendered} />
    {rendered && <div className="markdown-editor-preview"><Suspense fallback={<PreviewLoading />}><RenderedMarkdown text={editing ? draft : loaded.text} /></Suspense></div>}
    <footer className="text-editor-status"><span>{rendered ? 'Rendered preview' : `Ln ${position.line}, Col ${position.column}`}</span><span>{loaded.encoding.toUpperCase()} · {type.label}{json && !large ? jsonError ? ' (invalid)' : ' (valid)' : ''} · {editing ? dirty ? 'Unsaved changes' : 'Editing' : 'Read-only'}</span></footer>
  </div>;
}
