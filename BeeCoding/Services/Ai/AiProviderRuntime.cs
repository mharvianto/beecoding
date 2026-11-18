namespace BeeCoding.Services.Ai;

/// <summary>
/// In-memory cache of which AI provider/credential to bill — layered the same way as
/// AiRuntimeSettings' pause/quota (a platform-wide DB override on top of appsettings.json,
/// plus an optional override per organization on top of that). Populated at startup and
/// kept in sync by AdminUiController's/OrgAdminController's writes, so resolving the
/// effective config on every AI call (AiTutorService.Effective()) is a couple of in-memory
/// dictionary lookups — no DB round trip.
///
/// Caveat: per-instance cache, same as AdminAccess/AiRuntimeSettings.
/// </summary>
public class AiProviderRuntime
{
    public record Config(string? ApiKey, string? BaseUrl, string? Model, string? GenerateModel)
    {
        public bool IsEmpty => ApiKey is null && BaseUrl is null && Model is null && GenerateModel is null;
    }

    private static readonly Config Nothing = new(null, null, null, null);
    private volatile Config _platform = Nothing;
    private volatile Dictionary<int, Config> _orgs = new();

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static Config Build(string? apiKey, string? baseUrl, string? model, string? generateModel) =>
        new(Norm(apiKey), Norm(baseUrl), Norm(model), Norm(generateModel));

    public void SetPlatform(string? apiKey, string? baseUrl, string? model, string? generateModel) =>
        _platform = Build(apiKey, baseUrl, model, generateModel);

    /// <summary>Null when the platform has no DB override on any field (pure appsettings.json).</summary>
    public Config? Platform => _platform.IsEmpty ? null : _platform;

    public void SetOrgs(IEnumerable<(int OrganizationId, string? ApiKey, string? BaseUrl, string? Model, string? GenerateModel)> rows) =>
        _orgs = rows.ToDictionary(r => r.OrganizationId, r => Build(r.ApiKey, r.BaseUrl, r.Model, r.GenerateModel));

    public void SetOrg(int organizationId, string? apiKey, string? baseUrl, string? model, string? generateModel)
    {
        var next = new Dictionary<int, Config>(_orgs) { [organizationId] = Build(apiKey, baseUrl, model, generateModel) };
        _orgs = next;
    }

    public void ClearOrg(int organizationId)
    {
        if (!_orgs.ContainsKey(organizationId)) return;
        var next = new Dictionary<int, Config>(_orgs);
        next.Remove(organizationId);
        _orgs = next;
    }

    /// <summary>What an admin set for the platform beyond the main provider; null/empty fields fall through to appsettings.</summary>
    public record PlatformExtras(int? MaxTokens, string? ReasoningEffort, List<AiFallback> Fallbacks, string? ReviewModel = null, string? JobEffort = null);
    private volatile PlatformExtras _extras = new(null, null, []);
    public PlatformExtras Extras => _extras;
    public void SetExtras(int? maxTokens, string? reasoningEffort, List<AiFallback> fallbacks, string? reviewModel = null, string? jobEffort = null) =>
        _extras = new PlatformExtras(maxTokens, Norm(reasoningEffort), fallbacks, Norm(reviewModel), Norm(jobEffort));

    // An organization's own tuning and backups, used only while that organization brings its own key (see AiTutorService.OrgOwnsKey).
    private volatile Dictionary<int, PlatformExtras> _orgExtras = new();
    public PlatformExtras? OrgExtras(int organizationId) => _orgExtras.GetValueOrDefault(organizationId);
    public void SetOrgExtras(int organizationId, int? maxTokens, string? reasoningEffort, List<AiFallback> fallbacks) =>
        _orgExtras = new Dictionary<int, PlatformExtras>(_orgExtras) { [organizationId] = new PlatformExtras(maxTokens, Norm(reasoningEffort), fallbacks) };
    public void SetOrgsExtras(IEnumerable<(int OrganizationId, int? MaxTokens, string? ReasoningEffort, List<AiFallback> Fallbacks)> rows) =>
        _orgExtras = rows.ToDictionary(r => r.OrganizationId, r => new PlatformExtras(r.MaxTokens, Norm(r.ReasoningEffort), r.Fallbacks));

    public Config? Org(int organizationId) => _orgs.TryGetValue(organizationId, out var c) && !c.IsEmpty ? c : null;
}
