using System.Security.Claims;
using BeeCoding.Services;
using Microsoft.AspNetCore.Mvc;

namespace BeeCoding.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected int UserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("no user id claim"));

    protected string CurrentRole => User.FindFirstValue(ClaimTypes.Role) ?? "Student";

    /// <summary>The caller's email — for audit-log attribution.</summary>
    protected string ActorEmail => User.FindFirst(ClaimTypes.Email)?.Value ?? "";

    /// <summary>Is the caller an admin (Admin:Emails), independent of their Teacher/Student
    /// role? No base-class DI here, so pass the controller's own injected instance.</summary>
    protected bool IsAdminUser(AdminAccess admin) => admin.HasAdminPowers(User.FindFirst(ClaimTypes.Email)?.Value);
}
