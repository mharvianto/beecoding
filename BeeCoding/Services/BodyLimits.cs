namespace BeeCoding.Services;

/// <summary>Request-size ceilings. The server-wide default (<c>Kestrel:MaxRequestBodyMb</c>, 32 MB) is a backstop for
/// ordinary endpoints; the admin bulk-import endpoints may carry a whole problem bank (statements plus every test
/// case), so they accept up to this much. Keep nginx's <c>client_max_body_size</c> for <c>/api/admin</c> and
/// <c>/api/admin-ui</c> at least this large.</summary>
public static class BodyLimits
{
    public const long AdminImportBytes = 100L * 1024 * 1024;
}
