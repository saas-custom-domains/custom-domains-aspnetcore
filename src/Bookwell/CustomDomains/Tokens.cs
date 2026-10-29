using System.Security.Cryptography;
using System.Text;

namespace Bookwell.CustomDomains;

public static class Tokens
{
    // Constant-time comparison, so a wrong guess doesn't reveal how
    // many leading characters were right. An unset expected token
    // never matches.
    public static bool Matches(string? given, string? expected) =>
        !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given ?? ""),
            Encoding.UTF8.GetBytes(expected));
}
