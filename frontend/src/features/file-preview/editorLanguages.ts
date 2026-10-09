import type { Extension } from '@codemirror/state';
import { StreamLanguage } from '@codemirror/language';
import { linter } from '@codemirror/lint';
import type { TextLanguage } from './textFileTypes';

// Load only the language needed by this document. C# uses CodeMirror's supported stream parser.
const loaders: Record<TextLanguage, () => Promise<Extension>> = {
  plain: async () => [],
  json: async () => { const { json, jsonParseLinter } = await import('@codemirror/lang-json'); return [json(), linter(jsonParseLinter())]; },
  xml: async () => (await import('@codemirror/lang-xml')).xml(),
  markdown: async () => (await import('@codemirror/lang-markdown')).markdown(),
  sql: async () => (await import('@codemirror/lang-sql')).sql(),
  javascript: async () => (await import('@codemirror/lang-javascript')).javascript(),
  typescript: async () => (await import('@codemirror/lang-javascript')).javascript({ typescript: true }),
  css: async () => (await import('@codemirror/lang-css')).css(),
  html: async () => (await import('@codemirror/lang-html')).html(),
  csharp: async () => StreamLanguage.define((await import('@codemirror/legacy-modes/mode/clike')).csharp),
  python: async () => (await import('@codemirror/lang-python')).python(),
};
const loaded = new Map<TextLanguage, Promise<Extension>>();
export function loadEditorLanguage(language: TextLanguage) {
  if (!loaded.has(language)) loaded.set(language, loaders[language]().catch(error => { loaded.delete(language); throw error; }));
  return loaded.get(language)!;
}
