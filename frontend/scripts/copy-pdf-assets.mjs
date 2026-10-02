import { cpSync, mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const source = fileURLToPath(new URL('../node_modules/pdfjs-dist/', import.meta.url));
const target = fileURLToPath(new URL('../public/pdfjs/', import.meta.url));
const version = JSON.parse(readFileSync(`${source}/package.json`, 'utf8')).version;
if (!existsSync(`${target}/version.txt`) || readFileSync(`${target}/version.txt`, 'utf8') !== version) {
  mkdirSync(target, { recursive: true });
  for (const directory of ['cmaps', 'standard_fonts', 'wasm']) cpSync(`${source}/${directory}`, `${target}/${directory}`, { recursive: true });
  writeFileSync(`${target}/version.txt`, version);
}
