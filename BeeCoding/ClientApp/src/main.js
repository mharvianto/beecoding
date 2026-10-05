// Pull the signed-in user's synced preferences into localStorage first (lib/prefs.js), then load the app:
// theme, editor font and the like are read from localStorage once, when their modules are first imported.
import { bootstrapPrefs } from './lib/prefs';

bootstrapPrefs().finally(() => import('./boot'));
