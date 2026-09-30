import { watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

/**
 * Two-way binding between a page's filter/paging state and the URL query string, so a search,
 * filter or page survives refresh and back/forward and can be bookmarked or shared. (Practice
 * and Leaderboard do this by hand; this is the same pattern for everything else.)
 *
 * `fields` is `{ key: { ref, def, int?, allowed? } }` — or a function returning that, for pages
 * whose fields depend on the active tab. A value equal to its default is left out of the URL.
 *   - `int`     positive integer (a page number); anything else falls back to `def`
 *   - `allowed` whitelist for a string value; anything else falls back to `def`
 *
 * Usage: call `read()` once during setup (before the first load), `write()` whenever a load
 * starts, and pass `onExternalChange` to reload when the URL changes behind the page's back
 * (a pasted link, browser back/forward).
 *
 * `path` (optional) supplies the route path for pages whose path carries a tab: passing it
 * explicitly keeps two quick `replace` calls from racing on the not-yet-updated current route.
 */
export function useUrlQuery(fields, { onExternalChange, path, active } = {}) {
  const route = useRoute();
  const router = useRouter();
  const current = () => (typeof fields === 'function' ? fields() : fields);

  function parse(f, raw) {
    if (Array.isArray(raw)) raw = raw[0];
    if (raw == null || raw === '') return f.def;
    if (f.int) {
      const n = Math.floor(Number(raw));
      return Number.isFinite(n) && n >= 1 ? n : f.def;
    }
    const s = String(raw);
    return f.allowed && !f.allowed.includes(s) ? f.def : s;
  }

  /** Apply the URL to the refs. Returns true if anything changed. */
  function read() {
    let changed = false;
    for (const [key, f] of Object.entries(current())) {
      const v = parse(f, route.query[key]);
      if (f.ref.value !== v) { f.ref.value = v; changed = true; }
    }
    return changed;
  }

  /** Write the refs to the URL (replace, so it doesn't pile up history entries). */
  function write() {
    const query = {};
    for (const [key, f] of Object.entries(current())) {
      const v = f.ref.value;
      if (v == null || v === f.def || (typeof v === 'string' && v.trim() === '')) continue;
      query[key] = String(v);
    }
    router.replace(path ? { path: path(), query } : { query });
  }

  watch(() => route.fullPath, () => {
    if (active && !active()) return;
    if (read()) onExternalChange?.();
  });

  return { read, write };
}

/** Last page that exists for `total` rows (at least 1). */
export const lastPage = (total, pageSize) => Math.max(1, Math.ceil(total / pageSize));
