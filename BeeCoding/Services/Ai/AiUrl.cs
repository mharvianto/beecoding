using System.Net;

namespace BeeCoding.Services.Ai;

/// <summary>Checks an AI endpoint URL typed into a settings form. The server calls it with an API key
/// attached, so an org admin must not be able to aim it at internal addresses (SSRF).</summary>
public static class AiUrl
{
    /// <summary>Null when fine (or blank = "use the next layer"), else the message to show.</summary>
    public static string? Validate(string? url, bool strict)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
            return "Base URL must be a full http(s) address.";
        if (!strict) return null;   // platform admin: trusted, may point at a local model server
        if (u.Scheme != "https") return "Base URL must use https.";
        // shortcut: literal-IP/hostname check only, a public name that resolves to a private IP still passes — pin the resolved IP if org admins become untrusted.
        if (u.IsLoopback || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || !u.Host.Contains('.') && !IPAddress.TryParse(u.Host, out _))
            return "Base URL must be a public address.";
        if (IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) && IsPrivate(ip))
            return "Base URL must be a public address.";
        return null;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xFE) == 0xFC;   // fc00::/7
        return b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}
