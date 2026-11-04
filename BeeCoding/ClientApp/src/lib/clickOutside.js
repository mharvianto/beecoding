// v-click-outside="close": calls `close` when the pointer goes down anywhere outside the element, or Escape is pressed.
// Used by the dropdown menus instead of a full-screen invisible overlay: such an overlay swallows the mouse wheel, so
// the page behind an open menu could not be scrolled.
const handlers = new WeakMap();

export const vClickOutside = {
  mounted(el, binding) {
    const onDown = (e) => { if (!el.contains(e.target)) binding.value?.(e); };
    const onKey = (e) => { if (e.key === 'Escape') binding.value?.(e); };
    handlers.set(el, { onDown, onKey });
    document.addEventListener('pointerdown', onDown, true);
    document.addEventListener('keydown', onKey);
  },
  updated(el, binding) {   // keep the latest closure (the handler is usually an inline arrow)
    const h = handlers.get(el);
    if (!h) return;
    document.removeEventListener('pointerdown', h.onDown, true);
    document.removeEventListener('keydown', h.onKey);
    h.onDown = (e) => { if (!el.contains(e.target)) binding.value?.(e); };
    h.onKey = (e) => { if (e.key === 'Escape') binding.value?.(e); };
    document.addEventListener('pointerdown', h.onDown, true);
    document.addEventListener('keydown', h.onKey);
  },
  unmounted(el) {
    const h = handlers.get(el);
    if (!h) return;
    document.removeEventListener('pointerdown', h.onDown, true);
    document.removeEventListener('keydown', h.onKey);
    handlers.delete(el);
  },
};
