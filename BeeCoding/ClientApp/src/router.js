import { createRouter, createWebHistory } from 'vue-router';
import { useAuth } from './stores/auth';

const routes = [
  { path: '/', component: () => import('./views/Landing.vue'), meta: { anon: true } },
  { path: '/login', component: () => import('./views/Login.vue'), meta: { anon: true } },
  { path: '/register', component: () => import('./views/Register.vue'), meta: { anon: true } },
  { path: '/privacy', component: () => import('./views/Privacy.vue'), meta: { public: true } },
  { path: '/terms', component: () => import('./views/Terms.vue'), meta: { public: true } },
  { path: '/dashboard', component: () => import('./views/MyDashboard.vue') },
  { path: '/boards', component: () => import('./views/Dashboard.vue') },
  { path: '/bank', component: () => import('./views/Bank.vue') },
  { path: '/bank/new', component: () => import('./views/ProblemEdit.vue') },
  { path: '/bank/:problemSlug/edit', component: () => import('./views/ProblemEdit.vue'), props: true },
  { path: '/practice', component: () => import('./views/Practice.vue') },
  { path: '/playground', component: () => import('./views/Playground.vue') },
  { path: '/practice/:slug', component: () => import('./views/PracticeSolve.vue'), props: true },
  { path: '/leaderboard', component: () => import('./views/Leaderboard.vue') },
  { path: '/account', component: () => import('./views/Account.vue') },
  { path: '/admin/:tab?', component: () => import('./views/Admin.vue') },
  { path: '/org-admin', component: () => import('./views/OrgAdmin.vue') },
  { path: '/lti/deep-link', component: () => import('./views/LtiDeepLink.vue') },
  { path: '/boards/:slug', component: () => import('./views/Board.vue'), props: true },
  { path: '/boards/:slug/live', component: () => import('./views/LiveCode.vue'), props: true },
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
  if (to.meta.public) return true;                       // privacy / terms — anyone
  if (!to.meta.anon && !auth.user) return { path: '/login', query: { r: to.fullPath } };
  if (to.meta.anon && auth.user) return { path: '/boards' };
  return true;
});
