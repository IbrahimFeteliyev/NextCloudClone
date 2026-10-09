import { describe, expect, it } from 'vitest';
import { EditorState } from '@codemirror/state';
import { decodeText, textBytes } from './textEncoding';
describe('Text file encoding and round trips', () => {
  it('preserves Unicode, UTF-8 BOM and Windows line endings', () => {
    const input = '\uFEFFAzərbaycan\r\nsecond\r\n'; const bytes = new TextEncoder().encode(input); const result = decodeText(bytes);
    expect(result.bom).toBe(true); expect(result.text).toBe('Azərbaycan\r\nsecond\r\n'); expect(textBytes(result.text, true)).toBe(bytes.length);
    const state = EditorState.create({ doc: result.text, extensions: [EditorState.lineSeparator.of('\r\n')] }); expect(state.sliceDoc()).toBe(result.text);
  });
  it('recognizes UTF-16 as read-only and rejects binary or invalid complete UTF-8', () => {
    expect(decodeText(new Uint8Array([255, 254, 65, 0])).encoding).toBe('utf-16le');
    expect(() => decodeText(new Uint8Array([65, 0, 66]))).toThrow('binary'); expect(() => decodeText(new Uint8Array([255]))).toThrow();
  });
  it('does not invent replacement characters at a truncated multi-byte boundary', () => {
    expect(decodeText(new Uint8Array([65, 0xc9]), true).text).toBe('A');
  });
  it('uses byte size rather than character count for the editing limit', () => { expect(textBytes('ə')).toBe(2); expect(textBytes('')).toBe(0); });
});
