using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;

return await MetaGridUpdaterProgram.RunAsync(args);

internal static class MetaGridUpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var request = ParseArgs(args);
            var engine = new AppUpdateInstallerEngine();
            var result = await engine.ExecuteAsync(request, CancellationToken.None);
            if (result.Success)
            {
                return 0;
            }

            Console.Error.WriteLine(result.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static AppUpdateInstallRequest ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MetaGrid updater arguments were invalid.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        return new AppUpdateInstallRequest(
            GetRequired(values, "install-dir"),
            GetRequired(values, "package-dir"),
            GetRequired(values, "exe-name"),
            int.TryParse(GetRequired(values, "parent-pid"), out var parentPid) ? parentPid : throw new InvalidOperationException("MetaGrid updater parent PID was invalid."),
            GetRequired(values, "backup-dir"),
            GetRequired(values, "current-version"),
            GetRequired(values, "target-version"),
            GetOptionalBool(values, "skip-relaunch"),
            GetOptional(values, "log-path"),
            GetOptional(values, "simulate-failure"));
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"MetaGrid updater argument '--{key}' was missing.");

    private static string? GetOptional(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool GetOptionalBool(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"MetaGrid updater argument '--{key}' was invalid.");
    }
}
