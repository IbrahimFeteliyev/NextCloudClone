export function decodeText(bytes: Uint8Array, truncated = false) {
  const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf-16le' : bytes[0] === 0xfe && bytes[1] === 0xff ? 'utf-16be' : 'utf-8';
  const text = new TextDecoder(encoding, { fatal: !truncated }).decode(bytes, { stream: truncated });
  if (text.includes('\0')) throw new Error('This file contains binary data. Download it to inspect it locally.');
  return { text, encoding, bom: encoding === 'utf-8' && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf };
}
export const textBytes = (text: string, bom = false) => new TextEncoder().encode((bom ? '\uFEFF' : '') + text).length;
