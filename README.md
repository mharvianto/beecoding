# BeeCoding

A Padlet-style board where a teacher posts C/C++ problems, students solve them in the
browser (Monaco), run/submit code against a sandboxed judge, and a **live board** shows
everyone's progress — with layered controls over who can see whose answers.

## Stack

| Layer      | Choice |
|------------|--------|
| API        | ASP.NET Core 10 (controllers) |
| DB         | SQLite + EF Core |
| Realtime   | SignalR (`/hubs/board`) |
| Auth       | cookie auth (self-register, PBKDF2) |
| Frontend   | Vue 3 + Vite + Pinia + Tailwind v4 + Monaco, built into `wwwroot/` |
| Judge      | native `gcc`/`g++` + a small `setrlimit` runner, optional bubblewrap |
| IntelliSense | optional `clangd` over LSP/WebSocket (`/lsp/cpp`), off by default |

## Features

1. **Teacher posts problems** — CRUD problems + test cases per board (`ProblemsController`).
2. **Live board** — two views, toggle persisted per browser:
   - **Wall** (Padlet-style): one tab per problem, a card per student's post — avatar,
     verdict sticker, code preview, a note the author can write, emoji reactions, and a
     comment thread. `PadletWall.vue` + `WallController` / `WallService`; realtime via
     `wallChanged`.
   - **Grid**: compact students × problems table (`ProgressGrid.vue`).
   Both refetch the visibility-filtered `/api/boards/{id}/wall` or `/progress` on SignalR
   events; hidden posts show as a locked card / neutral dot.
   - **Live drafts**: the Solve editor streams the student's buffer over the hub
     (`PushDraft`, ~900 ms debounce) — a teacher (and peers, if allowed) watch
     work-in-progress on the wall (amber "✎ editing / LIVE" card) without the student
     running or submitting. Buffers are in-memory (`DraftStore`). Peer visibility of the
     live code obeys the same rules as the submitted card (`WallService.PeerCanSee`):
     hidden by exam mode, `BoardMembership.HiddenByTeacher`, or the student's own
     `Post.HiddenByStudent` toggle. Staff always see it.
3. **Run code** — `POST /api/run`, default limits **1 s CPU / 32 MB** (per-problem limits on
   submit). Enforced with `RLIMIT_CPU`, `RLIMIT_AS`, `RLIMIT_STACK`, `RLIMIT_NPROC`,
   `RLIMIT_FSIZE` + a wall-clock backstop. Verdicts: AC / WA / TLE / MLE / RE / CE.
   The student picks **C or C++** in the editor (a `C / C++` toggle, remembered per browser);
   Run and Submit both carry that choice — the judge compiles with `gcc` or `g++` accordingly
   (`Submission.Language` / `BankSubmission.Language`, null = the problem's authored language).
4. **Student hides own answer** — `PATCH /api/submissions/{id} { hiddenByStudent }`.
5. **Teacher hides answers from peers** — per student
   (`PATCH /api/boards/{id}/members/{userId} { hiddenByTeacher }`) **and** board-wide exam
   mode (`PATCH /api/boards/{id} { examMode }`).

5b. **Lecturing mode** (`PATCH /api/boards/{slug} { lecturingMode }`) — for live-coding
   demos. The teacher's editor buffer streams read-only to students on the same problem
   (`BoardHub.PushLecture` → `lectureUpdated`, `LectureStore`), with a "Copy into my editor"
   button; the teacher gets an expandable list of every student's live code for that
   problem. Students keep their own editor, can run/submit, and can still ask the AI tutor
   to explain an error or the code.

All five visibility rules live in one place: `Services/VisibilityService.cs`.

5c. **Download results and answers** (staff only, board page → *Download*) —
   `GET /api/boards/{slug}/export/results.xlsx` (one row per student with verdict, score and attempts per
   problem, plus a sheet with every attempt) and `GET /api/boards/{slug}/export/answers.zip?pick=best|latest`
   (source code, one folder per problem and one file per student). Both take `problem=<slug>` or `group=<id>`
   (`0` = problems without a group) to cover a single problem or group; the ⬇ menus on the Problems page do this.
   Verdict and score come from the student's best attempt (highest score, then newest). Each download is
   written to the audit log (`BoardExportController`).

6. **Problem bank** (`/bank`, teachers) — a reusable library of problems, private by default
   with a per-problem "share" toggle. Other teachers browsing a shared problem see only its
   **sample** tests; copying it onto their board carries the hidden tests server-side, so
   secret test data is never sent to a non-owner. Adding to a board **copies** (the board's
   problem is independent afterwards; `Problem.SourceBankProblemId` records provenance), and
   an existing board problem can be saved back into the bank.
   `BankController`: `GET/POST /api/bank`, `GET/PUT/DELETE /api/bank/{id}`,
   `POST /api/bank/{id}/copy-to/{slug}`, `POST /api/boards/{slug}/problems/{id}/to-bank`.
   For scripted bulk-loading there is also a token-authed `AdminController`
   (`GET/POST/DELETE /api/admin/bank-problems`, header `X-Admin-Token`): batch-upsert public
   bank problems by `(owner, title)`. Disabled (404) unless `Admin:Token` is configured.
   See INSTALL.md §5.
   With AI enabled, a teacher can also **generate a problem from an idea**
   (`POST /api/ai/generate-problem`): the model drafts the statement + a reference solution,
   the judge compiles and runs that solution against the model's inputs, and the *actual*
   program output is stored as each test's expected output (so the test data is verified,
   not the model's word). Saved to the teacher's bank as a private draft to review/publish.

7. **Free practice + XP / levels** (`/practice`, `/leaderboard`) — any signed-in user can
   browse and solve **every public bank problem** independent of a board
   (`PracticeController`, `BankSubmission`, judged by `BankSubmissionJob`). The first full
   solve of a problem awards XP by difficulty (Easy 10 / Medium 20 / Hard 40), tracked in
   `SolveRecord` with a unique `(user, problemKey)` — a bank problem and its board copies
   share one key, so the same problem can't be farmed. XP rolls up to a level
   (`ProgressService`, `25·L·(L-1)` cumulative); header shows `Lv N` + bar, and a
   leaderboard ranks by total XP. Practice statements are ALWAYS content-protected (encrypted watermarked image, `/api/practice/{id}/statement`). `GET /api/me/progress`, `GET /api/leaderboard`.

8. **C/C++ IntelliSense** (optional, off by default) — with `Lsp:Enabled=true` and `clangd`
   on `PATH`, the Monaco editor on **Solve** and **Practice** talks to clangd as a language
   server over a WebSocket (`/lsp/cpp`, `LspEndpoint` ⇄ `ClangdSession`): autocomplete,
   hover docs, signature help, and inline diagnostics. Each open editor gets its own
   short-lived clangd process on a throwaway single-file workspace, reaped on close or after
   `Lsp:IdleTimeoutSeconds`; concurrency capped by `Lsp:MaxConcurrent`. A thin hand-rolled
   LSP client (`lib/cpplsp.js`) feeds Monaco providers directly. Without clangd the editor
   still works with word-based completion.

9. **AI tutor** (optional, off by default) — with `Ai:Enabled=true` and `Ai:ApiKey` set, a
   **🤖 AI tutor** panel appears on Solve/Practice. `POST /api/ai/hint` sends the statement,
   samples, the student's code and any error text to an OpenAI-compatible chat-completions
   endpoint (`Ai:BaseUrl`/`Ai:Model`, defaults to NVIDIA NIM `deepseek-v4-flash`) behind a
   system prompt that forbids handing over a solution — it points at the bug, suggests an
   approach, and asks a guiding question. Replies with a code fence over ~12 lines are
   trimmed server-side. The reply **streams token-by-token over SSE**
   (`POST /api/ai/hint/stream`), and the student picks the **reply language** (Indonesian /
   English, remembered per browser). Per-user throttle (`Ai:RateLimitSeconds`); a
   slow/failed upstream surfaces as a friendly error (`502` on the one-shot endpoint, an
   SSE `error` frame on the stream). `AiController` + `AiTutorService`.

## Running (dev)

```bash
# terminal 1 — API on http://localhost:5080
cd BeeCoding
dotnet run

# terminal 2 — Vite dev server on http://localhost:5173 (proxies /api and /hubs)
cd BeeCoding/ClientApp
npm install
npm run build     # first time: also populates ../wwwroot for the fallback route
npm run dev
```

Open http://localhost:5173. Demo teacher: `teacher@demo.test` / `password`, board join
code `DEMO01` (an "A + B" problem is seeded).

## Running (single process)

```bash
cd BeeCoding
dotnet publish -c Release -o out     # runs `npm ci && npm run build` into wwwroot
./out/BeeCoding                       # serves SPA + API on one port
```

## Build prerequisites on the host

`gcc` and `g++` must be on `PATH`. `bubblewrap` (`bwrap`) is used automatically **if the
environment allows unprivileged user namespaces**; otherwise the judge falls back to
rlimits only and logs which mode it picked at startup.

## Content protection (per-board `ProtectContent` toggle)

When a teacher turns it on, a student opening a problem gets:

- The statement + samples **rendered to a PNG server-side** (`StatementImageService`,
  SkiaSharp + Markdig, DejaVu fonts vendored in `Assets/fonts/`) with a per-viewer
  identity watermark **baked into the pixels** — there is no text in the DOM to select,
  copy, or read via inspect-element.
- That PNG returned **AES-256-GCM encrypted** (`GET /api/problems/{id}/statement`, fresh
  key+nonce per request, `Cache-Control: no-store`); the client decrypts via WebCrypto to
  a `blob:` URL. No stable image URL, nothing reusable in a HAR/cache.
- `ContentGuard.vue` on top: blocks selection / copy / context-menu / drag, blurs the
  content while the tab is hidden or unfocused, and disables printing.

**This cannot stop a second camera** pointed at the screen — nothing can. The watermark is
what makes such a leak attributable. Everything else raises the effort for casual copying.

## Security note

The judge is **classroom-grade**, not hardened multi-tenant isolation:

- Time and memory limits are always enforced (`setrlimit` in `Services/Judge/RunnerSource.cs`).
- Filesystem isolation requires `bwrap`; this dev container blocks user namespaces, so runs
  currently share the host filesystem view (read-only for most paths, per the app user's
  permissions) with no network namespace.
- To harden: run the API as a dedicated low-privilege user, and/or rebuild the dev container
  with `"runArgs"` that permit user namespaces so `bwrap` engages, or move code execution to
  a disposable VM/container. A `seccomp` syscall filter in the runner is a further step.

## Layout

```
BeeCoding/
  Controllers/        Auth, Boards, Members, Problems, Submissions, Run
  Services/
    VisibilityService.cs      who sees whose answers
    BoardService.cs           join codes + progress-grid builder
    Judge/                    queue, BackgroundService worker, compiler, sandbox, verdicts
  Hubs/BoardHub.cs            groups: board-{id}, board-{id}-staff, user-{id}
  Data/                       AppDbContext, migrations, seeder
  ClientApp/                  Vue SPA (build output -> ../wwwroot)
```
