// Signs that the page is driven by automation (Playwright, Selenium, Puppeteer, headless Chrome). Cheap and easy to fake: a deterrent
// against casual scripts, not a lock — it cannot see a model reading screenshots or a person's desktop being controlled.
let cached;
export function clientFlags() {
  if (cached !== undefined) return cached;
  const flags = [];
  try {
    if (navigator.webdriver === true) flags.push('webdriver');
    if (/HeadlessChrome/i.test(navigator.userAgent || '')) flags.push('headless');
  } catch { /* an odd browser: no flags */ }
  return (cached = flags.join(','));
}
