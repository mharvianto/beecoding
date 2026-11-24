// v-modal="close" on a role="dialog" element: moves focus into it when it appears, keeps Tab inside it, closes on Escape,
// and gives focus back to whatever opened it. Add `data-autofocus` to the element that should get focus first.
const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]):not([type=hidden]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';
const state = new WeakMap();

export const vModal = {
  mounted(el, binding) {
    const s = { close: binding.value, opener: document.activeElement };
    s.onKey = (e) => {
      if (e.key === 'Escape') { e.stopPropagation(); s.close?.(); return; }
      if (e.key !== 'Tab') return;
      const items = [...el.querySelectorAll(FOCUSABLE)].filter((n) => n.offsetParent !== null);
      if (!items.length) { e.preventDefault(); el.focus(); return; }
      const first = items[0], last = items[items.length - 1];
      if (e.shiftKey && (document.activeElement === first || document.activeElement === el)) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    };
    state.set(el, s);
    el.addEventListener('keydown', s.onKey);
    if (!el.hasAttribute('tabindex')) el.tabIndex = -1;
    // after the dialog's own content (and an `autofocus` input of its own) has rendered
    setTimeout(() => {
      if (el.contains(document.activeElement)) return;
      (el.querySelector('[data-autofocus]') || el.querySelector(FOCUSABLE) || el).focus();
    }, 0);
  },
  updated(el, binding) { const s = state.get(el); if (s) s.close = binding.value; },   // the handler is usually an inline arrow
  unmounted(el) {
    const s = state.get(el);
    if (!s) return;
    el.removeEventListener('keydown', s.onKey);
    state.delete(el);
    if (s.opener?.isConnected) s.opener.focus?.();
  },
};
