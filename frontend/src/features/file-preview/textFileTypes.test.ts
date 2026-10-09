import { describe, expect, it } from 'vitest';
import { EditorState } from '@codemirror/state';
import { syntaxTree } from '@codemirror/language';
import { textFileTypes, resolveTextFileType, previewTextFileType } from './textFileTypes';
import { resolvePreview } from './previewResolver';
import { loadEditorLanguage } from './editorLanguages';
import { formatJson, jsonValidation } from './jsonTools';
describe('Shared text type registry', () => {
  it.each(textFileTypes.flatMap(type => type.extensions.map(extension => ({ extension, type }))))('routes .$extension to the correct language and preview', ({ extension, type }) => {
    for (const contentType of ['application/octet-stream', 'text/plain', type.mime, ...type.aliases]) {
      const file = { name: 'source.' + extension.toUpperCase(), contentType };
      expect(resolveTextFileType(file)?.language).toBe(type.language); expect(resolvePreview(file)).toBe(type.renderedPreview ? 'markdown' : 'text');
    }
  });
  it('preserves media routing for contradictory MIME and does not route arbitrary uploaded HTML into the DOM', () => {
    expect(resolvePreview({ name: 'source.json', contentType: 'image/png' })).toBe('image');
    expect(resolvePreview({ name: 'source.html', contentType: 'text/html' })).toBe('text');
    expect(resolveTextFileType({ name: 'source.html', contentType: 'application/pdf' })).toBeUndefined();
  });
  it('retains MIME-only Markdown preview without enabling edits to unregistered extensions', () => {
    const file = { name: 'README', contentType: 'text/x-markdown; charset=utf-8' };
    expect(resolvePreview(file)).toBe('markdown'); expect(previewTextFileType(file).renderedPreview).toBe(true); expect(resolveTextFileType(file)).toBeUndefined();
  });
  it('loads JSON/XML/Python grammars instead of applying JavaScript to every file', async () => {
    const data = [['json', '{"a":1}', 'JsonText'], ['xml', '<root/>', 'Document'], ['python', 'def hello():\n  return True', 'Script']] as const;
    for (const [language, doc, name] of data) expect(syntaxTree(EditorState.create({ doc, extensions: [await loadEditorLanguage(language)] })).topNode.name).toBe(name);
    for (const type of textFileTypes) await expect(loadEditorLanguage(type.language)).resolves.toBeDefined();
  });
});
describe('JSON formatting and validation', () => {
  it('formats valid JSON with two spaces while preserving Windows line endings', () => { expect(formatJson('{"a":1,"b":[true]}', '\r\n').formatted).toBe('{\r\n  "a": 1,\r\n  "b": [\r\n    true\r\n  ]\r\n}'); expect(jsonValidation('{"a":1}')).toBe(''); });
  it('returns an error without replacement content for invalid JSON', () => { const original = '{"a":'; expect(jsonValidation(original)).not.toBe(''); expect(formatJson(original).formatted).toBeUndefined(); expect(formatJson(original).error).toBeTruthy(); });
});
