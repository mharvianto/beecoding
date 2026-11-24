import { describe, it, expect, vi, beforeEach } from 'vitest';
import { vModal } from '../src/lib/vModal';

function mountDialog(close) {
  document.body.innerHTML = '<button id="opener">open</button>';
  const opener = document.getElementById('opener'); opener.focus();
  const el = document.createElement('div');
  el.innerHTML = '<button id="a">a</button><button id="b">b</button>';
  document.body.append(el);
  // jsdom has no layout: offsetParent is null, so pretend everything is visible
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', { configurable: true, get() { return document.body; } });
  vModal.mounted(el, { value: close });
  return { el, opener };
}
const key = (el, k, extra = {}) => el.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true, ...extra }));

describe('v-modal', () => {
  beforeEach(() => vi.useFakeTimers());

  it('moves focus in, wraps Tab at both ends, closes on Escape and gives focus back', () => {
    const close = vi.fn();
    const { el, opener } = mountDialog(close);
    vi.runAllTimers();
    expect(document.activeElement.id).toBe('a');
    document.getElementById('b').focus();
    key(el, 'Tab'); expect(document.activeElement.id).toBe('a');                 // last -> first
    key(el, 'Tab', { shiftKey: true }); expect(document.activeElement.id).toBe('b');   // first -> last
    key(el, 'Escape'); expect(close).toHaveBeenCalledOnce();
    vModal.unmounted(el);
    expect(document.activeElement).toBe(opener);
  });

  it('keeps an input that already took focus itself', () => {
    document.body.innerHTML = '';
    const el = document.createElement('div'); el.innerHTML = '<button>x</button><input id="i">';
    document.body.append(el); document.getElementById('i').focus();
    vModal.mounted(el, { value: () => {} }); vi.runAllTimers();
    expect(document.activeElement.id).toBe('i');
  });
});
