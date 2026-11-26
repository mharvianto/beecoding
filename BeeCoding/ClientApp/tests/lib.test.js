import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { localDayKey } from '../src/lib/localDay';
import { saveDraft, loadDraft, markDraftAccepted, pruneDrafts, clearAllDrafts } from '../src/lib/draft';
import { SYNCED_KEYS } from '../src/lib/prefs';
import { tr } from '../src/lib/i18n';
import { setDocLang } from '../src/lib/docLang';
import id from '../src/lib/i18n/id.js';

beforeEach(() => localStorage.clear());
afterEach(() => { vi.useRealTimers(); setDocLang('en'); });

describe('localDayKey', () => {
  it('uses the local calendar day, not UTC', () => {
    expect(localDayKey(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');   // local constructor: month is 0-based
    expect(localDayKey(new Date(2026, 11, 31, 0, 0))).toBe('2026-12-31');
  });
});

describe('drafts', () => {
  it('round-trips and ignores a corrupt entry', () => {
    saveDraft(1, 'p', 'int main(){}', 'cpp');
    expect(loadDraft(1, 'p')).toMatchObject({ code: 'int main(){}', lang: 'cpp' });
    localStorage.setItem('beecoding.draft.1.bad', '{not json');
    expect(loadDraft(1, 'bad')).toBeNull();
  });

  it('expires a solved draft after an hour and an unsolved one after a week', () => {
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-01-01T00:00:00Z'));
    saveDraft(1, 'solved', 'a', 'c'); markDraftAccepted(1, 'solved');
    saveDraft(1, 'open', 'b', 'c');
    vi.setSystemTime(new Date('2026-01-01T01:30:00Z')); pruneDrafts();
    expect(loadDraft(1, 'solved')).toBeNull();
    expect(loadDraft(1, 'open')).not.toBeNull();
    vi.setSystemTime(new Date('2026-01-09T00:00:00Z')); pruneDrafts();
    expect(loadDraft(1, 'open')).toBeNull();
  });

  it('keeps only the newest 50', () => {
    for (let i = 0; i < 55; i++) saveDraft(1, `p${i}`, 'x', 'c');
    expect(loadDraft(1, 'p0')).toBeNull();
    expect(loadDraft(1, 'p54')).not.toBeNull();
  });

  it('sign-out removes every draft, even one the index lost', () => {
    saveDraft(1, 'a', 'x', 'c');
    localStorage.setItem('beecoding.draft.2.orphan', '{"code":"x"}');
    clearAllDrafts();
    expect(loadDraft(1, 'a')).toBeNull();
    expect(localStorage.getItem('beecoding.draft.2.orphan')).toBeNull();
  });
});

describe('synced preferences', () => {
  it('match the server allow-list (PreferencesController) exactly', () => {
    const src = readFileSync(resolve(process.cwd(), '../Controllers/PreferencesController.cs'), 'utf8')   // npm test runs from ClientApp/;
    const block = src.slice(src.indexOf('"beecoding.theme"') - 10, src.indexOf('}', src.indexOf('"beecoding.admin.aiGranularity"')));
    const server = [...block.matchAll(/"(beecoding\.[^"]+)"/g)].map((m) => m[1]).sort();
    expect([...SYNCED_KEYS].sort()).toEqual(server);
  });
});

describe('tr()', () => {
  it('is the English text in English and fills {params}', () => {
    expect(tr('Save')).toBe('Save');
    expect(tr('Hello {name}', { name: 'Budi' })).toBe('Hello Budi');
  });
  it('translates to Indonesian, falling back to English for an unknown text', () => {
    setDocLang('id');
    expect(tr('Loading…')).toBe(id['Loading…']);
    expect(tr('A text nobody translated')).toBe('A text nobody translated');
  });
  it('matches a server message that carries a value', () => {
    const key = Object.keys(id).find((k) => /\{p0\}/.test(k) && !/\{p1\}/.test(k));
    setDocLang('id');
    const english = key.replace('{p0}', '3');
    expect(tr(english)).toBe(id[key].replaceAll('{p0}', '3'));
  });
});

import { CODE_TEMPLATES, isPristine } from '../src/lib/templates';
describe('starter templates', () => {
  it('the C++ starter avoids the umbrella header, and a draft saved with the old one still counts as untouched', () => {
    expect(CODE_TEMPLATES.cpp).not.toContain('bits/stdc++.h');
    expect(isPristine(CODE_TEMPLATES.cpp.replace('<iostream>', '<bits/stdc++.h>'))).toBe(true);
    expect(isPristine(CODE_TEMPLATES.cpp + '// mine')).toBe(false);
  });
});

import { api } from '../src/lib/api';
describe('api', () => {
  it('announces a 403 mfa_required answer so the app can refresh the user', async () => {
    const seen = vi.fn(); window.addEventListener('beecoding:mfa-required', seen);
    globalThis.fetch = vi.fn(async () => new Response(JSON.stringify({ message: 'Turn on two-step verification to continue.', code: 'mfa_required' }), { status: 403 }));
    await expect(api.get('/api/boards')).rejects.toMatchObject({ status: 403 });
    expect(seen).toHaveBeenCalledTimes(1);
  });
});
