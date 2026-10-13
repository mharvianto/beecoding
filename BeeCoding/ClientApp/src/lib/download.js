import { withBase } from './base';

// Fetch a file from the API with the session cookie and hand it to the browser as a download. The file name
// comes from the response's Content-Disposition header; `fallbackName` is used when there is none. Throws
// an Error carrying the server's message (or the HTTP status) so callers can show it.
export async function downloadFile(url, fallbackName) {
  const res = await fetch(url.startsWith('/') ? withBase(url) : url, { credentials: 'include' });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(res.status === 403 ? 'You are not allowed to download this.' : (text && text.length < 200 ? text : `Download failed (HTTP ${res.status}).`));
  }
  const blob = await res.blob();
  const disposition = res.headers.get('content-disposition') || '';
  const name = decodeURIComponent(disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1] || '') || disposition.match(/filename="?([^";]+)"?/i)?.[1] || fallbackName;
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
