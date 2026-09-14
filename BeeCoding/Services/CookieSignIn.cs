using System.Security.Claims;
using BeeCoding.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace BeeCoding.Services;

/// <summary>Issues/clears the cookie-auth session — shared by AuthController (password
/// login/logout), LtiController (LTI launch), and Program.cs's OnValidatePrincipal (forced
/// sign-out of a deleted/changed user), so every path stays consistent.</summary>
public static class CookieSignIn
{
    private const string CookieName = "beecoding.auth";   // must match Program.cs's o.Cookie.Name

    public static Task SignInAsync(HttpContext ctx, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>Signs out AND clears a stale Path=/ cookie left over from before this
    /// deployment started using a PathBase (see Program.cs) — CookieBuilder defaults Path to
    /// PathBase, so every cookie issued before one was configured sits at Path=/ forever.
    /// Without this, that old Path=/ cookie keeps riding along in the Cookie header after
    /// "logout" — if it hasn't expired yet, it silently re-authenticates the user on the
    /// very next request, which looks exactly like sign-out not working.
    ///
    /// Deletes BOTH paths explicitly ourselves rather than relying solely on the framework's
    /// SignOutAsync: when the client sends two same-named cookies (one per path) at once,
    /// ASP.NET Core's cookie parsing/authentication can end up confused enough that
    /// SignOutAsync's own cookie handler silently writes nothing at all for the current
    /// PathBase path (observed directly). Also can't use HttpResponse.Cookies.Delete() twice
    /// for the same cookie NAME even with different Path values — it replaces rather than
    /// adds a second Set-Cookie header (also observed directly). Appending a hand-built
    /// Set-Cookie header instead sidesteps both quirks.</summary>
    public static async Task SignOutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var pathBase = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value! : "/";
        AppendExpiredCookie(ctx, pathBase);
        if (pathBase != "/") AppendExpiredCookie(ctx, "/");
    }

    private static void AppendExpiredCookie(HttpContext ctx, string path)
    {
        var header = new SetCookieHeaderValue(CookieName, string.Empty)
        {
            Path = path,
            Expires = DateTimeOffset.UnixEpoch,
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = Microsoft.Net.Http.Headers.SameSiteMode.Lax,
        }.ToString();
        ctx.Response.Headers.Append(HeaderNames.SetCookie, header);
    }
}
