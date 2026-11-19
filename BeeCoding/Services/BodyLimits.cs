namespace BeeCoding.Services;

/// <summary>Request-size ceilings. The server-wide default (<c>Kestrel:MaxRequestBodyMb</c>, 32 MB) is a backstop for
/// ordinary endpoints; the admin bulk-import endpoints may carry a whole problem bank (statements plus every test
/// case), so they accept up to this much. Keep nginx's <c>client_max_body_size</c> for <c>/api/admin</c> and
/// <c>/api/admin-ui</c> at least this large.</summary>
public static class BodyLimits
{
    /// <summary>A submission, run or AI hint carries at most 200 000 characters of code (checked in each action, after the body is
    /// read) plus up to 256 000 of stdin; JSON escaping can make a character 6 bytes. This keeps a huge body out of memory first.</summary>
    public const long CodeBodyBytes = 2L * 1024 * 1024;
    public const long AdminImportBytes = 100L * 1024 * 1024;
}
