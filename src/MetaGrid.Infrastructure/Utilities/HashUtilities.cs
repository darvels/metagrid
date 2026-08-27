using System.Security.Cryptography;
using System.Text;

namespace MetaGrid.Infrastructure.Utilities;

internal static class HashUtilities
{
    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
