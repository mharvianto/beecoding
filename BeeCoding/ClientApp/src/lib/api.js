import { withBase } from './base';

async function request(method, url, body) {
  const opts = {
    method,
    credentials: 'include',
    headers: {},
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

export const api = {
  get: (u) => request('GET', u),
  post: (u, b) => request('POST', u, b ?? {}),
  put: (u, b) => request('PUT', u, b ?? {}),
  patch: (u, b) => request('PATCH', u, b ?? {}),
  del: (u, b) => request('DELETE', u, b),
};
