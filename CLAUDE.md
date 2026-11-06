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

### Small test suite, no linter

`BeeCoding.Tests` (xunit, `dotnet test BeeCoding.Tests`) covers only the risky rules: `VisibilityService`,
`GroupAccess`, `AiUrl` and the CSRF middleware (via `WebApplicationFactory<Program>` on a temp SQLite file).
Add a test there when you touch one of those. There is no frontend test runner or ESLint/Prettier config, so
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

`NotificationKinds` lists the kinds (reaction, comment, report, report-update, proposal, proposal-update, practice-review, review-submitted). Every creator in `NotificationService` asks `ChannelsAsync(user, kind)` first: a kind the platform switched off (Admin → System → "Notification kinds", `PlatformRuntimeSettings.NotifyOffKinds`, cached in `PlatformRuntimeConfig`) or the user muted (Account → "What to notify me about", `User.NotifyOffKinds`) creates no row, no push and no email. Email is opt-in per user (`User.NotifyEmailKinds`, only for kinds that stay in the bell), allowed per kind by the platform (`NotifyEmailKinds`, null = all but reaction/comment), and sent only when `Email` (SMTP) and `App:PublicUrl` are set and the address is verified. An admin can also lock a kind (`NotifyLockedKinds`): the user's mute is then ignored (switching it off for everyone still wins). Reactions/comments email once per unread pile (a bump does not mail again). Sent in the background; a failure is only logged.

### Audit log lifetime

`AuditLogJanitor` (daily) deletes audit-log rows older than `Audit:RetentionDays` (365; 0 = keep forever).

### Statement images (no equations)

A statement can carry pictures: the editor's "Add image" uploads to `ImagesController` (`POST /api/images`, signed-in users, upload checked against the admin-set limit (`PlatformRuntimeConfig.ImageMaxKb`, default 1 MB, refused if over), then
compressed (≤1200 px wide, lossless PNG, JPEG when that is smaller for a photo), kept in `StatementImage`; `ImageMaxPerUser` caps the count; SVG is refused on purpose, it is code) and inserts `![](/api/images/{id})`. Two renderers must both understand
that reference: `MarkdownBlock.vue` (prefixes `withBase` for a sub-path deployment) and `StatementImageService` (the protected PNG every
student sees; `StatementController` loads the ids found by `ImagesController.RefsIn`). `StatementImageJanitor` deletes pictures no statement names (live or soft-deleted, board or bank) `Images:OrphanRetentionDays` (3; 0 = never) after the sweep that first found them unused (`StatementImage.UnusedSince`), so un-using a picture always leaves time to undo. Storage per uploader is shown to platform admins (Admin → System, `GET /api/admin-ui/image-usage`) and, scoped to the members, to org admins (Members tab, `GET /api/org-admin/{id}/image-usage`) through `ImageUsage` / `ImageUsagePanel.vue`. Formulas (`$..$`, `$$..$$`, `\(..\)`, `\[..\]`) are typeset: KaTeX in the browser (`lib/math.js`, loaded only when a statement has one, `trust:false`, formulas put back AFTER DOMPurify so its allow-list is untouched) and CSharpMath in the PNG (`StatementMath` pulls them out before Markdig, `StatementImageService` draws them on the same canvas under a static `MathLock`, because CSharpMath's glyph caches are not thread-safe). Both use the same limits (200 formulas, 500 characters each) and fall back to ASCII text for what they cannot read. CSharpMath lacks `\dfrac`/`\bmod`/`\pmod`: `StatementMath.Shim` rewrites them. The AI prompts still tell the model not to write LaTeX.

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
  idiom. Draft autosaves (`lib/draft.js`) self-expire (1 day after a solved problem, 1 week
  otherwise), pruned once per app load, not per save.
- Display preferences that should follow the user across devices (site/editor theme, fonts, default language, table/board view, chart granularity, ...) are still read and written through plain `localStorage`, but the keys listed in `lib/prefs.js` (`SYNCED_KEYS`, mirrored by the allowlist in `PreferencesController`) are mirrored to `User.Preferences` — pulled before the app loads (`main.js` → `boot.js`), pushed debounced on write. To sync a new preference add its key to BOTH lists. Per-device state (drafts, celebration markers, last queries) deliberately stays local.
- The privacy policy, terms and the problem-writing guide are bilingual (English default, Indonesian): `lib/docLang.js` holds the choice (synced preference `beecoding.docLang`), `DocLangToggle.vue` flips it, and each page carries both texts side by side (`v-if="docLang === ..."`). A change to one language's text must be mirrored in the other. The editor's starting statement follows the same choice.
- Custom code snippets (`lib/snippets.js`, edited at Account → Editor) are a synced preference (`beecoding.editor.snippets`, a JSON array) offered by one shared Monaco completion provider for `c`/`cpp`, independent of clangd.
- Auth/session itself is an `HttpOnly` cookie, never `localStorage` — don't change that.

Guess caps (`LoginThrottle`): count the attempt with `TryBegin` BEFORE checking the password or code (a success clears it) — checking first and recording after lets a burst of simultaneous guesses all pass.

### Config surface (`appsettings.json`, all overridable via `Section__Key` env vars)

`Judge` (queue backend, concurrency, limits, sandbox), `Lsp` (clangd bridge, off by
default; `MaxConcurrent` clangd processes of one student each, or — Admin → System "Share one clangd between students", off by default — `SharedClangd`: one process per language (up to `SharedMaxProcesses`, the next starting only when the earlier ones are full) serving up to `SharedMaxSessions` students each, each in a file of their own, ids renumbered and only editing methods let through; `TotalCap` (`SharedMaxTotalSessions`, or automatically `SharedMemoryPercent` of host RAM ÷ `SharedSessionMemoryMb`) limits students attached at once, reserved before any parsing, because memory grows with open files: measured ≈85 MB per student with `<bits/stdc++.h>` plus ≈440 MB per process, ≈65 MB with `SharedPchStorage=disk`), one bridge per account and language (`LspEndpoint`): a newer editor takes over and the older one is closed with reason `replaced`, which the browser answers with state `elsewhere` (basic completion, no auto-reconnect), `Ai` (AI tutor, off by default — needs `Ai:Enabled=true` + `Ai:ApiKey`),
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
