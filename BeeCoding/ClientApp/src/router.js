import { createRouter, createWebHistory } from 'vue-router';
import { useAuth } from './stores/auth';

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
  { path: '/join/:code', component: () => import('./views/JoinBoard.vue'), props: true },
  { path: '/bank', component: () => import('./views/Bank.vue'), meta: { teacherOnly: true } },
  { path: '/reports/problems', component: () => import('./views/ProblemReports.vue'), meta: { teacherOnly: true } },
  { path: '/bank/new', component: () => import('./views/ProblemEdit.vue'), meta: { teacherOnly: true } },
  { path: '/bank/:problemSlug/edit', component: () => import('./views/ProblemEdit.vue'), props: true, meta: { teacherOnly: true } },
  { path: '/practice', component: () => import('./views/Practice.vue') },
  { path: '/playground', component: () => import('./views/Playground.vue') },
  { path: '/practice/:slug', component: () => import('./views/PracticeSolve.vue'), props: true },
  { path: '/leaderboard', component: () => import('./views/Leaderboard.vue') },
  { path: '/account/:section(profile|security|danger)?', component: () => import('./views/Account.vue') },
  { path: '/admin/:tab?', component: () => import('./views/Admin.vue') },
  { path: '/org-admin', component: () => import('./views/OrgAdmin.vue') },
  { path: '/org-admin/:slug/:tab?', component: () => import('./views/OrgAdmin.vue') },
  { path: '/lti/deep-link', component: () => import('./views/LtiDeepLink.vue') },
  { path: '/boards/:slug', component: () => import('./views/Board.vue'), props: true },
  { path: '/boards/:slug/stats', component: () => import('./views/BoardStats.vue'), props: true },
  { path: '/boards/:slug/submissions', component: () => import('./views/BoardSubmissions.vue'), props: true },
  { path: '/boards/:slug/plagiarism', component: () => import('./views/BoardPlagiarism.vue'), props: true },
  { path: '/boards/:slug/live', component: () => import('./views/LiveCode.vue'), props: true },
  { path: '/boards/:slug/problems', component: () => import('./views/BoardProblems.vue'), props: true },
  { path: '/boards/:slug/problems/new', component: () => import('./views/ProblemEdit.vue'), props: true },
  { path: '/boards/:slug/problems/:problemSlug/edit', component: () => import('./views/ProblemEdit.vue'), props: true },
  { path: '/boards/:slug/problems/:problemSlug', component: () => import('./views/Solve.vue'), props: true },
];

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
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
  if (to.meta.teacherOnly && !auth.isTeacher) return { path: '/boards' };
  return true;
});
