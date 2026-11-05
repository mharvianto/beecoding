import { createRouter, createWebHistory } from 'vue-router';
import { useAuth } from './stores/auth';

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
  { path: '/leaderboard', component: () => import('./views/Leaderboard.vue') },
  { path: '/account/:section(profile|editor|security|danger)?', component: () => import('./views/Account.vue') },
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

// A deploy replaces the build, and the old build's hashed chunk files are gone. A tab opened before the deploy still runs the old
// code, so the first click on a page it has not loaded yet asks for a file that no longer exists ("Failed to fetch dynamically
// imported module") and the link seems dead until a manual refresh. Reload onto the page that was asked for: the new index.html
// brings the new build. Once per 15 s at most, so a build that is itself broken cannot loop.
const CHUNK_ERROR = /dynamically imported module|importing a module script failed|error loading dynamically imported module|Unable to preload CSS/i;
function reloadForNewBuild(target) {
  try {
    const last = Number(sessionStorage.getItem('beecoding.chunkReload')) || 0;
    if (Date.now() - last < 15_000) return false;
    sessionStorage.setItem('beecoding.chunkReload', String(Date.now()));
  } catch { /* storage blocked: reload anyway, the page is useless as it is */ }
  window.location.assign(target ?? window.location.href);
  return true;
}
// Vite reports a failed chunk on its own event first (and the router error never fires if that is handled), so keep track of where the
// current navigation is going: that is the page to reload onto.
let navigatingTo = null;
router.beforeEach((to) => { navigatingTo = to.fullPath; });
router.afterEach(() => { navigatingTo = null; });
const hrefOf = (fullPath) => (fullPath ? router.resolve(fullPath).href : undefined);
router.onError((err, to) => {
  if (CHUNK_ERROR.test(String(err?.message ?? err))) reloadForNewBuild(hrefOf(to?.fullPath ?? navigatingTo));
});
// Chunks a page loads on its own (the editor's KaTeX, Monaco workers, ...) fail the same way.
window.addEventListener('vite:preloadError', (e) => { if (reloadForNewBuild(hrefOf(navigatingTo))) e.preventDefault(); });

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
