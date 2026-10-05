using BeeCoding.Models;
using Microsoft.AspNetCore.Identity;

namespace BeeCoding.Services;

/// <summary>Thin wrapper around ASP.NET's PBKDF2 password hasher.</summary>
public class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        user.HasPassword
        && _hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    /// <summary>Re-authentication for sensitive actions: null when the password checks out, else the message to show.</summary>
    public string? Reauth(User user, string? password) =>
        !user.HasPassword ? "Set a password first (Account → Set password), then try again."
        : Verify(user, password ?? "") ? null : "Password is wrong.";

    /// <summary>A random password nobody knows, for accounts that sign in through Google.</summary>
    public string HashRandom(User user) => Hash(user, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
}
