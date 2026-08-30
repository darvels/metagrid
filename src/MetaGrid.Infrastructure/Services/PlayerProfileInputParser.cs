using System.Globalization;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class PlayerProfileInputParser : IPlayerProfileInputParser
{
    public ProfileInputParseResult Parse(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return ProfileInputParseResult.Failure("Enter an OpenDota account ID or a supported player URL.");
        }

        if (TryParseNumericAccountId(value, out var numericId))
        {
            return ProfileInputParseResult.Success(numericId);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return ProfileInputParseResult.Failure("The player profile format is not recognized.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileInputParseResult.Failure("Only HTTP or HTTPS player URLs are supported.");
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if ((host == "opendota.com" || host == "www.opendota.com")
            && segments.Length >= 2
            && string.Equals(segments[0], "players", StringComparison.OrdinalIgnoreCase)
            && TryParseNumericAccountId(segments[1], out numericId))
        {
            return ProfileInputParseResult.Success(numericId);
        }

        if ((host == "dotabuff.com" || host == "www.dotabuff.com")
            && segments.Length >= 2
            && string.Equals(segments[0], "players", StringComparison.OrdinalIgnoreCase)
            && TryParseNumericAccountId(segments[1], out numericId))
        {
            return ProfileInputParseResult.Success(numericId);
        }

        return ProfileInputParseResult.Failure("MetaGrid supports only numeric account IDs, OpenDota player URLs, and Dotabuff player URLs.");
    }

    private static bool TryParseNumericAccountId(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId))
        {
            return false;
        }

        if (accountId <= 0)
        {
            return false;
        }

        normalized = accountId.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}

