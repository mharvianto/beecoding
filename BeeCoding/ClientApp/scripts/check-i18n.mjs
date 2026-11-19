// Lists texts passed to tr('...') that have no Indonesian entry in src/lib/i18n/id.js, and entries no code mentions any more.
// Run from ClientApp: `npm run check:i18n`. A missing entry is not a bug (the text just stays English); this is a to-do list.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

const src = new URL('../src/', import.meta.url).pathname;
const files = [];
(function walk(dir) {
  for (const f of readdirSync(dir)) {
    const p = join(dir, f);
    if (statSync(p).isDirectory()) walk(p);
    else if (/\.(vue|js)$/.test(f) && !p.includes('lib/i18n')) files.push(p);
  }
})(src);

const code = files.map((f) => readFileSync(f, 'utf8')).join('\n');
const used = new Set();
for (const m of code.matchAll(/(?<![\w.])tr\(\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")/g)) used.add((m[1] ?? m[2]).replace(/\\(['"\\])/g, '$1'));

const { default: id } = await import(new URL('../src/lib/i18n/id.js', import.meta.url));
const { default: server } = await import(new URL('../src/lib/i18n/id-server.js', import.meta.url));   // written by the server, not in client code
const missing = [...used].filter((k) => !(k in id));
// texts shown through a variable (tr(label)) are not in tr(...) literally, so "unused" is only a hint
const unused = Object.keys(id).filter((k) => !(k in server) && !code.includes(k) && !code.includes(k.replace(/'/g, "\\'")));
console.log(`${used.size} texts in tr(...), ${Object.keys(id).length} translations`);
console.log(`Without a translation (${missing.length}):`); missing.forEach((k) => console.log('  ' + k));
console.log(`Translations no code mentions (${unused.length}):`); unused.forEach((k) => console.log('  ' + k));
