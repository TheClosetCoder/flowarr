using System.Security.Cryptography;
using Flowarr.Core.Entities;
using Flowarr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Infrastructure.Services;

public interface IAdminAuthService
{
    Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default);
    Task<bool> CreateAdminAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<bool> ValidateAsync(string username, string password, CancellationToken cancellationToken = default);
}

public sealed class AdminAuthService(FlowarrDbContext dbContext) : IAdminAuthService
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.AdminUsers.AnyAsync(cancellationToken);
    }

    public async Task<bool> CreateAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (await IsSetupCompleteAsync(cancellationToken) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            password.Length < 12)
        {
            return false;
        }

        dbContext.AdminUsers.Add(new AdminUser
        {
            Username = username.Trim(),
            PasswordHash = HashPassword(password)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var admin = await dbContext.AdminUsers.FirstOrDefaultAsync(
            user => user.Username == username.Trim(),
            cancellationToken);
        return admin is not null && VerifyPassword(password, admin.PasswordHash);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
