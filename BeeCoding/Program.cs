using BeeCoding.Data;
using BeeCoding.Hubs;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lsp;
using BeeCoding.Services.Lti;
using BeeCoding.Services.Realtime;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// journald integration + Type=notify readiness when run under systemd; no-op otherwise.
builder.Host.UseSystemd();

// Cap request bodies. Code/stdin are validated per-endpoint; this is the backstop for
// the admin ingest route, big generated problems, and anything else. Keep nginx's
// client_max_body_size at least this large or nginx 413s first.
var maxBodyMb = builder.Configuration.GetValue("Kestrel:MaxRequestBodyMb", 32);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = (long)maxBodyMb * 1024 * 1024);

// Trust X-Forwarded-* from a reverse proxy (nginx) so Request.Scheme is "https"
// behind TLS termination. Only the proxy should be able to reach the app port.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// /health = liveness (process is up); /health/ready = readiness (DB reachable).
builder.Services.AddHealthChecks()
    .AddCheck<DbHealthCheck>("db", tags: new[] { "ready" });

// Brotli + gzip for text-ish payloads (the Monaco bundle is ~3.3 MB -> ~0.86 MB).
// Safe over HTTPS here: the compressible responses are static assets / non-secret JSON,
// and auth lives in an httpOnly cookie, not response bodies.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "image/svg+xml", "application/wasm", "application/manifest+json" });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=beecoding.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "beecoding.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;   // HTTP dev fallback — see OnSigningIn below
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // Secure when served over HTTPS
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        // LTI embeds this app in an iframe on the platform's (a different site's) page —
        // every fetch()/XHR our SPA makes from inside that iframe is a cross-site request
        // from the cookie's point of view, and SameSite=Lax is never sent on those (only on
        // top-level navigations), so every /api/* call 401s even right after a successful
        // launch. SameSite=None fixes that, but browsers silently drop a None cookie unless
        // it's also Secure — which requires HTTPS, so this only flips when the request looks
        // like HTTPS; a plain HTTP dev server keeps Lax (LTI needs HTTPS anyway, so this
        // never matters there).
        //
        // ctx.Request.IsHttps only reflects reality if a fronting reverse proxy correctly
        // forwards X-Forwarded-Proto (see the ForwardedHeaders config above) — some setups
        // (notably some reverse-proxy GUIs, e.g. Synology's) don't, in which case the app
        // sees plain HTTP even though the browser is on HTTPS and the auto-detection above
        // never fires. Security:CookieAlwaysSecure=true is an escape hatch for exactly that:
        // it forces Secure+SameSite=None unconditionally. Only set it if the app is in fact
        // reachable solely over HTTPS externally — it would otherwise mark the cookie Secure
        // on a real plain-HTTP deployment, which browsers then simply never send back.
        var forceSecureCookie = builder.Configuration.GetValue("Security:CookieAlwaysSecure", false);
        o.Events.OnSigningIn = ctx =>
        {
            if (ctx.Request.IsHttps || forceSecureCookie)
            {
                ctx.CookieOptions.SameSite = SameSiteMode.None;
                ctx.CookieOptions.Secure = true;
                // CHIPS: Chrome (and, following its lead, other browsers) now blocks
                // unpartitioned third-party cookies by default even when SameSite=None;
                // Secure is correct — Partitioned opts back in without needing the user to
                // grant a manual site exception, since the whole point of partitioning is
                // that it can't be used for cross-site tracking (storage is isolated per
                // top-level site, which is exactly "usable inside this one LMS's iframe").
                ctx.CookieOptions.Extensions.Add("Partitioned");
            }
            return Task.CompletedTask;
        };
        // API/hub calls should get 401/403, never an HTML redirect.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        // A user soft-deleted (or removed) by an admin loses access on their very next
        // request instead of riding out the rest of their 7-day cookie. An admin changing
        // someone's Teacher/Student role also takes effect immediately — the claim in their
        // existing cookie is refreshed in place, no re-login needed.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var idClaim = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (idClaim is null || !int.TryParse(idClaim, out var uid)) return;

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var row = await db.Users.Where(u => u.Id == uid)
                .Select(u => new { u.DeletedAt, u.Role, u.DisplayName, u.Email }).FirstOrDefaultAsync();
            if (row is null || row.DeletedAt is not null)
            {
                ctx.RejectPrincipal();
                await CookieSignIn.SignOutAsync(ctx.HttpContext);
                return;
            }

            var currentRole = ctx.Principal!.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (currentRole != row.Role.ToString())
            {
                var claims = new List<System.Security.Claims.Claim>
                {
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, uid.ToString()),
                    new(System.Security.Claims.ClaimTypes.Name, row.DisplayName),
                    new(System.Security.Claims.ClaimTypes.Email, row.Email),
                    new(System.Security.Claims.ClaimTypes.Role, row.Role.ToString()),
                };
                var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                ctx.ReplacePrincipal(new System.Security.Claims.ClaimsPrincipal(identity));
                ctx.ShouldRenew = true;
            }
        };
    });
builder.Services.AddSingleton<AdminAccess>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddSingleton<AiRuntimeSettings>();
builder.Services.AddSingleton<AiProviderRuntime>();
builder.Services.AddSingleton<LtiPlatformOriginsCache>();
builder.Services.AddSingleton<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.Requirements.Add(new AdminRequirement()));

builder.Services.Configure<JudgeOptions>(builder.Configuration.GetSection("Judge"));
builder.Services.Configure<LspOptions>(builder.Configuration.GetSection("Lsp"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddSingleton<LspEndpoint>();
// The default HttpClient.Timeout is 100s — too short for generate-problem. Let each call's
// own CancellationTokenSource (Ai:TimeoutSeconds / Ai:GenerateTimeoutSeconds) be the limit.
builder.Services.AddHttpClient<AiTutorService>(c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<AiUsageService>();
builder.Services.AddScoped<AiHintProgressService>();

// --- LTI 1.3 (see Controllers/LtiController.cs) ---
builder.Services.AddHttpClient("lti", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<LtiLoginStateStore>();
builder.Services.AddSingleton<LtiJwksCache>();
builder.Services.AddSingleton<LtiLaunchValidator>();
builder.Services.AddScoped<LtiToolKeyService>();
builder.Services.AddScoped<LtiProvisioningService>();
builder.Services.AddScoped<LtiDeepLinkService>();
builder.Services.AddScoped<LtiTokenService>();
builder.Services.AddScoped<LtiGradeSyncService>();

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<NativeToolchain>();
builder.Services.AddSingleton<SysstatService>();
builder.Services.AddSingleton<NativeCompiler>();
builder.Services.AddSingleton<NativeSandbox>();

// --- Redis: shared by the realtime stores and (optionally) the judge queue ---
builder.Services.Configure<RealtimeStoreOptions>(builder.Configuration.GetSection("Realtime"));
var realtimeOpt = builder.Configuration.GetSection("Realtime").Get<RealtimeStoreOptions>() ?? new RealtimeStoreOptions();
var judgeOpt = builder.Configuration.GetSection("Judge").Get<JudgeOptions>() ?? new JudgeOptions();

string? redisConn = realtimeOpt.UseRedis ? realtimeOpt.RedisConnectionString
    : judgeOpt.Queue.UseRedis ? judgeOpt.Queue.RedisConnectionString
    : null;
if ((realtimeOpt.UseRedis || judgeOpt.Queue.UseRedis) && string.IsNullOrWhiteSpace(redisConn))
    throw new InvalidOperationException("A 'redis' backend needs a connection string (Realtime:RedisConnectionString or Judge:Queue:RedisConnectionString).");
if (redisConn is not null)
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));

// Ephemeral realtime stores + the AI-job registry (see DEPLOY.md §2.4)
if (realtimeOpt.UseRedis)
{
    builder.Services.AddSingleton<IPresenceTracker, RedisPresenceTracker>();
    builder.Services.AddSingleton<IDraftStore, RedisDraftStore>();
    builder.Services.AddSingleton<ILectureStore, RedisLectureStore>();
    builder.Services.AddSingleton<IAiJobStore, RedisAiJobStore>();
}
else
{
    builder.Services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
    builder.Services.AddSingleton<IDraftStore, InMemoryDraftStore>();
    builder.Services.AddSingleton<IAiJobStore, InMemoryAiJobStore>();
    builder.Services.AddSingleton<ILectureStore, InMemoryLectureStore>();
}

// Judge queue (see DEPLOY.md §2.6): in-process Channel, or a Redis broker so the judge can
// be its own low-privilege deployment (BeeCoding.Judge). One singleton implements the
// producer side, the job-source side, and the grade-result stream the web tier reads.
if (judgeOpt.Queue.UseRedis)
{
    builder.Services.AddSingleton<RedisJudgeQueue>();
    builder.Services.AddSingleton<IJudgeQueue>(sp => sp.GetRequiredService<RedisJudgeQueue>());
    builder.Services.AddSingleton<IJudgeJobSource>(sp => sp.GetRequiredService<RedisJudgeQueue>());
    builder.Services.AddSingleton<IGradeResultStream>(sp => sp.GetRequiredService<RedisJudgeQueue>());
}
else
{
    builder.Services.AddSingleton<InProcessJudgeQueue>();
    builder.Services.AddSingleton<IJudgeQueue>(sp => sp.GetRequiredService<InProcessJudgeQueue>());
    builder.Services.AddSingleton<IJudgeJobSource>(sp => sp.GetRequiredService<InProcessJudgeQueue>());
    builder.Services.AddSingleton<IGradeResultStream>(sp => sp.GetRequiredService<InProcessJudgeQueue>());
}

builder.Services.AddSingleton<StatementImageService>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddSingleton<PlatformRuntimeConfig>();
builder.Services.AddSingleton<SubmitCooldown>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<IBoardNotifier, BoardNotifier>();

// The web tier always applies verdicts + notifies. It runs the compute worker itself only
// in the in-process setup; with the Redis broker, BeeCoding.Judge does the compiling/running.
builder.Services.AddHostedService<GradeResultConsumer>();
if (!judgeOpt.Queue.UseRedis)
    builder.Services.AddHostedService<JudgeWorker>();
builder.Services.AddHostedService<JudgeJanitor>();

builder.Services.AddScoped<VisibilityService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<OrgResolver>();
builder.Services.AddScoped<OrgAccess>();
builder.Services.AddScoped<WallService>();
builder.Services.AddScoped<ProgressService>();

const string DevCors = "dev-spa";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173", "https://localhost:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<PasswordService>());
    await DbSeeder.SeedBankAsync(db);

    // Backfill public slugs for boards created before slugs existed.
    var boardSvc = scope.ServiceProvider.GetRequiredService<BeeCoding.Services.BoardService>();
    var slugless = await db.Boards.Where(b => b.Slug == null || b.Slug == "").ToListAsync();
    foreach (var b in slugless) b.Slug = await boardSvc.GenerateSlugAsync();
    if (slugless.Count > 0) await db.SaveChangesAsync();

    // Same for problems / bank problems (the migration seeds these, this is a safety net).
    var sluglessProblems = await db.Problems.Where(p => p.Slug == null || p.Slug == "").ToListAsync();
    var sluglessBank = await db.BankProblems.Where(p => p.Slug == null || p.Slug == "").ToListAsync();
    if (sluglessProblems.Count > 0 || sluglessBank.Count > 0)
        await db.SaveChangesAsync();   // AppDbContext.SaveChangesAsync assigns the slugs

    // Backfill wall posts for submissions made before the wall existed.
    var missing = await db.Submissions
        .Select(s => new { s.UserId, s.ProblemId })
        .Distinct()
        .Where(k => !db.Posts.Any(p => p.ProblemId == k.ProblemId && p.UserId == k.UserId))
        .ToListAsync();
    foreach (var k in missing)
    {
        var boardId = await db.Problems.Where(p => p.Id == k.ProblemId).Select(p => p.BoardId).FirstAsync();
        db.Posts.Add(new BeeCoding.Models.Post { BoardId = boardId, ProblemId = k.ProblemId, UserId = k.UserId });
    }
    if (missing.Count > 0) await db.SaveChangesAsync();

    // Warm the in-memory DB-admin cache (see AdminAccess) with anyone granted admin from
    // the admin panel, so the "Admin" policy doesn't need a DB hit on every request.
    var dbAdminEmails = await db.Users.Where(u => u.IsAdmin).Select(u => u.Email).ToListAsync();
    scope.ServiceProvider.GetRequiredService<AdminAccess>().SetDbAdmins(dbAdminEmails);

    // Same for the AI pause/quota settings + per-user overrides (see AiRuntimeSettings).
    var aiSettings = await db.AiSettings.FindAsync(1);
    if (aiSettings is null)
    {
        aiSettings = new BeeCoding.Models.AiSettings { Id = 1 };
        db.AiSettings.Add(aiSettings);
        await db.SaveChangesAsync();
    }
    var aiOverrides = await db.AiUserSettings
        .Select(x => new { x.UserId, x.DailyQuotaOverride, x.Banned }).ToListAsync();
    var aiRuntime = scope.ServiceProvider.GetRequiredService<AiRuntimeSettings>();
    aiRuntime.SetGlobal(aiSettings.Paused, aiSettings.PausedReason, aiSettings.DailyQuotaStudent, aiSettings.DailyQuotaTeacher);
    aiRuntime.SetOverrides(aiOverrides.Select(x => (x.UserId, x.DailyQuotaOverride, x.Banned)));

    // Same for each organization's own AI settings override (see Organization/OrgAdminController).
    var orgAiSettings = await db.AiSettings.Where(x => x.OrganizationId != null)
        .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.Paused, x.PausedReason, x.DailyQuotaStudent, x.DailyQuotaTeacher })
        .ToListAsync();
    aiRuntime.SetOrgs(orgAiSettings.Select(x => (x.OrganizationId, x.Paused, x.PausedReason, x.DailyQuotaStudent, x.DailyQuotaTeacher)));

    // Same warm-up for which AI provider/credential to bill (see AiProviderRuntime) —
    // appsettings.json's Ai:* stays the ultimate fallback when neither layer has a row.
    var providerRuntime = scope.ServiceProvider.GetRequiredService<AiProviderRuntime>();
    var platformProvider = await db.AiProviderConfigs.FirstOrDefaultAsync(x => x.OrganizationId == null);
    if (platformProvider is not null)
        providerRuntime.SetPlatform(platformProvider.ApiKey, platformProvider.BaseUrl, platformProvider.Model, platformProvider.GenerateModel);
    var orgProviders = await db.AiProviderConfigs.Where(x => x.OrganizationId != null)
        .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.ApiKey, x.BaseUrl, x.Model, x.GenerateModel })
        .ToListAsync();
    providerRuntime.SetOrgs(orgProviders.Select(x => (x.OrganizationId, x.ApiKey, x.BaseUrl, x.Model, x.GenerateModel)));

    // Same warm-up for which origins the CSP should let frame this app (see
    // LtiPlatformOriginsCache) — every enabled LTI platform's issuer.
    var ltiOrigins = scope.ServiceProvider.GetRequiredService<LtiPlatformOriginsCache>();
    ltiOrigins.Set(await db.LtiPlatforms.Where(p => p.Enabled).Select(p => p.Issuer).ToListAsync());

    // Same warm-up for the judge/LSP runtime overrides editable from /admin/reports (see
    // PlatformRuntimeConfig) — first run seeds the row from appsettings.json so shipping
    // this feature doesn't silently change existing behavior; every run after that, the DB
    // row (not appsettings.json) is the live source of truth for these two fields.
    var runtimeConfig = scope.ServiceProvider.GetRequiredService<PlatformRuntimeConfig>();
    var platformRuntime = await db.PlatformRuntimeSettings.FindAsync(1);
    if (platformRuntime is null)
    {
        var seedJudgeOpt = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<JudgeOptions>>().Value;
        var seedLspOpt = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LspOptions>>().Value;
        platformRuntime = new BeeCoding.Models.PlatformRuntimeSettings
        {
            Id = 1, LspEnabled = seedLspOpt.Enabled, JudgeRateLimitMs = seedJudgeOpt.RateLimitMs,
        };
        db.PlatformRuntimeSettings.Add(platformRuntime);
        await db.SaveChangesAsync();
    }
    runtimeConfig.Set(platformRuntime.LspEnabled, platformRuntime.JudgeRateLimitMs);
}

// Build the sandbox runner + probe capabilities before serving traffic.
app.Services.GetRequiredService<NativeToolchain>().Initialize();

app.UseForwardedHeaders();

// Reverse-proxied under a subpath (e.g. nginx serving this app at /beecoding/ alongside
// others on the same domain)? Set PathBase (env var or appsettings), no trailing slash.
// Must match the frontend's VITE_BASE_PATH build setting (see vite.config.js). Strips the
// prefix for routing/static files while keeping it for URL generation and the auth
// cookie's Path (CookieBuilder defaults Path to PathBase when one is set).
var pathBase = builder.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase(pathBase.TrimEnd('/'));

// Security headers on every response. Override CSP with Security:ContentSecurityPolicy
// (a custom string), or set it to "off" to send no CSP header (e.g. if Monaco breaks).
// The default CSP's frame-ancestors is computed per-request from LtiPlatformOriginsCache
// instead of being baked in here — an LTI tool must be frameable by its registered
// platform(s) (both a normal resource-link launch and the Deep Linking picker embed this
// app in an iframe), so a blanket 'none' breaks LTI the moment one platform is registered.
// A custom override string is trusted as-is (its own frame-ancestors, if any, applies).
var cspCfg = builder.Configuration["Security:ContentSecurityPolicy"];
var cspOff = string.Equals(cspCfg, "off", StringComparison.OrdinalIgnoreCase);
var customCsp = !cspOff && !string.IsNullOrWhiteSpace(cspCfg) ? cspCfg : null;
var defaultCspBase =
    "default-src 'self'; " +
    "img-src 'self' data: blob:; " +
    "style-src 'self' 'unsafe-inline'; " +
    "script-src 'self' blob:; " +          // blob: for Vite's Monaco worker shim
    "worker-src 'self' blob:; " +
    "connect-src 'self'; " +
    "font-src 'self' data:; " +
    "object-src 'none'; base-uri 'self'";
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    if (!cspOff)
    {
        if (customCsp is not null)
        {
            h["Content-Security-Policy"] = customCsp;
            h["X-Frame-Options"] = "DENY";   // unknown whether the override covers framing — safest default
        }
        else
        {
            var origins = ctx.RequestServices.GetRequiredService<LtiPlatformOriginsCache>().Origins;
            if (origins.Count > 0)
            {
                h["Content-Security-Policy"] = $"{defaultCspBase}; frame-ancestors 'self' {string.Join(' ', origins)}";
                // Legacy browsers ignore frame-ancestors and fall back to this — it can only
                // express one policy, so once >=1 LTI platform is allowed, it's dropped
                // rather than wrongly DENY-ing every evergreen browser too.
            }
            else
            {
                h["Content-Security-Policy"] = $"{defaultCspBase}; frame-ancestors 'none'";
                h["X-Frame-Options"] = "DENY";
            }
        }
    }
    if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    // API responses reflect the caller's session (auth/me, board membership, …) and must
    // never be cached by the browser or an intermediate proxy — without this, a browser's
    // heuristic cache (or an over-eager reverse proxy) can replay a stale 200 from GET
    // /api/auth/me after sign-out, making a page refresh look like it's still logged in.
    if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
    await next();
});

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
    app.UseCors(DevCors);

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Vite writes content-hashed names under /assets — safe to cache forever.
        // Everything else (index.html, theme-init.js, favicon) must revalidate so a
        // deploy is picked up; ETag/Last-Modified still give cheap 304s.
        var dir = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers["Cache-Control"] =
            dir.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    },
});

app.UseWebSockets();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<BoardHub>("/hubs/board");

// Liveness: no checks, just "the app is answering". Readiness: run the "ready"-tagged checks.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
});

// C/C++ language server bridge (clangd). No-op unless Lsp:Enabled + clangd on PATH.
app.MapGet("/lsp/cpp", (HttpContext c, LspEndpoint ep) => ep.HandleAsync(c)).RequireAuthorization();

// Lets the editor skip the WebSocket attempt (and its console error) when the bridge is off.
// Live-toggleable from /admin/reports (see PlatformRuntimeConfig) — not appsettings-fixed.
app.MapGet("/api/lsp/enabled", (PlatformRuntimeConfig cfg) =>
    Results.Ok(new { enabled = cfg.LspEnabled }));

app.MapFallbackToFile("index.html");

app.Run();
