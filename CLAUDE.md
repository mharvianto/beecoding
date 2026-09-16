# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

BeeCoding: a Padlet-style board + C/C++ online judge. A teacher posts problems to a board,
students solve them in-browser (Monaco), run/submit code against a sandboxed native judge,
and a live board (Wall or Grid view) shows everyone's progress with layered
answer-visibility controls. Also has a standalone practice/XP/leaderboard mode, a private
problem bank, multi-tenant organizations with LTI 1.3 (LMS) integration, and optional
AI-tutor + clangd IntelliSense add-ons.

| Layer | Choice |
|---|---|
| API | ASP.NET Core 10 (controllers), net10.0 |
| DB | SQLite + EF Core, auto-migrate + auto-seed on startup |
| Realtime | SignalR (`/hubs/board`) |
| Auth | cookie auth, self-register, PBKDF2 |
| Frontend | Vue 3 + Vite + Pinia + Tailwind v4 + Monaco, built into `BeeCoding/wwwroot/` |
| Judge | native `gcc`/`g++` + `setrlimit`, optional bubblewrap; in-process or split out over Redis |

## Common commands

### Dev (two terminals — this is how you'll normally run/test changes)

```bash
# terminal 1 — backend on http://localhost:5048 (see BeeCoding/Properties/launchSettings.json)
cd BeeCoding
dotnet run

# terminal 2 — Vite dev server on http://localhost:5173 (proxies /api, /hubs, /lsp to :5048)
cd BeeCoding/ClientApp
npm install
npm run build     # first time only: also populates ../wwwroot for the fallback route
npm run dev
```

Demo login after first run: `teacher@demo.test` / `password`, board join code `DEMO01`.

Backend-only compile check (fast, does not build the SPA): `dotnet build BeeCoding.csproj`
from `BeeCoding/`, or `dotnet build BeeCoding.slnx` from the repo root for all three
projects. `npm run build` in `BeeCoding/ClientApp/` is the frontend equivalent.

### Single-process build (what CI/production runs)

```bash
cd BeeCoding
dotnet publish -c Release -o out     # runs `npm ci && npm run build` into wwwroot first
./out/BeeCoding                      # serves SPA + API on one port
```

Skip the npm step (e.g. when you've already built the SPA, or don't need it) with
`dotnet publish -c Release -o out -p:BuildClient=false`. Plain `dotnet build`/`dotnet run`
never touch npm — the Vite dev server is expected instead.

### Database migrations

The `DbContext` (`AppDbContext`) and all migrations live in `BeeCoding.Core`, but the
startup/config project is `BeeCoding` — `dotnet ef` needs both pointed out explicitly, run
from `BeeCoding/`:

```bash
dotnet tool install --global dotnet-ef   # once
dotnet ef migrations add SomeName --project ../BeeCoding.Core --startup-project .
dotnet ef database update --project ../BeeCoding.Core --startup-project .   # optional — the app auto-migrates on startup anyway
```

Reset local dev data: stop the app, delete `BeeCoding/beecoding.db*`, restart (reseeds).

### No test suite, no linter

There is no `.Tests` project (`BeeCoding.slnx` only has the three app projects) and no
frontend test runner or ESLint/Prettier config. Verify changes by building
(`dotnet build`, `npm run build`) and exercising the feature — via the running dev server
(curl / a Playwright script) rather than assuming a `dotnet test` or `npm test` command
exists.

### Judge prerequisites

`gcc`/`g++` must be on `PATH`. `bubblewrap` (`bwrap`) engages automatically if the
environment allows unprivileged user namespaces; otherwise the judge logs a warning at
startup and falls back to rlimits-only (this is the case in the default dev container).

## Architecture

### Three projects, one judge core

- **`BeeCoding.Core`** — shared library: EF Core `AppDbContext` + migrations, domain models
  (`Models/Entities.cs`, `Models/Dtos.cs`), and the judge engine itself (`Judge/`:
  `NativeCompiler`, `NativeSandbox`, `NativeToolchain`, `VerdictEvaluator`,
  `JudgeQueue`/`RedisJudgeQueue`). No web/SignalR dependencies.
- **`BeeCoding`** — the web app: controllers, SignalR hub, the Vue SPA (`ClientApp/` →
  `wwwroot/`), and everything that touches the database, auth, or notifies clients
  (`GradeResultConsumer` persists verdicts and pushes SignalR events after the judge engine
  produces a result).
- **`BeeCoding.Judge`** — an optional standalone worker (see its `Program.cs`) for scaling
  the judge out separately from the web tier: it holds no DB/SignalR/auth, only pulls jobs
  from a Redis broker, compiles/runs untrusted code, and publishes the verdict back. Only
  runs when `Judge:Queue:Backend=redis`; unused in the default single-process setup, where
  `BeeCoding` runs the judge in-process via an in-memory `Channel`. See DEPLOY.md §2.6 and
  CONTAINERS.md §4 for splitting it into its own container image.

`Judge:Queue:UseRedis` / `Realtime:UseRedis` (`Program.cs`) independently toggle each
subsystem between in-process and Redis-backed — read `Program.cs`'s DI wiring before
assuming either is on.

### Visibility rules — one source of truth

All answer-visibility logic (exam mode, per-student teacher-hide, per-submission
student-hide, staff-always-sees-everything) is centralized in
`Services/VisibilityService.cs` and consumed by both the REST endpoints
(`BoardService.BuildProgressAsync`, `WallService.BuildWallAsync`) and the SignalR payload
builders — don't duplicate a visibility check elsewhere. A **hidden problem**
(`Problem.Hidden`) is a separate, simpler mechanism: it's filtered out of the
student-facing list/wall/progress-grid and 404s on direct access/submit for non-staff,
independent of the peer-visibility rules above.

### Soft delete via EF global query filters

`Board`, `Problem`, and `BankProblem` have `HasQueryFilter(x => x.DeletedAt == null)`
(`BeeCoding.Core/Data/AppDbContext.cs`). Normal queries silently exclude deleted rows; a
restore/admin-trash path that needs to see or act on a deleted row must call
`.IgnoreQueryFilters()` explicitly (see `ProblemsController.Restore` for the pattern). EF
logs benign warnings at migration/build time about these filters interacting with
required relationships — expected, not a bug to fix.

### Multi-tenant organizations + LTI

`Organization` / `OrganizationMembership` scope boards, AI usage (proxied via membership,
since `AiUsage` itself isn't per-org), and dashboards (`OrgAdminController`). A platform
super admin (`AdminAccess.IsAdminEmail`, config `Admin:Emails`) can manage every
organization without an explicit membership row. LTI 1.3 (`Services/Lti/`,
`LtiController`) lets an LMS launch into a board/practice problem and sync grades back;
see INSTALL.md §5A.

### Progress / XP / streaks

`ProgressService` computes level from lifetime XP (`User.Xp`, awarded once per distinct
problem via `SolveRecord`, `25·L·(L-1)` cumulative per level) and the daily practice streak
(`User.CurrentStreak`/`StreakLocalDay` — bumped only by practice/bank solves, keyed by the
**client's locally-reported calendar day**, not server UTC, since a streak day means the
student's day). `BankSubmission.LocalDay` / `Submission.LocalDay` carry that client day
for later "solved today" aggregation (`ProgressService.CountSolvedOnLocalDayAsync`, board +
practice combined) — always pass the browser's local date, not `toISOString()` (UTC), when
calling anything that takes a `localDay` param (see `lib/localDay.js`).

### Frontend conventions

- Charts with an hourly/daily/weekly granularity toggle persist the choice in
  `localStorage` per view (`beecoding.<view>.engagementGranularity`/`aiGranularity`) —
  follow this pattern for any new time-series chart rather than a bare `ref`.
- List/table row secondary actions (edit, hide, delete, restore, ...) use the shared
  `.row-action-btn` CSS class (+ `--accent`/`--success`/`--danger` modifiers, defined in
  `src/style.css`) for a consistent icon+label ghost-button look; the one primary action
  per row (e.g. "View"/"Solve") stays a filled button. There is no shared Vue component for
  this yet, just the CSS class.
- Per-viewer local state (theme, editor prefs, board view, autosave drafts, celebration
  markers) lives in plain `localStorage` reads/writes wrapped in `try/catch`, not an
  abstraction layer — see `lib/theme.js`, `lib/draft.js`, `lib/celebration.js` for the
  idiom. Draft autosaves (`lib/draft.js`) self-expire (1 day after a solved problem, 1 week
  otherwise), pruned once per app load, not per save.
- Auth/session itself is an `HttpOnly` cookie, never `localStorage` — don't change that.

### Config surface (`appsettings.json`, all overridable via `Section__Key` env vars)

`Judge` (queue backend, concurrency, limits, sandbox), `Lsp` (clangd bridge, off by
default), `Ai` (AI tutor, off by default — needs `Ai:Enabled=true` + `Ai:ApiKey`),
`Admin:Emails`/`Admin:Token` (platform admin access + scripted bank-ingest auth),
`Auth:TeacherSignupCode`, `Realtime` (Redis backend for presence/drafts/lecture-mode
stores). `BeeCoding/appsettings.json` and `beecoding.db` are local dev state — do not
assume changes there should be committed.

## Further docs

- `README.md` — feature tour with the exact endpoints behind each one.
- `INSTALL.md` (Bahasa Indonesia) — single-VM install/deploy, nginx+SSL, systemd, LTI setup,
  multi-tenant setup, migrations, troubleshooting table.
- `CONTAINERS.md` (Bahasa Indonesia) — Podman / Docker Swarm packaging, including splitting
  `BeeCoding.Judge` into its own image.
- `DEPLOY.md` (Bahasa Indonesia) — Azure Web App / Kubernetes / scale-out architecture.
