using System.Security.Cryptography;
using System.Text;

namespace AfterTheCredits.Services;

internal static class DemoAccessPassword
{
    internal const string ConfigurationKey = "DemoAccess";
    internal const string VersionClaim = "demo_access_version";

    internal static string? GetVersion(IConfiguration configuration)
    {
        var password = configuration[ConfigurationKey];
        return string.IsNullOrWhiteSpace(password) ? null : Hash(password);
    }

    internal static bool Matches(string? password, string? version)
    {
        return !string.IsNullOrEmpty(password) &&
            version is not null &&
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Hash(password)), Convert.FromHexString(version));
    }

    private static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}
