import { createRouter, createWebHistory } from 'vue-router';
import { useAuth } from './stores/auth';
import { tr } from './lib/i18n';

// meta.keyed: the :params name one entity (a board, a problem) whose page loads its data once when created, so a link
// from one entity to another of the same kind must remount the page (see the RouterView key in App.vue). Do NOT add it
// where a param only picks a tab (admin, account, org-admin): remounting would reset the page for nothing.
const routes = [
  { path: '/', component: () => import('./views/Landing.vue'), meta: { anon: true } },
  { path: '/login', component: () => import('./views/Login.vue'), meta: { anon: true } },
  { path: '/register', component: () => import('./views/Register.vue'), meta: { anon: true } },
  { path: '/verify-email', component: () => import('./views/VerifyEmail.vue'), meta: { public: true } },
  { path: '/forgot-password', component: () => import('./views/ForgotPassword.vue'), meta: { anon: true } },
  { path: '/reset-password', component: () => import('./views/ResetPassword.vue'), meta: { anon: true } },
  { path: '/privacy', component: () => import('./views/Privacy.vue'), meta: { public: true } },
  { path: '/terms', component: () => import('./views/Terms.vue'), meta: { public: true } },
  { path: '/design', component: () => import('./views/Design.vue'), meta: { public: true } },   // living style guide, static UI only
  { path: '/oauth/consent', component: () => import('./views/OAuthConsent.vue') },   // an AI assistant asks a signed-in teacher for access
  { path: '/dashboard', redirect: '/account' },
  { path: '/boards', component: () => import('./views/Dashboard.vue') },
  { path: '/join/:code', component: () => import('./views/JoinBoard.vue'), props: true, meta: { keyed: true } },
  { path: '/bank', component: () => import('./views/Bank.vue') },
  { path: '/guide/problems', component: () => import('./views/ProblemGuide.vue') },
  { path: '/reports/problems', component: () => import('./views/ProblemReports.vue'), meta: { teacherOnly: true, adminToo: true } },
  { path: '/bank/new', component: () => import('./views/ProblemEdit.vue'), meta: { keyed: true } },
  { path: '/bank/:problemSlug/edit', component: () => import('./views/ProblemEdit.vue'), props: true, meta: { keyed: true } },
  { path: '/practice', component: () => import('./views/Practice.vue') },
  { path: '/playground', component: () => import('./views/Playground.vue') },
  { path: '/practice/:slug', component: () => import('./views/PracticeSolve.vue'), props: true, meta: { keyed: true } },
  { path: '/survey', component: () => import('./views/Survey.vue') },
  { path: '/survey/results', component: () => import('./views/SurveyResults.vue') },
  { path: '/journey/edit', component: () => import('./views/JourneyAdmin.vue') },
  { path: '/journey/:id?', component: () => import('./views/Journey.vue'), props: true },
  { path: '/leaderboard', component: () => import('./views/Leaderboard.vue') },
  { path: '/account/:section(profile|badges|editor|security|danger)?', component: () => import('./views/Account.vue') },
  { path: '/admin/:tab?', component: () => import('./views/Admin.vue') },
  { path: '/org-admin', component: () => import('./views/OrgAdmin.vue') },
  { path: '/org-admin/:slug/:tab?', component: () => import('./views/OrgAdmin.vue') },
  { path: '/lti/deep-link', component: () => import('./views/LtiDeepLink.vue') },
  { path: '/boards/:slug', component: () => import('./views/Board.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/stats', component: () => import('./views/BoardStats.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/submissions', component: () => import('./views/BoardSubmissions.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/plagiarism', component: () => import('./views/BoardPlagiarism.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/live', component: () => import('./views/LiveCode.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/problems', component: () => import('./views/BoardProblems.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/problems/new', component: () => import('./views/ProblemEdit.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/problems/:problemSlug/edit', component: () => import('./views/ProblemEdit.vue'), props: true, meta: { keyed: true } },
  { path: '/boards/:slug/problems/:problemSlug', component: () => import('./views/Solve.vue'), props: true, meta: { keyed: true } },
];

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
});

// After a deploy the hashed files an open tab still points at are gone: the lazy import of the next page fails and the click does
// nothing until a manual refresh. Reload onto the page it wanted instead (the new index.html knows the new files). The guard
// (once per 30 s) stops a reload loop when a file is really missing; with storage blocked we leave it to a manual refresh.
const STALE_CHUNK = /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed|Unable to preload/i;
function reloadOnce(href) {
  try {
    const last = Number(sessionStorage.getItem('beecoding.staleReload') || 0);
    if (Date.now() - last < 30_000) return;
    sessionStorage.setItem('beecoding.staleReload', String(Date.now()));
  } catch { return; }
  window.location.assign(href);
}
router.onError((err, to) => {
  if (STALE_CHUNK.test(String(err?.message || err))) reloadOnce(router.resolve(to.fullPath).href);
});

router.beforeEach(async (to) => {
  const auth = useAuth();
  if (!auth.ready) await auth.fetchMe();
  if (auth.user) await auth.loadConfig();
  // The server locks unverified accounts out of the app: park them on the verification screen.
  if (auth.mustVerify && to.path !== '/verify-email' && !to.meta.public) return { path: '/verify-email' };
  // ...and accounts the platform requires to use two-step verification stay on Account > Security until they do.
  if (auth.mustSetupMfa && !to.path.startsWith('/account/security') && !to.meta.public && !to.meta.anon) return { path: '/account/security' };
  if (to.meta.public) return true;                       // privacy / terms — anyone
  if (!to.meta.anon && !auth.user) return { path: '/login', query: { r: to.fullPath } };
  if (to.meta.anon && auth.user) return { path: '/boards' };
  if (to.meta.teacherOnly && !auth.isTeacher && !(to.meta.adminToo && auth.user?.isAdmin)) return { path: '/boards' };
  return true;
});

// A new page says so: the tab title changes (it was always the same) and focus moves to the content, which is how a screen
// reader hears that the page changed. Only a change of path counts, not a tab or query change inside the same page.
const TITLES = [
  [/^\/$/, 'Home'], [/^\/login/, 'Sign in'], [/^\/register/, 'Create account'], [/^\/verify-email/, 'Verify email'],
  [/^\/forgot-password/, 'Forgot password'], [/^\/reset-password/, 'Reset password'], [/^\/privacy/, 'Privacy policy'], [/^\/terms/, 'Terms'],
  [/^\/boards$/, 'Boards'], [/^\/boards\/[^/]+$/, 'Board'], [/^\/boards\/[^/]+\/stats/, 'Board stats'], [/^\/boards\/[^/]+\/submissions/, 'Submissions'],
  [/^\/boards\/[^/]+\/plagiarism/, 'Plagiarism'], [/^\/boards\/[^/]+\/live/, 'Live code'], [/^\/boards\/[^/]+\/problems$/, 'Problems'],
  [/^\/boards\/[^/]+\/problems\/(new|[^/]+\/edit)/, 'Edit problem'], [/^\/boards\/[^/]+\/problems\//, 'Problem'],
  [/^\/practice$/, 'Practice'], [/^\/practice\//, 'Problem'], [/^\/playground/, 'Playground'], [/^\/leaderboard/, 'Leaderboard'],
  [/^\/bank/, 'Problem bank'], [/^\/guide/, 'Problem guide'], [/^\/journey/, 'Journey'], [/^\/survey/, 'Survey'],
  [/^\/account/, 'Account'], [/^\/admin/, 'Admin'], [/^\/org-admin/, 'Organization'], [/^\/reports/, 'Problem reports'], [/^\/join\//, 'Join a board'],
];
router.afterEach((to, from) => {
  const name = TITLES.find(([re]) => re.test(to.path))?.[1];
  document.title = name ? `${tr(name)} · BeeCoding` : 'BeeCoding';
  if (from.path === to.path || !from.matched.length) return;   // first load: the browser starts at the top already
  setTimeout(() => document.getElementById('main')?.focus({ preventScroll: true }), 0);
});
