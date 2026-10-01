namespace BeeCoding.Services;

/// <summary>Builds absolute links for emails. Prefers config <c>App:PublicUrl</c> so a forged Host
/// header can't point the link at another site; falls back to the incoming request.</summary>
public static class PublicUrl
{
    public static string Build(IConfiguration cfg, HttpRequest request, string pathAndQuery)
    {
        var configured = cfg["App:PublicUrl"]?.Trim().TrimEnd('/');
        var baseUrl = string.IsNullOrEmpty(configured)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : configured;
        return baseUrl + pathAndQuery;
    }
}
