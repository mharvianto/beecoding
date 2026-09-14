using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Lti;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// LTI 1.3 tool endpoints — this app acting as a Tool that an LMS (the Platform) launches
/// into. Three anonymous, browser-facing steps (login -> launch -> jwks) implement the
/// OIDC third-party-initiated login core spec; the two authenticated deep-link/* actions
/// back the Deep Linking picker SPA page shown mid-launch when a teacher is adding this
/// tool as an activity in their course.
/// </summary>
[Route("lti")]
public class LtiController(
    AppDbContext db, LtiLoginStateStore stateStore, LtiLaunchValidator validator,
    LtiProvisioningService provisioning, LtiToolKeyService toolKeys, LtiDeepLinkService deepLink)
    : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly LtiLoginStateStore _stateStore = stateStore;
    private readonly LtiLaunchValidator _validator = validator;
    private readonly LtiProvisioningService _provisioning = provisioning;
    private readonly LtiToolKeyService _toolKeys = toolKeys;
    private readonly LtiDeepLinkService _deepLink = deepLink;

    private string? Param(string key) =>
        (Request.HasFormContentType ? Request.Form[key].FirstOrDefault() : null) ?? Request.Query[key].FirstOrDefault();

    private string AbsoluteUrl(string relativePath) =>
        $"{Request.Scheme}://{Request.Host}{Url.Content("~" + relativePath)}";

    /// <summary>Step 1 — OIDC third-party-initiated login. The platform redirects (or
    /// form-posts) the browser here first; we bounce it on to the platform's own auth
    /// endpoint with a nonce/state we'll check back at Launch.</summary>
    [HttpGet("login")]
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        var iss = Param("iss");
        var loginHint = Param("login_hint");
        var targetLinkUri = Param("target_link_uri");
        var clientId = Param("client_id");
        var messageHint = Param("lti_message_hint");
        var deploymentIdHint = Param("lti_deployment_id");

        if (string.IsNullOrEmpty(iss) || string.IsNullOrEmpty(loginHint) || string.IsNullOrEmpty(targetLinkUri))
            return BadRequest("Missing required LTI login parameters (iss / login_hint / target_link_uri).");

        var query = _db.LtiPlatforms.Where(p => p.Issuer == iss && p.Enabled);
        if (!string.IsNullOrEmpty(clientId)) query = query.Where(p => p.ClientId == clientId);
        var platform = await query.FirstOrDefaultAsync();
        if (platform is null) return BadRequest($"No registered LTI platform for issuer '{iss}'.");

        var nonce = Guid.NewGuid().ToString("N");
        var state = _stateStore.StartLogin(platform.Id, nonce, targetLinkUri);

        var qs = new Dictionary<string, string?>
        {
            ["scope"] = "openid",
            ["response_type"] = "id_token",
            ["client_id"] = platform.ClientId,
            ["redirect_uri"] = AbsoluteUrl("/lti/launch"),
            ["login_hint"] = loginHint,
            ["state"] = state,
            ["response_mode"] = "form_post",
            ["nonce"] = nonce,
            ["prompt"] = "none",
        };
        if (!string.IsNullOrEmpty(messageHint)) qs["lti_message_hint"] = messageHint;
        if (!string.IsNullOrEmpty(deploymentIdHint)) qs["lti_deployment_id"] = deploymentIdHint;

        return Redirect(QueryHelpers.AddQueryString(platform.AuthLoginUrl, qs));
    }

    /// <summary>Step 2 — the platform form-posts the signed id_token back here. Validate
    /// it, sign the user into BeeCoding's own session, and either land them on the board
    /// (resource-link launch) or hand off to the Deep Linking picker.</summary>
    [HttpPost("launch")]
    [AllowAnonymous]
    public async Task<IActionResult> Launch()
    {
        if (!Request.HasFormContentType) return BadRequest("Expected a form_post launch.");
        var idToken = Request.Form["id_token"].FirstOrDefault();
        var state = Request.Form["state"].FirstOrDefault();
        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(state))
            return BadRequest("Missing id_token/state.");

        var pending = _stateStore.ConsumeLogin(state);
        if (pending is null) return BadRequest("This login has expired or was already used — launch again from the LMS.");

        var platform = await _db.LtiPlatforms.FindAsync(pending.PlatformId);
        if (platform is null || !platform.Enabled) return BadRequest("This platform is no longer registered.");

        var (jwt, error) = await _validator.ValidateAsync(idToken, platform, pending.Nonce);
        if (jwt is null) return BadRequest($"LTI launch rejected: {error}");

        var claims = LtiLaunchClaims.Parse(jwt);
        var deploymentIds = platform.DeploymentIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!deploymentIds.Contains(claims.DeploymentId))
            return BadRequest($"Unrecognized deployment_id '{claims.DeploymentId}' for this platform registration.");

        var user = await _provisioning.FindOrCreateUserAsync(platform, claims);
        await CookieSignIn.SignInAsync(HttpContext, user);

        if (claims.MessageType == LtiClaims.MessageTypeDeepLinking)
        {
            if (string.IsNullOrEmpty(claims.DeepLinkReturnUrl))
                return BadRequest("Deep Linking request is missing its return URL.");
            var token = _stateStore.StageDeepLink(new LtiDeepLinkContext(
                platform.Id, platform.Name, claims.DeploymentId, claims.DeepLinkReturnUrl, claims.DeepLinkData, default));
            return Redirect($"{Url.Content("~/lti/deep-link")}?token={Uri.EscapeDataString(token)}");
        }

        if (claims.MessageType != LtiClaims.MessageTypeResourceLink)
            return BadRequest($"Unsupported LTI message_type '{claims.MessageType}'.");

        var isInstructor = LtiClaims.IsInstructor(claims.Roles);
        var (board, bankProblem) = await _provisioning.ResolveResourceLinkAsync(platform, claims, user, isInstructor);

        if (bankProblem is not null)
            return Redirect(Url.Content($"~/practice/{bankProblem.Slug}"));

        if (board is null)
            return Content("This activity hasn't been opened by your instructor yet — ask them to launch it from the course first.");

        await _provisioning.EnsureMembershipAsync(board, user, isInstructor);
        return Redirect(Url.Content($"~/boards/{board.Slug}"));
    }

    /// <summary>Our public keyset — give this URL to every platform when registering
    /// BeeCoding as a tool. Verifies Deep Linking responses and the client-assertion JWT
    /// used to fetch an AGS access token.</summary>
    [HttpGet("jwks")]
    [AllowAnonymous]
    public async Task<IActionResult> Jwks() => Ok(await _toolKeys.GetPublicJwksAsync());

    [HttpGet("deep-link/context")]
    [Authorize]
    public ActionResult<LtiDeepLinkContextDto> DeepLinkContext([FromQuery] string token)
    {
        var ctx = _stateStore.GetDeepLink(token);
        if (ctx is null) return NotFound("This Deep Linking session has expired — go back to the LMS and add the activity again.");
        return new LtiDeepLinkContextDto(ctx.PlatformName, true);
    }

    /// <summary>The teacher picked a board or a single bank problem in the SPA picker —
    /// build the signed Deep Linking response JWT; the SPA itself does the actual
    /// form-post back to the LMS.</summary>
    [HttpPost("deep-link/select")]
    [Authorize]
    public async Task<ActionResult<LtiDeepLinkResultDto>> DeepLinkSelect(LtiDeepLinkSelectDto dto)
    {
        var ctx = _stateStore.GetDeepLink(dto.Token);
        if (ctx is null) return NotFound("This Deep Linking session has expired — go back to the LMS and add the activity again.");

        var platform = await _db.LtiPlatforms.FindAsync(ctx.PlatformId);
        if (platform is null) return NotFound("Platform no longer registered.");

        string title, targetLinkUri;
        if (!string.IsNullOrEmpty(dto.BankProblemSlug))
        {
            var problem = await _db.BankProblems.FirstOrDefaultAsync(b => b.Slug == dto.BankProblemSlug);
            if (problem is null) return NotFound("Problem not found.");
            if (problem.OwnerId != UserId && !problem.IsPublic) return Forbid();
            title = problem.Title;
            // ?practice=<slug> rides along on every future launch of the placement the
            // platform is about to create, so its very first real launch attaches to
            // this exact problem instead of falling through to the board flow — see
            // LtiProvisioningService.ResolveResourceLinkAsync.
            targetLinkUri = QueryHelpers.AddQueryString(AbsoluteUrl("/lti/launch"), "practice", problem.Slug);
        }
        else if (!string.IsNullOrEmpty(dto.BoardSlug))
        {
            var board = await _db.Boards.FirstOrDefaultAsync(b => b.Slug == dto.BoardSlug);
            if (board is null) return NotFound("Board not found.");
            if (board.OwnerId != UserId) return Forbid();
            title = board.Title;
            targetLinkUri = QueryHelpers.AddQueryString(AbsoluteUrl("/lti/launch"), "board", board.Slug);
        }
        else return BadRequest("Pick a board or a problem first.");

        var jwt = await _deepLink.BuildResponseJwtAsync(platform, ctx.DeploymentId, ctx.Data, title, targetLinkUri);
        return new LtiDeepLinkResultDto(ctx.ReturnUrl, jwt);
    }
}
