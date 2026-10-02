import { resolvePreview } from './features/file-preview/previewResolver';
export const isOfficeFile = (resource: { type: string; name: string; contentType?: string | null }) => resource.type === 'file' && resolvePreview(resource) === 'office';
export type OfficeConfiguration = {
  documentType: 'word' | 'cell' | 'slide';
  document: { fileType: string; key: string; title: string; url: string; permissions: { edit: boolean; download: boolean; print: boolean } };
  editorConfig: { mode: 'view' | 'edit'; callbackUrl: string; user: { id: string; name: string } };
  token: string;
  width: string;
  height: string;
};
export type OfficeResponse = { documentServerUrl: string; mode: 'view' | 'edit'; version: number; config: OfficeConfiguration };
export type OfficeEditor = { destroyEditor(): void };
declare global {
  interface Window {
    DocsAPI?: { DocEditor: new (id: string, config: OfficeConfiguration & { events: Record<string, (event: { data?: { errorDescription?: string } }) => void> }) => OfficeEditor };
  }
}
let apiLoad: Promise<void> | undefined;
let apiUrl = '';
export function loadOfficeApi(base: string): Promise<void> {
  const url = `${base.replace(/\/$/, '')}/web-apps/apps/api/documents/api.js`;
  if (apiLoad && apiUrl === url) return apiLoad;
  apiUrl = url;
  apiLoad = new Promise((resolve, reject) => {
    const script = document.createElement('script'); script.src = url; script.async = true;
    const fail = () => { clearTimeout(timer); script.remove(); apiLoad = undefined; reject(new Error('Unable to load ONLYOFFICE. Check that Document Server is running, then try again.')); };
    const timer = window.setTimeout(fail, 15000);
    script.onload = () => { clearTimeout(timer); if (window.DocsAPI) resolve(); else fail(); };
    script.onerror = fail;
    document.head.appendChild(script);
  });
  return apiLoad;
}
