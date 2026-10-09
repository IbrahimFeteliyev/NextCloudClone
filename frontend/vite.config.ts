import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
export default defineConfig({ plugins: [react(), tailwindcss()], server: { port: 5173, strictPort: true },
  optimizeDeps: { include: ['video.js', 'photoswipe', 'pdfjs-dist', 'react-markdown', 'remark-gfm', 'papaparse', '@tanstack/react-table', 'dompurify', 'codemirror', '@codemirror/state', '@codemirror/view', '@codemirror/commands', '@codemirror/search', '@codemirror/lang-json', '@codemirror/lang-xml', '@codemirror/lang-sql', '@codemirror/lang-javascript', '@codemirror/lang-css', '@codemirror/lang-html', '@codemirror/lang-python', '@codemirror/lang-markdown', '@codemirror/legacy-modes/mode/clike', '@codemirror/language', '@codemirror/lint'] } });
