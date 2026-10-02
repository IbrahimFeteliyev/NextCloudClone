// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import ReactMarkdown from 'react-markdown';
import { sanitizeSvg } from './ImagePreview';
describe('Uploaded content safety', () => {
  it('removes SVG scripts, foreign HTML, event handlers and remote references', () => {
    const result = sanitizeSvg('<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><foreignObject><div>unsafe</div></foreignObject><rect onclick="alert(1)" width="50"/><image href="https://tracker.invalid/image.png"/><path fill="url(https://tracker.invalid/style)"/></svg>');
    expect(result).not.toMatch(/script|foreignObject|onclick|https:\/\/tracker/); expect(result).toContain('<rect');
  });
  it('does not execute raw HTML or javascript links in Markdown', () => {
    const result = renderToStaticMarkup(<ReactMarkdown skipHtml>{'# Safe\n\n<script>alert(1)</script>\n\n[Unsafe](javascript:alert(1))'}</ReactMarkdown>);
    expect(result).toContain('<h1>Safe</h1>'); expect(result).not.toContain('<script'); expect(result).not.toContain('href="javascript:');
  });
});
