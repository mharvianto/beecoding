// Formulas in a problem statement ($x^2$, $$...$$, \(...\), \[...\]). They are pulled out of the markdown BEFORE it is parsed (so '_' and
// '*' inside a formula are not read as emphasis), the markdown goes through DOMPurify exactly as before, and KaTeX's own output is put
// back afterwards — KaTeX never passes through the sanitiser's allow-list, and the statement text never reaches KaTeX's HTML.
// The statement picture (server, StatementMath.cs) uses the same delimiters and the same limits: keep the two in step.

export const MAX_FORMULAS = 200;   // per statement
export const MAX_TEX_CHARS = 500;  // per formula

const OPEN = '', CLOSE = '';
const PLACEHOLDER = new RegExp(`${OPEN}(\\d+)${CLOSE}`, 'g');
const CODE = /(```[\s\S]*?```|`[^`\n]*`)/;
// "$5 and $10" is money: the opening $ needs a non-space after it, the closing one a non-space before it and no digit after it.
const RULES = [
  [/\\\[([\s\S]+?)\\\]/g, true],
  [/\$\$([\s\S]+?)\$\$/g, true],
  [/\\\(([\s\S]+?)\\\)/g, false],
  [/(?<![\\$])\$(?!\s)([^$\n]+?)(?<!\s)\$(?!\d)/g, false],
];

/** The text with each formula replaced by a placeholder, and the formulas ({ tex, display }). Code is left alone. */
export function extractMath(src) {
  const store = [];
  if (!src || (!src.includes('$') && !src.includes('\\'))) return { text: src || '', store };
  const stash = (whole, tex, display) => {
    if (store.length >= MAX_FORMULAS || tex.length > MAX_TEX_CHARS) return whole;
    store.push({ tex: tex.trim(), display });
    return `${OPEN}${store.length - 1}${CLOSE}`;
  };
  const text = src.split(CODE).map((part, i) => {
    if (i % 2 === 1) return part;   // code: kept as typed
    for (const [re, display] of RULES) part = part.replace(re, (m, tex) => stash(m, tex, display));
    return part;
  }).join('');
  return { text, store };
}

const escapeHtml = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** Put the formulas back as plain (escaped) source text: what is shown until KaTeX has loaded. */
export function restorePlain(html, store) {
  return html.replace(PLACEHOLDER, (_, n) => `<span>${escapeHtml(store[+n]?.tex ?? '')}</span>`);
}

/** Put the formulas back typeset. A formula KaTeX cannot read is shown in red as typed (throwOnError: false), never thrown. */
export function restoreTypeset(html, store, katex) {
  return html.replace(PLACEHOLDER, (_, n) => {
    const f = store[+n];
    if (!f) return '';
    return katex.renderToString(f.tex, {
      throwOnError: false, displayMode: f.display,
      trust: false,          // \href, \url, \includegraphics, \htmlClass ... stay off
      maxExpand: 1000,       // a self-expanding macro stops here
      maxSize: 4,            // em: \rule{99999em}{99999em} cannot draw a page-sized block
      strict: 'ignore', output: 'htmlAndMathml',
    });
  });
}

let katexPromise = null;
/** KaTeX (and its stylesheet) is only fetched the first time a statement has a formula. */
export function loadKatex() {
  katexPromise ??= Promise.all([import('katex'), import('katex/dist/katex.min.css')]).then(([m]) => m.default ?? m);
  return katexPromise;
}
