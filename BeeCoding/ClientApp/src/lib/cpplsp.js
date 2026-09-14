import { withBase } from './base';

/**
 * Thin LSP client for the clangd bridge (`/lsp/cpp`). One JSON-RPC message per
 * WebSocket frame. Exposes just what the Monaco providers need: completion, hover,
 * signature help, and a diagnostics callback. Silent no-op if the bridge is off.
 */
export class CppLsp {
  constructor(language = 'cpp', formatStyle = 'LLVM') {
    this.language = language === 'c' ? 'c' : 'cpp';
    this.formatStyle = formatStyle;
    this._id = 0;
    this._pending = new Map();
    this._version = 1;
    this._diagCb = null;
    this.uri = null;
    this.ws = null;
    this.ready = false;
  }

  connect(timeoutMs = 4000) {
    return new Promise((resolve, reject) => {
      let settled = false;
      // In dev the page is on :5173 and Vite's proxy handles this WebSocket
      // unreliably — go straight to Kestrel (same as the SignalR hub). Prod:
      // relative to the page, nginx proxies /lsp fine.
      const base = import.meta.env.DEV
        ? (import.meta.env.VITE_BACKEND_URL || 'http://localhost:5048').replace(/^http/, 'ws')
        : (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host;
      try {
        this.ws = new WebSocket(
          `${base}${withBase('/lsp/cpp')}?lang=${this.language}&style=${encodeURIComponent(this.formatStyle)}`);
      } catch (e) { return reject(e); }

      const timer = setTimeout(() => { if (!settled) { settled = true; this.close(); reject(new Error('lsp timeout')); } }, timeoutMs);

      this.ws.onmessage = (ev) => {
        let m;
        try { m = JSON.parse(ev.data); } catch { return; }
        if (m.beecoding === 'ready') {
          this.uri = m.uri;
          this.rootUri = m.rootUri;
          this.ready = true;
          if (!settled) { settled = true; clearTimeout(timer); resolve(this); }
          return;
        }
        if (m.id != null && this._pending.has(m.id)) {
          const { resolve: res } = this._pending.get(m.id);
          this._pending.delete(m.id);
          res(m.error ? null : m.result);
          return;
        }
        if (m.method === 'textDocument/publishDiagnostics') {
          this._diagCb?.(m.params?.diagnostics ?? []);
          return;
        }
        // server -> client request: acknowledge so clangd isn't left waiting
        if (m.id != null && m.method) {
          const result = m.method === 'workspace/configuration'
            ? (m.params?.items ?? [{}]).map(() => null)
            : null;
          this._send({ jsonrpc: '2.0', id: m.id, result });
        }
      };
      this.ws.onerror = () => { if (!settled) { settled = true; clearTimeout(timer); reject(new Error('lsp error')); } };
      this.ws.onclose = () => { this.ready = false; for (const { resolve: r } of this._pending.values()) r(null); this._pending.clear(); };
    });
  }

  _send(o) { if (this.ws && this.ws.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(o)); }
  notify(method, params) { this._send({ jsonrpc: '2.0', method, params }); }
  request(method, params) {
    if (!this.ready) return Promise.resolve(null);
    const id = ++this._id;
    this._send({ jsonrpc: '2.0', id, method, params });
    return new Promise((resolve) => {
      this._pending.set(id, { resolve });
      setTimeout(() => { if (this._pending.delete(id)) resolve(null); }, 5000);
    });
  }

  async open(text) {
    await this.request('initialize', {
      processId: null,
      rootUri: this.rootUri,
      capabilities: {
        textDocument: {
          synchronization: { didSave: false, willSave: false },
          completion: {
            completionItem: { snippetSupport: true, documentationFormat: ['markdown', 'plaintext'] },
            contextSupport: true,
          },
          hover: { contentFormat: ['markdown', 'plaintext'] },
          signatureHelp: { signatureInformation: { documentationFormat: ['markdown', 'plaintext'] } },
          publishDiagnostics: {},
          formatting: {},
        },
      },
    });
    this.notify('initialized', {});
    this.notify('textDocument/didOpen', {
      textDocument: { uri: this.uri, languageId: this.language, version: 1, text: text || '' },
    });
  }

  didChange(text) {
    if (!this.ready) return;
    this.notify('textDocument/didChange', {
      textDocument: { uri: this.uri, version: ++this._version },
      contentChanges: [{ text: text || '' }],
    });
  }

  completion(pos, triggerCharacter) {
    return this.request('textDocument/completion', {
      textDocument: { uri: this.uri },
      position: pos,
      context: triggerCharacter
        ? { triggerKind: 2, triggerCharacter }
        : { triggerKind: 1 },
    });
  }
  hover(pos) { return this.request('textDocument/hover', { textDocument: { uri: this.uri }, position: pos }); }
  signatureHelp(pos) { return this.request('textDocument/signatureHelp', { textDocument: { uri: this.uri }, position: pos }); }
  formatting(tabSize = 4, insertSpaces = true) {
    return this.request('textDocument/formatting', {
      textDocument: { uri: this.uri },
      options: { tabSize, insertSpaces },
    });
  }

  onDiagnostics(cb) { this._diagCb = cb; }

  close() {
    try { this.ws?.close(); } catch { /* ignore */ }
    this.ws = null;
    this.ready = false;
  }
}
