using System;
using System.Security.Cryptography;
using System.Text;

namespace KioskApp;

/// <summary>
/// Security utilities for password hashing and validation.
/// Uses SHA256 for password hashing as specified.
/// </summary>
public static class SecurityHelper
{
    /// <summary>
    /// Hashes a password using SHA256.
    /// </summary>
    /// <param name="password">Plain text password to hash</param>
    /// <returns>Hexadecimal string representation of the hash</returns>
    public static string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Validates a password against a stored hash using constant-time comparison.
    /// </summary>
    /// <param name="password">Plain text password to validate</param>
    /// <param name="storedHash">Stored hash to compare against</param>
    /// <returns>True if password matches, false otherwise</returns>
    public static bool ValidatePassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var inputHash = HashPassword(password);

        // Use constant-time comparison to prevent timing attacks
        return ConstantTimeEquals(inputHash, storedHash);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }

    /// <summary>
    /// Generates a default password hash for initial setup.
    /// Default password: "admin123" (should be changed in production)
    /// </summary>
    public static string GetDefaultPasswordHash()
    {
        return HashPassword("admin123");
    }
}
