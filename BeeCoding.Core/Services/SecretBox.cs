namespace BeeCoding.Services;

/// <summary>
/// Encrypts the few secrets the database keeps (authenticator-app secret, AI API keys, the LTI private key) so a copy of the
/// .db file does not hand them over. The web app plugs in ASP.NET Data Protection at startup (<see cref="Configure"/>); this
/// library has no web dependency, so it only sees two delegates. Stored form: <c>enc1:&lt;protected text&gt;</c>.
/// A value without that prefix is read as plain text (rows from before this existed) and is encrypted the next time it is saved.
/// A value that cannot be decrypted (the key ring was lost) reads as null: an AI key or authenticator secret simply stops working
/// instead of breaking every query that loads the row. Not configured (tests of this library, dotnet ef) = stored as is.
/// </summary>
public static class SecretBox
{
    private const string Prefix = "enc1:";
    private static Func<string, string>? _protect;
    private static Func<string, string>? _unprotect;

    public static void Configure(Func<string, string> protect, Func<string, string> unprotect) { _protect = protect; _unprotect = unprotect; }

    public static bool IsSealed(string? stored) => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    public static string? Seal(string? plain) =>
        plain is null || _protect is null || IsSealed(plain) ? plain : Prefix + _protect(plain);

    public static string? Open(string? stored)
    {
        if (stored is null || !IsSealed(stored)) return stored;
        if (_unprotect is null) return null;
        try { return _unprotect(stored[Prefix.Length..]); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
