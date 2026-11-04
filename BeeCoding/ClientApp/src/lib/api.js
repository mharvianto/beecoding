import { withBase } from './base';

async function request(method, url, body) {
  const opts = {
    method,
    credentials: 'include',
    // The server derives the user's local calendar day from this (AI daily quota), see UtcOffsetMiddleware.
    headers: { 'X-UTC-Offset': String(-new Date().getTimezoneOffset()) },
  };
  if (body !== undefined) {
    opts.headers['Content-Type'] = 'application/json';
    opts.body = JSON.stringify(body);
  }
  const res = await fetch(url.startsWith('/') ? withBase(url) : url, opts);
  if (res.status === 204) return null;
  const text = await res.text();
  const data = text ? tryParse(text) : null;
  if (!res.ok) {
    const msg = friendlyMessage(res.status, data) || res.statusText || `HTTP ${res.status}`;
    const err = new Error(msg);
    err.status = res.status;
    err.body = data;
    if (data && typeof data === 'object') Object.assign(err, data);
    throw err;
  }
  return data;
}

function tryParse(t) {
  try { return JSON.parse(t); } catch { return t; }
}

function friendlyMessage(status, data) {
  if (typeof data === 'string' && /^\s*<(!doctype|html)/i.test(data)) {
    if (status === 429) return 'Too many requests — please wait a moment and try again.';
    if (status >= 500) return 'Server error — please try again shortly.';
    return `Request failed (HTTP ${status}).`;
  }
  return typeof data === 'string' ? data : data?.message || data?.title || data?.detail;
}

/** multipart upload of one file as field "file" (the browser sets the boundary, so no Content-Type here). */
async function upload(url, file) {
  const body = new FormData();
  body.append('file', file);
  const res = await fetch(withBase(url), { method: 'POST', credentials: 'include', body });
  const text = await res.text();
  const data = text ? tryParse(text) : null;
  if (!res.ok) throw new Error(friendlyMessage(res.status, data) || `HTTP ${res.status}`);
  return data;
}

export const api = {
  upload,
  get: (u) => request('GET', u),
  post: (u, b) => request('POST', u, b ?? {}),
  put: (u, b) => request('PUT', u, b ?? {}),
  patch: (u, b) => request('PATCH', u, b ?? {}),
  del: (u, b) => request('DELETE', u, b),
};
