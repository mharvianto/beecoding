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

**Two providers, two sets of migrations.** SQLite (production today) uses `BeeCoding.Core/Migrations`; PostgreSQL (`Database:Provider=postgres`) uses `BeeCoding.Core.Postgres/Migrations`
(`Program.cs` names that assembly, and `MigrateAsync` runs for both). A model change needs one of each: `scripts/ef-add.sh SomeName` makes both (no server needed). `HardeningTests.The_migrations_of_the_provider_in_use_match_the_model`
fails on the provider that was forgotten. The whole suite runs on Postgres when `BEECODING_TEST_PG` is set to a server connection string without a database (`Host=127.0.0.1;Port=5433;Username=postgres`); each test then
gets its own database, dropped afterwards. CI (`deploy.yml`) runs it both ways with a `postgres:16` service container; locally any Postgres works (this dev container has none: `pip install pgserver` brings a binary).
The initial Postgres migration was made from the model as of 22 Nov 2026; moving existing SQLite data over is `dotnet BeeCoding.dll move-data <file.db> "<Npgsql conn>"` (`Services/DataMove/SqliteToPostgres.cs`, runbook in DEPLOY.md §3b; copies raw values, so the SAME Data Protection keys are needed; tested by `MoveDataTests` when `BEECODING_TEST_PG` is set).

Reset local dev data: stop the app, delete `BeeCoding/beecoding.db*`, restart (reseeds).

### Small test suite, no linter

`BeeCoding.Tests` (xunit, `dotnet test BeeCoding.Tests`) covers only the risky rules: `VisibilityService`,
`GroupAccess`, `AiUrl` and the CSRF middleware (via `WebApplicationFactory<Program>` on a temp SQLite file).
Add a test there when you touch one of those. The frontend has a small Vitest suite (`npm test` in `BeeCoding/ClientApp`, `tests/*.test.js`, jsdom) for the plain-JS logic in `src/lib` — drafts, `tr()`, `v-modal`, and that `SYNCED_KEYS` equals the server's `PreferencesController` list; CI runs it. Four browser flows live in `ClientApp/e2e/` (`npm run test:e2e`: Playwright starts the app on port 5058 with a throw-away SQLite file, needs `npm run build` first and `npx playwright install chromium` once; CI runs it too). No component tests, no ESLint/Prettier config, so
verify everything else by building (`dotnet build`, `npm run build`) and exercising the feature — via the
running dev server (curl / a Playwright script).

### Judge prerequisites

`gcc`/`g++` must be on `PATH`. The compiler itself runs behind `prlimit` (util-linux, `/usr/bin/prlimit`: 2 GB address space, 20 s CPU, 64 MB output file) and its messages are cut at 20 000 characters — a self-doubling macro in a few hundred bytes otherwise makes `cc1plus` eat gigabytes; without `prlimit` the judge logs a warning and compiles unlimited. `bubblewrap` (`bwrap`) engages automatically if the
environment allows unprivileged user namespaces. `Judge:RequireSandbox` is **true by default** (code and `appsettings.json`): without a usable
bwrap the app refuses to start, because rlimits-only mode lets judged code read the database and the Data Protection keys. The default dev
container has no user namespaces, so `appsettings.Development.json` sets it to false there (the judge then logs a warning and runs rlimits-only).

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

### Board problems live in the bank

Every board `Problem` has a bank row of record, `Problem.BankProblemId`: the source for one copied in from the bank, otherwise a private
`BankProblem.BoardOnly` row (created with the problem; `Program.cs` backfills old ones at startup, safe to repeat). BoardOnly rows are out of the
bank list, admin bank list/count and everything public; "Save to bank" just clears the flag. Board edits are written through to a BoardOnly row only.
`SourceBankProblemId` stays the XP key source (`ProgressService`), deliberately separate: setting it on old problems would re-award XP.
Once a problem has any `Submission`, `ProblemsController.Update` refuses (409) changes to graded content (statement, tests, limits, languages,
banned lists, input file: `BoardBulkService.GradedContent`); title/tags/level/position stay editable. `POST .../new-version` copies it as a hidden draft
with its own bank row (the editor offers it on that 409). A copy that fell behind its bank problem (graded content compared, `BoardBulkService.GradedContent`, visible copies only) is listed by `GET .../problems/bank-updates` and shown as
"Update from bank" on the board's problem list (owner only): `POST .../{slug}/update-from-bank` replaces it in place, or, once students have submitted, adds the bank version as a hidden draft.
Not built: moving the content columns out of `Problem` (the bell/push does not announce a bank change either).

### Sharing a private bank problem with teachers

`BankProblemShare` (owner → one teacher, by email, `BankController.AddShare`, panel `BankShare.vue` on the bank problem's edit page, max 50): the teacher reads it
(`Readable()`, tab "Shared with me", hidden tests are still shown only as samples to a non-owner) and can copy it to their own boards. Read-only: every write path
checks `OwnerId`. Students cannot receive one, and there is no bell/email on sharing. Stopping a share removes access; a board copy already made stays, but
`bank-updates` / `update-from-bank` only follow a source the teacher can still read (`ProblemsController.CanReadBank`). Copying copies all tests, as for a public problem.

### Problem groups (folders/sessions)

`ProblemGroup` is a one-level folder on a board, carrying its own `Hidden`, `ExamMode` and
`OpensAt`/`ClosesAt` window. `Services/GroupAccess.cs` is the source of truth for what that
means: student-visible (not hidden, group not hidden, already open), closed (readable but
submit refused) and *effective exam mode* (the group's flag, or `Board.ExamMode` for
ungrouped problems). Exam mode is therefore per problem, not per board —
`VisibilityService` takes an `examMode` bool rather than a `Board`. Always `Include(Group)`
where you call `GroupAccess`; an unloaded group on a grouped problem fails closed.

### Problem feedback (likes + reports)

Anyone who can open a problem can like it and report it as wrong (input that doesn't match the statement or
breaks its constraints, wrong expected output, unclear text) — `ProblemFeedbackController`, UI in
`ProblemFeedback.vue` on both Solve pages. `ProblemLike`/`ProblemReport` target either a bank problem
(`BankProblemId`, practice) or a board problem (`ProblemId`), never both. Reports are reviewed on
`/reports/problems` by the problem's owner (board staff for a board problem) and by platform admins
(`Reviewable()` is the single place that scopes who sees what); the user menu carries the open count. The bell
(`NotificationService`) rings the bank problem's owner / the board's staff on a new report (`report`) and the
reporter when it is resolved or dismissed (`report-update`); those rows have no board/post (`ReportId` set,
`Link` says where they lead).

### Notification lifetime

`NotificationJanitor` (every 6 h): unread ones live `Notifications:RetentionDays` (30), read ones `PlatformRuntimeConfig.NotificationReadRetentionDays`
after being read (default 7, Admin → System, 0 = off), and each user keeps only the newest 200. Reactions and comments on one card pile into
a single unread row (`Count`). The bell pages 30 at a time (`GET /api/notifications?skip=`); a user may delete only notifications they already read.

### Notification kinds + email

`NotificationKinds` lists the kinds (reaction, comment, report, report-update, proposal, proposal-update, practice-review, review-submitted). Every creator in `NotificationService` asks `ChannelsAsync(user, kind)` first: a kind the platform switched off (Admin → System → "Notification kinds", `PlatformRuntimeSettings.NotifyOffKinds`, cached in `PlatformRuntimeConfig`) or the user muted (Account → "What to notify me about", `User.NotifyOffKinds`) creates no row, no push and no email. Email is opt-in per user (`User.NotifyEmailKinds`, only for kinds that stay in the bell), allowed per kind by the platform (`NotifyEmailKinds`, null = all but reaction/comment), and sent only when `Email` (SMTP) and `App:PublicUrl` are set and the address is verified. An admin can also lock a kind (`NotifyLockedKinds`): the user's mute is then ignored (switching it off for everyone still wins). More kinds are board-less rows (`ProposalId = 0`, `Link` says where they lead): `org-invite` (`OrgAdminController` when an invite, not a direct add, is made), `group-opens`/`group-closing` (`GroupScheduleNotifier`, every 2 min, claims a group with a conditional UPDATE on `ProblemGroup.OpenNotifiedAt`/`CloseNotifiedAt`; editing a date clears the marker), `problem-new` (`ProblemsController` create / unhide, only for what a student can open now, one unread pile per board), `lti-sync-failed` (`LtiGradeSyncService`, board staff, one pile per board; practice-problem sync failures are only logged) and `security` (password change/reset, two-step verification turned off: emailed without the per-user opt-in, because the point is to alert). Reactions/comments email once per unread pile (a bump does not mail again). Sent in the background; a failure is only logged.

### What survives an account (`AccountErasure`)

Deleting an account (self: `AuthController.DeleteAccount`; admin: Trash purge, bulk purge, or `DeletedUserJanitor`) hard-deletes the user row, which cascades to
their submissions, solve records, private drafts and every `BankProblem` they own. Unless told otherwise (`DeleteAccountDto.KeepPublicProblems`, default true;
`?keepProblems=` on the purge endpoints) their PUBLIC non-board-only problems are first handed to the placeholder `User.IsSystem` ("Deleted user", email
`deleted-user@system.invalid`, created on first use, soft-deleted from the start so it cannot sign in, hidden from the Trash and never purged), so other students keep their
attempts at them. Shares of those problems are dropped. Run it inside the caller's transaction before removing the user. The admin "delete user" is a soft delete
(restorable); `DeletedUserJanitor` purges it after `PlatformRuntimeSettings.DeletedUserRetentionDays` (Admin → System → Privacy; 0 = never; existing installs migrate to 0, new ones 90).

### Submission code lifetime

`SubmissionCodeJanitor` (every 12 h) blanks `Code`, `CompilerOutput` and `FailedTest` of FINISHED submissions older than `BoardCodeRetentionDays` / `PracticeCodeRetentionDays`
(Admin → System → Privacy; 0 = keep; existing installs migrate to 0, new ones 365 / 180) and sets `CodePurgedAt`. The row stays (verdicts, scores, XP never read the code); DTOs carry `CodePurged` and a null `Code`;
plagiarism, the ZIP export and the wall preview skip or mark purged ones.

### Statement protection: what is and is not stopped

The two statement endpoints are addressed by slug (`api/problems/{slug}/statement`, `api/practice/{slug}/statement`), never by the sequential id, and a non-member of the board gets the same 404 as for a problem that does not exist. The same holds for every endpoint that looks a row up by its sequential id (submissions, wall posts, practice submissions): a caller who may not see it gets 404, never 403 (`IdProbeTests`). Students get the statement only as the watermarked image (lossless WebP, `StatementImageService.RenderImage`; `StatementController`); the JSON of a board problem for a student carries NO statement and no expected output of samples (`Mapping.ToStudentDto`, as practice already did), only sample inputs for the Run box.
The picture is drawn for the screen it is shown on: the browser sends `?w=` (`StatementImage.vue`, redrawn when a resize crosses a tier) and `StatementImageService.WidthFor` maps it to a 400 px page at 2x (phone), 600 px at 1.5x or the 780 px desktop page, so the text is as big as the app's own and sharp on a dense screen; the width is part of the cache key (up to three renders per statement), and the watermark is stamped in page units so its size follows. `Solve.vue` shows the statement ONLY as that image for everyone, teachers and board staff included (an "Edit problem" link in its header leads to `ProblemEdit`, the one page that shows the text); the server still sends staff the statement text in the problem JSON, because the edit page needs it, so for staff this is a view rule, not a network one. The browser reports `X-Client-Flags` (`lib/automation.js`: `navigator.webdriver`, HeadlessChrome);
for a student the two statement endpoints then answer 403 and write an `automation-blocked` audit entry (once per person and problem an hour); `Security:BlockAutomatedBrowsers=false` turns it off; staff are never refused.
The header is the client's own word: it stops casual Playwright/Selenium scripts only. A model reading screenshots, desktop control, a second camera or a script that fakes the header are NOT stopped — the watermark makes a leak attributable, exam mode (paste blocked) and plagiarism detection are the other deterrents.

### Practice badges

`BadgeService` holds the rules (24 badges in six groups, each with its own icon and a bronze/silver/gold/platinum tier: problems solved, streaks, Medium problems, Hard problems, solved in a day, levels) as code; earned ones are `UserBadge` rows (key + `EarnedAt`, `SeenAt` null until the browser has announced it) and
stay even if the numbers later fall. Inputs are what the XP system already keeps: `SolveRecord` (board + practice, `journey-boss:` bonus rows excluded), `User.LongestStreak`, `MaxSolvedInADay`, level from `Xp`.
`GradeResultConsumer` calls `EvaluateAsync` after a full solve (before the result is pushed); `BadgeBackfill` gives existing users their badges silently once at startup. `GET /api/me/badges` (all, with progress) and
`GET /api/me/badges/new` (earned since the last look, each returned once → `BadgeToast`, triggered in `App.vue` when XP changes). UI: Account → Badges (`BadgeGrid.vue`), only the owner sees them. Names/descriptions are English
in the API and translated with `tr()` in the browser. Journey boss badges (`JourneyWorld.BadgeName`) are still only a label.

### One instance or several (`Realtime:Backend`)

With `redis`, SignalR also goes through a Redis backplane (`AddStackExchangeRedis`, `Program.cs`; `BackplaneTests`). The Redis judge broker (`RedisJudgeQueue`, `Judge:Queue:Backend=redis`) uses Streams + consumer groups, at-least-once: a job is acknowledged by `IJudgeJobSource.AckAsync` (called by `JudgeWorker` when done, not on shutdown), a verdict when the web consumer asks for the next one, and anything left pending is taken over after `Judge:Queue:ReclaimAfterSeconds`; a reclaimed ad-hoc `RunJob` is dropped. After a Redis failure `RedisCounterStore` stays on memory for 30 s (one slow call, not one per request).
One switch, `Realtime:Backend` = `memory` (default, ONE instance, no Redis) or `redis`, decides where the state that must be shared lives. Besides the presence/draft/lecture/AI-job stores it now covers:
`ICounterStore` (`InMemoryCounterStore` / `RedisCounterStore`, `Services/Realtime/CounterStore.cs`): `LoginThrottle` (counters are `Increment`ed up front and the attempt is allowed only while every counter is within its cap, so a burst across instances cannot pass),
`RateLimiter`, `SubmitCooldown`, the `/api/admin` token throttle and the AI hint spacing / one-at-a-time flag (`TryClaim` = `SET NX PX`, which expires by itself). Redis down → the call falls back to this instance's memory, never fails closed.
`JobLock` (`ShouldRun(name, interval)`): every periodic janitor/backfill claims its name for 0.9 × its interval before a sweep, so with several instances one runs it (a lone instance always passes; tests build janitors without a lock = always run).
`RuntimeCacheLoader.LoadAsync` is the ONLY place that fills the in-memory copies of admin-edited settings (admin emails, AI settings/providers, LTI origins, `PlatformRuntimeConfig`); startup calls it, and `RuntimeCacheRefresher` calls it every `Realtime:ConfigRefreshSeconds`
(default 15 with Redis, 0 without). A new admin-editable cache goes in the loader, not in `Program.cs`. Still per instance: `AiController._generating`, `PracticeController._aiCache`, the 10 s session recheck. Tests: `SharedStateTests` (memory always; Redis when `BEECODING_TEST_REDIS=host:port`, any Redis, e.g. `pip install redislite` brings a binary).

### MCP (read-only, for teachers)

`POST /mcp` (`McpController` + `Services/Mcp/McpServer`, hand-written JSON-RPC, stateless streamable HTTP, `Mcp:Enabled` default true) lets a teacher connect an AI assistant to their own boards. Signed in ONLY by a personal access token (`ApiToken`: SHA-256 kept, text shown once, 5 per teacher, 90 days, created in Account → "Connect an AI assistant" after re-entering the password, `ApiTokensController`); the cookie is ignored and a student or deleted owner is refused; a password change or reset deletes the owner's tokens. Eight read tools: `board_progress`, `students_at_risk` (stuck = 5+ attempts and latest not Accepted, deep AI hints, inactive N days, never submitted), `session_summary` (a time window; a time without an offset is the caller's local time via `User.UtcOffsetMinutes`; admins also get the IntelliSense peak), `problem_stats`, `student_progress` (one student, per-problem verdict and attempts, no code), `hint_usage` (AI-tutor hints per problem and student, recent days), `problem_issues`, `lsp_usage` (platform admins). They go through `BoardService.BuildProgressAsync` and the board-staff checks, return names and counts only (no email, code or statements), and cut and mark student-written report text as data (prompt-injection). Bad guesses are counted per IP before the lookup, 60 calls a minute per token. OAuth for the web "Connect" button (`OAuthController`, RFC 9728/8414/7591, PKCE S256 only, public clients): `/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server` (also served as `openid-configuration`), `POST /oauth/register` (redirects must be on the allow-list: claude.ai/claude.com callbacks or `Mcp:OAuthRedirects`, plus `http://localhost`), `GET /oauth/authorize` → the SPA consent page `/oauth/consent` (`OAuthConsent.vue`, teachers only, same-origin POST `/api/oauth/authorize` returns the redirect), `POST /oauth/token`. The result is an ordinary `ApiToken` with `ClientId` set: 1-hour access token + refresh token (60 days, rotated per use, atomic), one row per person and app, listed/revocable in Account, deleted by a password change. Codes are single-use and burn on a failed verifier. Sub-path deployments must forward two well-known paths from the host root (INSTALL.md §4C-4). Not verified against the real claude.ai yet.

### IntelliSense use history

`LspUsageSampler` (every minute, per instance, no `JobLock`) writes an `LspUsageSample` (`At` minute, `Host`, `Students` = distinct people attached via `LspBridge.AttachedStudents`, `Processes` = shared clangd processes) only while someone is attached plus the minute it drops to zero; rows older than 30 days are deleted hourly. `GET /api/admin-ui/system/lsp-usage?hours=` sums instances per minute and thins long windows to the busiest minute of 5/15; chart in Admin → System. The live "x / y attached" in the settings is still only the instant.

### Judge busy + LTI grade retry

The in-process judge channel is bounded (`Judge:QueueCapacity`) and in Wait mode, so a full one would leave a submit hanging: `SubmissionsController.Submit` / `PracticeController.Submit` ask `JudgeQueueStats.IsFull` first and answer 503 "The judge is busy" before any row or cool-down slot is used (the Redis list is unbounded, never "full").
A grade the LMS did not take (LTI score post, board or practice) is kept as an `LtiSyncRetry` row (`LtiGradeSyncService.TrackResultAsync`; one per kind + target + user, the score is recomputed each time) and sent again by `LtiSyncRetryJanitor` (every 2 min, waits 1/5/15/60/180/720 min, then dropped). The teacher is notified on the first failure only.

### Audit log lifetime

`AuditLogJanitor` (daily) deletes audit-log rows older than `Audit:RetentionDays` (365; 0 = keep forever).

### Statement images (no equations)

A statement can carry pictures: the editor's "Add image" uploads to `ImagesController` (`POST /api/images`, signed-in users, upload checked against the admin-set limit (`PlatformRuntimeConfig.ImageMaxKb`, default 1 MB, refused if over), then
compressed (≤1200 px wide; the smaller of lossless PNG and lossless WebP, lossy WebP q90 only when that is smaller than the upload, i.e. a photo; measured: WebP lossless beats PNG on flat colours but loses on busy screenshots; pictures stored earlier stay PNG/JPEG and `ImagesController.ContentTypeOf` serves each as what it is), kept in `StatementImage`; `ImageMaxPerUser` caps the count; SVG is refused on purpose, it is code) and inserts `![](/api/images/{id})`. Two renderers must both understand
that reference: `MarkdownBlock.vue` (prefixes `withBase` for a sub-path deployment) and `StatementImageService` (the protected image every
student sees; `StatementController` loads the ids found by `ImagesController.RefsIn`). `StatementImageJanitor` deletes pictures no statement names (live or soft-deleted, board or bank) `Images:OrphanRetentionDays` (3; 0 = never) after the sweep that first found them unused (`StatementImage.UnusedSince`), so un-using a picture always leaves time to undo. Storage per uploader is shown to platform admins (Admin → System, `GET /api/admin-ui/image-usage`) and, scoped to the members, to org admins (Members tab, `GET /api/org-admin/{id}/image-usage`) through `ImageUsage` / `ImageUsagePanel.vue`. Formulas (`$..$`, `$$..$$`, `\(..\)`, `\[..\]`) are typeset: KaTeX in the browser (`lib/math.js`, loaded only when a statement has one, `trust:false`, formulas put back AFTER DOMPurify so its allow-list is untouched) and CSharpMath in the image (`StatementMath` pulls them out before Markdig, `StatementImageService` draws them on the same canvas under a static `MathLock`, because CSharpMath's glyph caches are not thread-safe). Both use the same limits (200 formulas, 500 characters each) and fall back to ASCII text for what they cannot read. CSharpMath lacks `\dfrac`/`\bmod`/`\pmod`: `StatementMath.Shim` rewrites them. The AI prompts still tell the model not to write LaTeX.

### Recent solves (leaderboard tab)

`GET /api/leaderboard/recent` (`ProgressController.RecentSolves`, `RecentSolves.vue`, tab "Recent solves" on the leaderboard): the latest 20 first full Accepted
solves in PRACTICE (`BankSubmission`, not `SolveRecord`, because a solve through a board is recorded there too under the same `bank:` key and would leak a
board/exam's progress), full display names like the ranking, link to `/practice/{slug}`. Only PUBLIC bank problems, never one solved by its own author, same
org/board scope + membership check (403) as the ranking, cached 15 s per scope. Board problems are deliberately not listed.

### Student-authored problems

A student's problem bank (`/bank`, "My problems") is a private drafting space: `BankController` scopes a student to
their own rows (`Readable()`), ignores `IsPublic` from them, freezes a problem that is under review, has a pending
proposal or is already public, and caps the bank (`PlatformRuntimeConfig.StudentMaxProblems`, admin-editable under
Admin → System; the test-count and pending caps are constants in `BankController`). Nothing goes live without a reviewer:
- **Practice** — `submit-practice` sets `PendingReview`, the same flag and admin queue as AI-generated problems
  ("Problem review"); approve flips `IsPublic`, reject stores `ReviewNote`. `PracticeController.Pool()` is just
  `IsPublic`, so approval is all it takes. AI generation works for students too, but their drafts are *not* auto-queued.
- **Board** — `propose/{boardSlug}` creates a `ProblemProposal`; the board owner answers in `ProposalsController`
  (approve copies the problem onto the board **hidden**, like `copy-to`). `Board.AllowProposals` lets an owner turn
  proposals off. Both flows notify through the bell/Web Push (`Notification.ProposalId`, kinds `proposal`,
  `proposal-update`, `practice-review`; board-less like reports).
- Authors can open and submit to their own problem before it is public (`PracticeController.Playable()`: public or owned; the list and guide stay public-only, and `StatementController` allows the owner too). Nobody earns XP/streak for solving a problem they authored (`GradeResultConsumer.ApplyPracticeAsync`).

### AI hint guards (prompt injection)

The hint has no tools: its reply is only displayed (DOMPurify + CSP `img-src 'self' data: blob:`), and hints see sample tests only. Student text in the prompt
(code, compiler output, stderr, teacher code) is wrapped by `AiTutorService.Fenced`, a fence longer than any backtick run inside, so it cannot pose as a prompt section.
Output guard: `ClampCodeBlocks` replaces a code block (``` or ~~~, also one left open) with more than `HintCodeLines` (3) non-blank lines in a graded hint (`LiveCodeLines` 13 in live mode);
while streaming, `PastCodeLimit` stops the model as soon as a block passes the limit and the final text replaces what was shown. The "never write the solution" rule is still the model's to keep
for code without a fence or for prose that spells out the algorithm: measure it with `scripts/ai-injection-check.py` against the running app.

### AI review aids (admin only)

`AiController` `check-problem/bank/{id}` and `triage-report/{id}` (UI: `AiCheckPanel.vue` in Admin → Problem review and on
the reports page). The AI sees only the statement and writes its own solution, the judge runs it on every stored test, and
differences from the stored expected outputs are listed; triage adds one more call that weighs the report against that
evidence. Strictly advisory — results are only displayed, user-written text in the prompts is treated as untrusted data,
and they share the AI kill switch, daily quota and detached-job + polling pattern with problem generation.

### Similar problems (admin review aid)

Admin → Problem review → "Similar problems" (`SimilarProblemsController`, `SimilarProblems.vue`) lists the public/pending bank problems closest to
one, to catch a duplicate before approving. Two rankers: `TextSimilarity` (TF-IDF over character 4-grams, no model — always available; the corpus of all public/pending problems is cached 5 min in `TextCorpusCache`, so a problem added or edited in that window is missing or old in it) and, when
`Embeddings:ModelDir` holds multilingual-e5-small, `EmbeddingService` (ONNX Runtime, loaded on use, released when idle) with vectors cached in
`BankProblemEmbeddings` by `EmbeddingBackfill`. The endpoint uses e5 only when EVERY candidate has a current vector (a partial set would hide problems),
else text overlap. Measured on this bank: with 15 hand-written rewrites e5 had the original in the top 5 every time, text overlap 13/15, but neither
gives a threshold that separates duplicates from same-topic problems — so it is only a list for a person; do not auto-reject on a score. Authors (students too) get the same list for their own problems on `/bank` (`BankController.Similar`): candidates are ONLY public problems plus the caller's own pending ones — never anyone else's draft or pending problem — each with `Linkable` (public or own) → `/practice/{slug}`; titles and scores only, one check per 3 s (`SimilarCooldown`). Admins see public + everyone's pending. `SimilarProblemFinder` only ranks what the caller hands it, so the access rule lives in the two callers. After a solve, `PracticeController.Next` (`NextProblems.vue` on `PracticeSolve`) offers the guide's top pick plus up to two unsolved PUBLIC problems closest to the solved one, never easier (`Level >=`), empty until the caller has solved it; the similarity part is skipped, not refused, when `SimilarCooldown` says they just asked. The Practice search adds `GET /api/practice/suggest` ("You might be looking for", e5 only, empty without the model): the page asks for it when a typed search finds fewer than 3 problems; it ranks the PUBLIC pool by stored vectors (symmetric `query:` on both sides — no second vector per problem; measured a little different from the asymmetric query/passage form, neither clearly better), no score cut-off because e5 scores crowd together. The plain title/tag `LIKE` search is unchanged and still comes first. The problem editor's "Suggest tags" (`TagSuggestController`, `TagSuggest.vue`) votes the tags of the 5 nearest PUBLIC problems (measured leave-one-out on this bank: top tag right 65%, commonest-tag baseline 25%), shown as chips to click — never applied by itself; hidden when the model is off. On a problem still unsolved after 5 finished submissions, `PracticeController.Easier` (`EasierProblems.vue`) offers up to 2 easier public problems that SHARE A TAG with it and that the caller has not solved, closest in meaning first — the shared-tag rule is deliberate (measured: closeness alone offered unrelated problems for hard ones); empty when not stuck, already Easy, or nothing shares a tag (~14% of medium/hard problems in the current bank). It is not AI
(no provider, no kill switch/quota). The tokenizer is `Microsoft.ML.Tokenizers` sentencepiece with ids remapped to the XLM-R vocabulary (`ToModelIds`,
checked against the Hugging Face tokenizer by a test that runs when `E5_MODEL_DIR` is set).

AI problem generation is the one place that retrieval feeds a model (`AiController.NearestPublicAsync`): before `GenerateProblemAsync` it finds the 5 PUBLIC bank problems closest to the teacher's idea with the same finder, and the prompt lists their titles and tags (one line each, cut, marked as data) with "do not repeat any of them". Never statements, never a private or pending problem; a failure only means no hint. Not RAG over statements or solutions, and not used by the hint tutor on purpose (a similar solved problem would leak an approach).

### Practice default order

`GET /api/practice` without `sort` orders by `PracticeRanking.Key`: level first, then easiest first by the full-AC rate pulled toward a per-level typical rate (`LevelPrior`, weight `PriorWeight` submissions), so one lucky submission does not top a well-tried problem and an untried one sits at its level’s typical spot; ties go to the most tried, then title. The priors are guesses, not measured on the production bank. The rate counts submissions, not people, and includes authors’ own tests. The explicit `sort` values (likes, submissions, acrate, acrate_asc) are unchanged.

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
organization without an explicit membership row.

Joining an organization is never silent: `OrgAdminController.AddMember`/`ImportMembers` add an account straight away only when its email is on one of
`Organization.EmailDomains` (`OrgDomains`, set only by the platform admin in Admin → Organizations, since it skips consent; empty = never, since organizations can be departments of one university); anyone else gets an
`OrganizationInvite` that `OrgInvitesController` lets them accept, decline, or later leave (banner via `MeDto.PendingOrgInvites`, UI in
`OrganizationSettings.vue`). Membership shows the org admin the member's practice activity, and the org-admin bank (`BankFor`) is public problems
only — a member's private drafts are never offered. LTI auto-enrolment is unchanged (the user arrived from that org's LMS). LTI 1.3 (`Services/Lti/`,
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

The AI daily quota and `AiUsage.Day` use the user's local calendar day too, but server-side code often has no request at hand (background AI jobs), so the browser's UTC offset rides on every request as `X-UTC-Offset` (`lib/api.js`), `UtcOffsetMiddleware` stores it in `User.UtcOffsetMinutes`, and `UserClock.LocalTodayAsync` turns it into a day. Rows from before that are UTC days.

### Secrets at rest

`SecretBox` (Core) seals the few secrets the database keeps — `User.TotpSecret`, `AiProviderConfig.ApiKey`, `LtiToolKey.PrivateKeyPem` — as `enc1:<Data Protection text>`
through EF value converters (`AppDbContext.OnModelCreating`); `Program.cs` plugs the protector in right after `Build()` and re-saves legacy plain rows once at startup.
A value that cannot be decrypted reads as null (lost key ring), never as an exception. The key ring must survive restarts: set `DataProtection:KeysPath` (INSTALL.md) and
back that folder up with the database. The columns' `MaxLength` attributes are now shorter than the stored text — SQLite ignores them (`shortcut:` in the model).

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
  idiom. Draft autosaves (`lib/draft.js`) self-expire (1 hour after a solved problem, 1 week
  otherwise), pruned once per app load, not per save; sign-out and account deletion clear every draft (`clearAllDrafts`, shared computers).
- Display preferences that should follow the user across devices (site/editor theme, fonts, default language, table/board view, chart granularity, ...) are still read and written through plain `localStorage`, but the keys listed in `lib/prefs.js` (`SYNCED_KEYS`, mirrored by the allowlist in `PreferencesController`) are mirrored to `User.Preferences` — pulled before the app loads (`main.js` → `boot.js`), pushed debounced on write. To sync a new preference add its key to BOTH lists. Per-device state (drafts, celebration markers, last queries) deliberately stays local.
- The privacy policy, terms and the problem-writing guide are bilingual (English default, Indonesian): `lib/docLang.js` holds the choice (synced preference `beecoding.docLang`), `DocLangToggle.vue` flips it, and each page carries both texts side by side (`v-if="docLang === ..."`). A change to one language's text must be mirrored in the other. The editor's starting statement follows the same choice.
- The interface itself is bilingual too, with the same choice (`docLang`, the `EN | ID` toggle in the header, the user menu and the sign-in/landing pages): `lib/i18n.js` exports `tr(text, params)`, where **the English text is the key** and `lib/i18n/id.js` maps it to Indonesian (`{name}` in a text is filled from `params`). A text with no entry stays English, so a missing translation never breaks a screen. In templates write `{{ tr('Save') }}` (the helper is `tr`, not `t`: many files use `t` as a loop variable); for a pure-JS message use `tr()` at the moment it is created; for a list of English texts defined once (an array in `<script setup>`) translate where it is displayed, `{{ tr(item.title) }}`, because a constant evaluated at setup would not follow a language switch. Plural: two keys (`tr(n === 1 ? '{n} problem' : '{n} problems', { n })`). Do not put sentence fragments around a `{{ }}` in a template; make the whole sentence one key with `{name}` parameters. Add the Indonesian text to `id.js` in the same change; `npm run check:i18n` (in `ClientApp`) lists the `tr()` texts that still have no entry. Server texts: an API error message stays English on the server and is translated in the browser (`lib/api.js` runs `tr()` on every error it throws; `lib/i18n/id-server.js` holds those, an entry written with `{p0}`, `{p1}` also matches a message that carries a value, e.g. "Please wait 3s ..."), so a new `BadRequest("...")` is translated by adding its text there. The few texts the server writes itself — emails, web push, the notification sentence — use `Services/Text.cs` (`Text.T(lang, "English {0}", args)`, Indonesian in its `Id` table; the language is `Text.LangOf(user.Preferences, request)`: the saved `beecoding.docLang`, else the `X-Lang` header `api.js` sends). Not translated yet: the Admin and Org-admin screens, the guide's reasons and other API data that is prose (e.g. "Continue with array"), and statements (user content).
- Custom code snippets (`lib/snippets.js`, edited at Account → Editor) are a synced preference (`beecoding.editor.snippets`, a JSON array) offered by one shared Monaco completion provider for `c`/`cpp`, independent of clangd.
- Auth/session itself is an `HttpOnly` cookie, never `localStorage` — don't change that.

Guess caps (`LoginThrottle`): count the attempt with `TryBegin` BEFORE checking the password or code (a success clears it) — checking first and recording after lets a burst of simultaneous guesses all pass.

### Config surface (`appsettings.json`, all overridable via `Section__Key` env vars)

`Database:Provider` (`sqlite` default, or `postgres` with `ConnectionStrings:Default` as an Npgsql string: the full test suite passes on Postgres 16 (see "Two providers" above) and it has its own migrations, but it has NOT run against production data or load, and a SQLite→Postgres copy tool exists but only a copy of the production backup was moved with it; MaxLength attributes are enforced there — production data was checked against them (sealed `enc1:` secrets and long audit labels were over the old limits; raised, and `AuditLog` clips); on Postgres every `DateTime` goes through `UtcDateTimeConverter` (`AppDbContext.ConfigureConventions`), SQLite keeps its "unspecified" kind), `Judge` (queue backend, concurrency, limits, sandbox), `Lsp` (clangd bridge, off by
default; `Lsp:Sandbox=true` runs each clangd under bubblewrap like the judge (off by default; with it on and no bwrap the bridge refuses to start): without it a student's `#include "/some/file"` can make clangd read any file the app's OS user can; `MaxConcurrent` clangd processes of one student each, or — Admin → System "Share one clangd between students", off by default — `SharedClangd`: one process per language (up to `SharedMaxProcesses`, the next starting only when the earlier ones are full) serving up to `SharedMaxSessions` students each, each in a file of their own, ids renumbered and only editing methods let through; `TotalCap` (`SharedMaxTotalSessions`, or automatically `SharedMemoryPercent` of host RAM ÷ `SharedSessionMemoryMb`) limits students attached at once, reserved before any parsing, because memory grows with open files: measured with clangd 18 on the real code of a 35-student class (22 Nov backup): ≈5 MB per C file, ≈25 MB per C++ file with ordinary headers, ≈115 MB with `<bits/stdc++.h>`, plus ≈75–110 MB per process; the class in shared mode ≈0.6 GB against ≈4 GB with a clangd each; `SharedPchStorage=disk` saves only ≈8%; the default `SharedSessionMemoryMb` is 60, a class that writes the umbrella header should set 120; the C++ starter template names `<iostream>` for that reason), one bridge per account and language (`LspBridge`, in Core, key = user id; `LspEndpoint` only checks sign-in/`LspEnabled`); clangd on its own node: `Lsp:RemoteUrls` (web side; empty = clangd here) makes `LspBridge` relay the editor's frames untouched to `BeeCoding.Lsp` (`/lsp/node`, no DB/auth, refuses to start without `Lsp:NodeToken`, which the web sends as `Lsp:RemoteToken`), a student sticks to one node by an FNV hash of the id (`LspBridge.Candidates`, same on every web instance; next URL when it is down; none reachable → close `server busy`), the Admin shared toggle travels as `shared=`, and the session classes live in `BeeCoding.Core/Lsp` (`InternalsVisibleTo` BeeCoding.Tests); tests `LspRemoteTests` use a stand-in node: a newer editor takes over and the older one is closed with reason `replaced`, which the browser answers with state `elsewhere` (basic completion, no auto-reconnect), `Ai` (AI tutor, off by default — needs `Ai:Enabled=true` + `Ai:ApiKey`; `Ai:Fallbacks` = backup `{ApiKey, BaseUrl, Model}` providers tried in order on timeout/429/5xx/401/403/empty reply, never on 400/422 — hints only before the first streamed word, the generators spread their retries over the providers, and an organization with its own key never uses the platform's backups, max tokens or reasoning effort, since those suit the platform's model: it has its own (Org admin → AI, `ai-provider/extras`, same `AiProviderConfig` row, applied only while it has its own key; its backup URLs are validated `strict`), else the appsettings defaults),
`Admin:Emails`/`Admin:Token` (platform admin access + scripted bank-ingest auth),
`Email` (SMTP for password-reset and email-verification links; unset = both are hidden/off, admins can still issue a reset link or mark an email verified from Admin → Users) + `App:PublicUrl` (base URL used in those links), `Auth:RequireVerifiedEmail` (hard gate: unverified accounts can only reach `/api/auth/*`; ignored while `Email` is unset; platform admins exempt — see `EmailVerificationGate`),
`Auth:Google:ClientId`/`ClientSecret` (Sign in with Google via OIDC code+PKCE; needs `App:PublicUrl`; unset = button hidden; never auto-links on email — see `GoogleController`),
`Auth:Passkeys:RpId`/`RpName`/`Origins` (WebAuthn passkeys as a *passwordless* sign-in — user-verified, so it skips the TOTP step; unset = passkeys off; see `PasskeyController`/`PasskeyService`). optional second factors after password/Google live in `MfaController`/`MfaService`: TOTP authenticator apps (always available), emailed 6-digit codes (only while `Email` is configured; `EmailLoginCode`) and recovery codes — a user may enable several and chooses one at sign-in. MFA is optional unless Admin > Users switches on a required group (admins / org admins / teachers; `MfaPolicy` + `MfaRequirementGate`, stored in `PlatformRuntimeSettings`; LTI sessions exempt),
`Auth:Turnstile:SiteKey`/`SecretKey`/`Hostnames` (Cloudflare Turnstile human check on sign-in, register and forgot-password; unset = off; the site key reaches the browser via `/api/auth/config`; verified server-side by `TurnstileService` for `success` + `action` + `hostname`, fails closed),
`Push:PublicKey`/`PrivateKey`/`Subject` (Web Push for the bell with the tab closed: a VAPID pair + `mailto:` subject; unset = push off and its button hidden; browsers subscribe via `PushController`/`public/sw.js`, sends go through `WebPushService`, endpoints limited to the known browser push services),
`Security:TrustAllProxies` (forwarded headers are trusted only from loopback/private proxies; set it when the reverse proxy has a public IP), `Security:CookieAlwaysSecure`, `Security:SessionRecheckSeconds` (default 10, 0 = every request: how long the per-request check that a cookie's user still exists / has the same role is cached — an admin deleting a user or changing a role takes effect within it); a password change or reset ends every other session through the `beecoding:pwv` claim — a fingerprint of the password hash, compared in `OnValidatePrincipal`, cookies without it still pass,
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
