using System.Security.Cryptography;
using System.Text;
using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>Which role a self-service sign-up may get. Shared by password registration and Google sign-up.</summary>
public static class SignupRole
{
    /// <summary>Self-service sign-up only creates Students. A Teacher needs the shared invite code
    /// (<c>Auth:TeacherSignupCode</c>); with that config unset, teacher self-signup is off entirely.</summary>
    public static (UserRole? Role, string? Error) Resolve(IConfiguration cfg, string? requested, string? teacherCode)
    {
        if (requested?.Equals("Teacher", StringComparison.OrdinalIgnoreCase) != true) return (UserRole.Student, null);
        var code = cfg["Auth:TeacherSignupCode"];
        if (string.IsNullOrEmpty(code)) return (null, "Teacher self-registration is disabled on this server.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(teacherCode ?? ""), Encoding.UTF8.GetBytes(code)))
            return (null, "Invalid teacher code.");
        return (UserRole.Teacher, null);
    }
}
