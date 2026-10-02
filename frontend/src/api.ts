const base = import.meta.env.VITE_API_URL || 'http://localhost:5050/api';
export const getToken = () => sessionStorage.getItem('atlas-token');
export class ApiError extends Error { status: number; constructor(message: string, status: number) { super(message); this.status = status; } }
export async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  if (!(options.body instanceof FormData) && options.body) headers.set('Content-Type', 'application/json');
  const token = getToken(); if (token) headers.set('Authorization', `Bearer ${token}`);
  let response: Response;
  try { response = await fetch(`${base}${path}`, { ...options, headers }); }
  catch { throw new ApiError('Unable to reach Atlas. Start the API, PostgreSQL, and MinIO, then try again.', 0); }
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    if (response.status === 401 && token) { sessionStorage.removeItem('atlas-token'); window.dispatchEvent(new Event('atlas-session-expired')); }
    throw new ApiError(data.message || data.title || `Request failed (${response.status}).`, response.status);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}
export async function download(id: string, name: string) {
  let response: Response;
  try { response = await fetch(`${base}/files/${id}/download`, { headers: { Authorization: `Bearer ${getToken()}` } }); }
  catch { throw new ApiError('Unable to reach Atlas. Check that the API is running.', 0); }
  if (!response.ok) {
    if (response.status === 401) { sessionStorage.removeItem('atlas-token'); window.dispatchEvent(new Event('atlas-session-expired')); }
    const error = await response.json().catch(() => ({})); throw new ApiError(error.message || 'Download failed.', response.status);
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a'); link.href = url; link.download = name; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export const resourcePath = (r: { type: string; id: string }) => `/resources/${r.type}/${r.id}`;
