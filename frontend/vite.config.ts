import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
export default defineConfig({ plugins: [react(), tailwindcss()], server: { port: 5173, strictPort: true },
  optimizeDeps: { include: ['video.js', 'photoswipe', 'pdfjs-dist', 'react-markdown', 'remark-gfm', 'papaparse', '@tanstack/react-table', 'dompurify'] } });
