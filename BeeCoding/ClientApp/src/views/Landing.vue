<script setup>
import ThemeToggle from '../components/ThemeToggle.vue';

const features = [
  ['🧑‍🏫', 'Teacher posts problems', 'Build problems with samples and hidden tests, or pull from a shared problem bank.', 'bg-amber-100 dark:bg-amber-500/15'],
  ['📡', 'Live classroom board', 'A Padlet-style wall and a grid update in real time as students work — with controls over who sees whose answers.', 'bg-sky-100 dark:bg-sky-500/15'],
  ['⚙️', 'Sandboxed C / C++ judge', 'Run and submit code against real tests with CPU, memory and wall-clock limits. Students pick C or C++.', 'bg-violet-100 dark:bg-violet-500/15'],
  ['🎯', 'Practice + XP', 'A public bank of problems to solve for XP and a leaderboard, independent of any class.', 'bg-emerald-100 dark:bg-emerald-500/15'],
  ['💡', 'C/C++ IntelliSense', 'Optional clangd-powered autocomplete, hover docs and inline diagnostics in the editor.', 'bg-rose-100 dark:bg-rose-500/15'],
  ['🤖', 'AI tutor (hints only)', 'Optional. Points at the bug and suggests an approach — it will not write the solution for you.', 'bg-teal-100 dark:bg-teal-500/15'],
];

const steps = [
  ['1', 'Post', 'A teacher creates a board and adds problems — written by hand, copied from the bank, or grouped into weekly sessions.'],
  ['2', 'Solve', 'Students join with a code or QR, open a problem and code in the browser. Run against samples, then submit for a verdict.'],
  ['3', 'Watch live', 'The wall and grid fill in as submissions are judged, so the whole class sees progress the moment it happens.'],
];

const teacher = [
  'Group problems into sessions with open and close times',
  'Exam mode per session: peers hidden, paste blocked',
  'Live coding: stream your editor to the class',
  'Plagiarism spot-checks and full submission history',
  'Bulk-create boards and add problems across many at once',
  'LTI 1.3 launch and grade sync for your LMS',
];
const student = [
  'Code, run and submit without installing anything',
  'Instant verdicts with per-test feedback',
  'XP, levels, streaks and a leaderboard',
  'A practice bank to keep going after class',
  'Get notified when someone reacts or comments on your card',
  'Optional AI hints that never hand over the answer',
];

// Decorative product mock in the hero (not real data).
const mockCards = [
  ['A', 'Ana', 'AC', 'emerald'],
  ['B', 'Budi', 'AC', 'emerald'],
  ['C', 'Citra', 'WA', 'rose'],
  ['D', 'Dimas', 'typing…', 'amber'],
];
const badge = {
  emerald: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300',
  rose: 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-300',
  amber: 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300',
};
const avatar = { emerald: 'bg-emerald-400', rose: 'bg-rose-400', amber: 'bg-amber-400' };

const faqs = [
  ['What is BeeCoding?',
    'A classroom platform for teaching C and C++. A teacher posts problems, students solve them in the browser and submit against an automated judge, and the class sees progress on a live board.'],
  ['Do students need to install anything?',
    'No. Everything runs in the browser — the editor, running code, and submitting. Only a modern browser is needed.'],
  ['Can I write C or C++?',
    'Both. You choose the language per problem with a C / C++ toggle; the judge compiles your submission with gcc or g++ accordingly. The starter template switches to match.'],
  ['How is my code graded?',
    'Your program is compiled and run against sample and hidden test cases under CPU, memory and wall-clock limits. You get a verdict (Accepted, Wrong Answer, Time/Memory Limit, Runtime or Compile Error) and a score equal to the fraction of test points passed.'],
  ['Does the AI tutor give me the answer?',
    'No. When enabled by the operator, it points at where a bug likely is and suggests an approach or algorithm — it is instructed never to hand over a working solution. It is optional and off by default.'],
  ['Who can see my submissions?',
    'Teachers of the classes you belong to can see your submissions; other students see only what the visibility settings allow, and hidden tests are never shown to students. See the Privacy Policy for details.'],
  ['How do I get a teacher account?',
    'Choose the Teacher role at registration and enter the invite code from your school or whoever runs this instance. Without a code, registration creates a student account.'],
  ['Is it free? Where is my data?',
    'BeeCoding is open-source software that your school or teacher self-hosts. There are no ads and your data is not sold. The operator of the instance controls where it is stored and is your contact for any data request.'],
];
</script>

<template>
  <div class="landing min-h-full flex flex-col bg-slate-50 dark:bg-slate-950 text-slate-800 dark:text-slate-100">
    <header class="sticky top-0 z-30 backdrop-blur bg-slate-50/80 dark:bg-slate-950/80 border-b border-transparent">
      <div class="max-w-6xl w-full mx-auto px-4 h-14 flex items-center justify-between">
        <span class="font-bold text-lg text-amber-600 dark:text-amber-400">🐝 BeeCoding</span>
        <div class="flex items-center gap-3 text-sm">
          <ThemeToggle />
          <RouterLink to="/login" class="text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100">Sign in</RouterLink>
          <RouterLink to="/register" class="bg-amber-500 hover:bg-amber-600 text-white rounded-lg px-3 py-1.5 font-medium">
            Get started
          </RouterLink>
        </div>
      </div>
    </header>

    <main class="flex-1">
      <!-- hero -->
      <section class="hero relative overflow-hidden">
        <div class="hero-glow pointer-events-none absolute inset-0" aria-hidden="true"></div>
        <div class="relative max-w-6xl mx-auto px-4 pt-14 pb-16 sm:pt-20 grid lg:grid-cols-2 gap-12 items-center">
          <div class="text-center lg:text-left rise">
            <span class="inline-flex items-center gap-2 text-xs font-medium rounded-full px-3 py-1 bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300">
              <span class="w-1.5 h-1.5 rounded-full bg-amber-500"></span> Live board · C &amp; C++ judge · runs in the browser
            </span>
            <h1 class="mt-5 text-4xl sm:text-5xl font-extrabold tracking-tight leading-[1.1]">
              Teach C/C++ with a
              <span class="text-transparent bg-clip-text bg-gradient-to-r from-amber-500 to-orange-500">live board</span>
              and an automatic judge
            </h1>
            <p class="mt-5 text-lg text-slate-500 dark:text-slate-400 max-w-xl mx-auto lg:mx-0">
              Teachers post problems, students solve them in the browser and submit against a
              sandboxed judge, and the whole class sees progress in real time.
            </p>
            <div class="mt-8 flex flex-wrap items-center justify-center lg:justify-start gap-3">
              <RouterLink to="/register"
                          class="bg-amber-500 hover:bg-amber-600 text-white rounded-xl px-6 py-3 font-semibold shadow-lg shadow-amber-500/25 transition hover:-translate-y-0.5">
                Create an account
              </RouterLink>
              <RouterLink to="/login"
                          class="border border-slate-300 dark:border-slate-700 rounded-xl px-6 py-3 font-semibold hover:border-amber-400 transition">
                Sign in
              </RouterLink>
            </div>
            <ul class="mt-8 flex flex-wrap justify-center lg:justify-start gap-x-5 gap-y-2 text-sm text-slate-500 dark:text-slate-400">
              <li>✓ No install for students</li>
              <li>✓ Sandboxed judge</li>
              <li>✓ Self-hosted, no ads</li>
            </ul>
          </div>

          <!-- decorative product mock -->
          <div class="relative mx-auto w-full max-w-md lg:max-w-none rise rise-2" aria-hidden="true">
            <div class="float rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-2xl shadow-slate-900/10 dark:shadow-black/40 overflow-hidden">
              <div class="flex items-center gap-1.5 px-4 py-2.5 border-b border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-900">
                <span class="w-2.5 h-2.5 rounded-full bg-rose-400"></span>
                <span class="w-2.5 h-2.5 rounded-full bg-amber-400"></span>
                <span class="w-2.5 h-2.5 rounded-full bg-emerald-400"></span>
                <span class="ml-3 text-xs text-slate-400">A + B · C++</span>
                <span class="ml-auto text-[11px] font-semibold rounded-full px-2 py-0.5 bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300">Accepted · 10/10</span>
              </div>
              <pre class="px-4 py-3 text-[13px] leading-relaxed font-mono text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-900 overflow-hidden"><span class="text-violet-600 dark:text-violet-400">#include</span> <span class="text-emerald-600 dark:text-emerald-400">&lt;iostream&gt;</span>
<span class="text-violet-600 dark:text-violet-400">int</span> <span class="text-sky-600 dark:text-sky-400">main</span>() {
  <span class="text-violet-600 dark:text-violet-400">long long</span> a, b;
  std::cin &gt;&gt; a &gt;&gt; b;
  std::cout &lt;&lt; a + b &lt;&lt; <span class="text-emerald-600 dark:text-emerald-400">"\n"</span>;
}</pre>
            </div>

            <div class="float-slow mt-4 sm:absolute sm:-bottom-10 sm:-right-4 sm:mt-0 sm:w-64 rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-xl shadow-slate-900/10 dark:shadow-black/40 p-3">
              <div class="flex items-center justify-between text-xs mb-2">
                <span class="font-semibold text-slate-600 dark:text-slate-300">Live board</span>
                <span class="text-emerald-600 dark:text-emerald-400">● 4 online</span>
              </div>
              <div class="grid grid-cols-2 gap-2">
                <div v-for="[i, name, verdict, tone] in mockCards" :key="name"
                     class="rounded-lg border border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/50 px-2 py-1.5">
                  <div class="flex items-center gap-1.5">
                    <span class="w-5 h-5 rounded-full text-white text-[10px] font-bold grid place-items-center" :class="avatar[tone]">{{ i }}</span>
                    <span class="text-xs font-medium truncate">{{ name }}</span>
                  </div>
                  <span class="mt-1 inline-block text-[10px] font-semibold rounded px-1.5 py-0.5" :class="badge[tone]">{{ verdict }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- how it works -->
      <section class="max-w-6xl mx-auto px-4 pt-6 sm:pt-12 pb-16">
        <h2 class="text-2xl sm:text-3xl font-bold text-center">From problem to progress in three steps</h2>
        <div class="mt-10 grid gap-6 md:grid-cols-3">
          <div v-for="[n, title, body] in steps" :key="n" class="relative rounded-2xl p-6 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800">
            <span class="w-9 h-9 rounded-full bg-amber-500 text-white font-bold grid place-items-center shadow-md shadow-amber-500/30">{{ n }}</span>
            <h3 class="mt-4 text-lg font-semibold">{{ title }}</h3>
            <p class="mt-1.5 text-sm text-slate-500 dark:text-slate-400 leading-relaxed">{{ body }}</p>
          </div>
        </div>
      </section>

      <!-- features -->
      <section class="max-w-6xl mx-auto px-4 pb-16">
        <h2 class="text-2xl sm:text-3xl font-bold text-center">Everything a programming class needs</h2>
        <div class="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <div v-for="[icon, title, body, tint] in features" :key="title"
               class="group rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-5 transition hover:-translate-y-1 hover:shadow-lg hover:border-amber-300 dark:hover:border-amber-500/40">
            <div class="w-11 h-11 rounded-xl grid place-items-center text-2xl" :class="tint">{{ icon }}</div>
            <h3 class="font-semibold mt-4">{{ title }}</h3>
            <p class="text-sm text-slate-500 dark:text-slate-400 mt-1.5 leading-relaxed">{{ body }}</p>
          </div>
        </div>
      </section>

      <!-- teachers / students -->
      <section class="max-w-6xl mx-auto px-4 pb-16 grid gap-6 md:grid-cols-2">
        <div class="rounded-2xl p-6 sm:p-8 bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-500/10 dark:to-orange-500/5 border border-amber-100 dark:border-amber-500/20">
          <h3 class="text-xl font-bold">For teachers</h3>
          <ul class="mt-4 space-y-2.5 text-sm">
            <li v-for="t in teacher" :key="t" class="flex gap-2.5"><span class="text-amber-500 shrink-0">✓</span><span>{{ t }}</span></li>
          </ul>
        </div>
        <div class="rounded-2xl p-6 sm:p-8 bg-gradient-to-br from-sky-50 to-violet-50 dark:from-sky-500/10 dark:to-violet-500/5 border border-sky-100 dark:border-sky-500/20">
          <h3 class="text-xl font-bold">For students</h3>
          <ul class="mt-4 space-y-2.5 text-sm">
            <li v-for="t in student" :key="t" class="flex gap-2.5"><span class="text-sky-500 shrink-0">✓</span><span>{{ t }}</span></li>
          </ul>
        </div>
      </section>

      <!-- faq -->
      <section class="max-w-3xl mx-auto px-4 pb-16">
        <h2 class="text-2xl sm:text-3xl font-bold text-center mb-8">Frequently asked questions</h2>
        <div class="divide-y divide-slate-200 dark:divide-slate-800 border-y border-slate-200 dark:border-slate-800">
          <details v-for="[q, a] in faqs" :key="q" class="group">
            <summary class="flex items-center justify-between gap-4 cursor-pointer list-none py-4 font-medium
                            [&::-webkit-details-marker]:hidden hover:text-amber-600 dark:hover:text-amber-400">
              <span>{{ q }}</span>
              <span class="shrink-0 text-slate-400 transition-transform group-open:rotate-45">+</span>
            </summary>
            <p class="pb-4 -mt-1 text-sm text-slate-500 dark:text-slate-400 leading-relaxed">{{ a }}</p>
          </details>
        </div>
      </section>

      <!-- final call to action -->
      <section class="max-w-6xl mx-auto px-4 pb-20">
        <div class="rounded-3xl px-6 py-12 sm:py-14 text-center bg-gradient-to-br from-amber-500 to-orange-500 text-white shadow-xl shadow-amber-500/20">
          <h2 class="text-2xl sm:text-3xl font-extrabold">Ready to run your next class on BeeCoding?</h2>
          <p class="mt-2 text-amber-50/90">Create an account and open your first board in a minute.</p>
          <div class="mt-7 flex flex-wrap justify-center gap-3">
            <RouterLink to="/register" class="bg-white text-amber-700 hover:bg-amber-50 rounded-xl px-6 py-3 font-semibold transition hover:-translate-y-0.5">
              Get started
            </RouterLink>
            <RouterLink to="/login" class="border border-white/60 hover:bg-white/10 rounded-xl px-6 py-3 font-semibold transition">
              Sign in
            </RouterLink>
          </div>
        </div>
      </section>
    </main>
  </div>
</template>

<style scoped>
/* soft honey-coloured glow + a faint dot grid behind the hero */
.hero-glow {
  background:
    radial-gradient(60rem 28rem at 15% -10%, rgba(245, 158, 11, 0.20), transparent 60%),
    radial-gradient(40rem 24rem at 100% 10%, rgba(249, 115, 22, 0.12), transparent 60%);
}
.hero::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image: radial-gradient(rgba(100, 116, 139, 0.18) 1px, transparent 1px);
  background-size: 24px 24px;
  -webkit-mask-image: linear-gradient(to bottom, black, transparent 85%);
  mask-image: linear-gradient(to bottom, black, transparent 85%);
  pointer-events: none;
}

@keyframes rise { from { opacity: 0; transform: translateY(14px); } to { opacity: 1; transform: none; } }
@keyframes float { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(-8px); } }
.rise { animation: rise 0.6s ease-out both; }
.rise-2 { animation-delay: 0.15s; }
.float { animation: float 7s ease-in-out infinite; }
.float-slow { animation: float 9s ease-in-out infinite 1s; }

@media (prefers-reduced-motion: reduce) {
  .rise, .float, .float-slow { animation: none; }
}
</style>
