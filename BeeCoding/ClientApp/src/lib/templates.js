import { tr } from './i18n';
// Generic starter templates per language, dropped into a fresh editor.
export const CODE_TEMPLATES = {
  cpp: `#include <bits/stdc++.h>
using namespace std;

int main() {
    ios::sync_with_stdio(false);
    cin.tie(nullptr);

    return 0;
}
`,
  c: `#include <stdio.h>
#include <stdlib.h>

int main(void) {

    return 0;
}
`,
};

// True when `code` is still an untouched language template — i.e. safe to replace
// when the student switches language.
export function isPristine(code) {
  const t = (code || '').trim();
  if (t === '') return true;
  return [CODE_TEMPLATES.c, CODE_TEMPLATES.cpp].some((s) => s.trim() === t);
}

// Supported languages, in preference order.
export const SUPPORTED_LANGS = ['c', 'cpp'];

// Parse a problem's "allowed languages" csv → array. Empty csv => every language.
export function allowedLangs(csv) {
  const set = (csv || '')
    .split(/[,\s;/]+/)
    .map((s) => s.trim().toLowerCase())
    .map((s) => (s === 'c++' ? 'cpp' : s))
    .filter((s) => SUPPORTED_LANGS.includes(s));
  return set.length ? [...new Set(set)] : [...SUPPORTED_LANGS];
}

// Human label for an "allowed languages" csv: "Any" / "C" / "C++" / "C/C++".
export function langLabel(csv) {
  const set = (csv || '')
    .split(/[,\s;/]+/)
    .map((s) => s.trim().toLowerCase())
    .map((s) => (s === 'c++' ? 'cpp' : s))
    .filter((s) => SUPPORTED_LANGS.includes(s));
  if (!set.length) return tr('Any');
  return [...new Set(set)].map((s) => (s === 'c' ? 'C' : 'C++')).join('/');
}
