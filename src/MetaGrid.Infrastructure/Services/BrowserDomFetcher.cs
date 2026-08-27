using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MetaGrid.Infrastructure.Services;

public sealed class BrowserDomFetcher : IBrowserDomFetcher
{
    private static readonly string[] BrowserCandidates =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    ];

    public async Task<IReadOnlyList<BrowserDomFetchResult>> TryFetchAsync(string url, string workingDirectory, CancellationToken cancellationToken)
    {
        var results = new List<BrowserDomFetchResult>();
        Directory.CreateDirectory(workingDirectory);

        foreach (var browserPath in BrowserCandidates)
        {
            if (!File.Exists(browserPath))
            {
                results.Add(new BrowserDomFetchResult(
                    Success: false,
                    BrowserPath: browserPath,
                    BrowserDetected: false,
                    ProcessStarted: false,
                    ExitCode: null,
                    Body: null,
                    StandardError: null,
                    CloudflareDetected: false,
                    FinalUri: null,
                    Details: "Browser executable was not found."));
                continue;
            }

            var profileDirectory = Path.Combine(workingDirectory, $"browser-session-{Guid.NewGuid():N}");
            Directory.CreateDirectory(profileDirectory);

            Process? process = null;
            try
            {
                process = StartBrowser(browserPath, profileDirectory, url);
                if (process is null)
                {
                    results.Add(new BrowserDomFetchResult(
                        Success: false,
                        BrowserPath: browserPath,
                        BrowserDetected: true,
                        ProcessStarted: false,
                        ExitCode: null,
                        Body: null,
                        StandardError: null,
                        CloudflareDetected: false,
                        FinalUri: null,
                        Details: "Process.Start returned null."));
                    continue;
                }

                var devToolsPort = await WaitForDevToolsPortAsync(profileDirectory, cancellationToken);
                if (devToolsPort is null)
                {
                    results.Add(new BrowserDomFetchResult(
                        Success: false,
                        BrowserPath: browserPath,
                        BrowserDetected: true,
                        ProcessStarted: true,
                        ExitCode: process.HasExited ? process.ExitCode : null,
                        Body: null,
                        StandardError: null,
                        CloudflareDetected: false,
                        FinalUri: null,
                        Details: "DevTools endpoint was not published by the browser process."));
                    continue;
                }

                var snapshot = await TryReadDomSnapshotAsync(devToolsPort.Value, url, cancellationToken);
                results.Add(new BrowserDomFetchResult(
                    Success: snapshot.Success,
                    BrowserPath: browserPath,
                    BrowserDetected: true,
                    ProcessStarted: true,
                    ExitCode: process.HasExited ? process.ExitCode : null,
                    Body: snapshot.Html,
                    StandardError: null,
                    CloudflareDetected: snapshot.CloudflareDetected,
                    FinalUri: snapshot.FinalUri,
                    Details: snapshot.Details));

                if (snapshot.Success)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new BrowserDomFetchResult(
                    Success: false,
                    BrowserPath: browserPath,
                    BrowserDetected: true,
                    ProcessStarted: process is not null,
                    ExitCode: process?.HasExited == true ? process.ExitCode : null,
                    Body: null,
                    StandardError: ex.Message,
                    CloudflareDetected: false,
                    FinalUri: null,
                    Details: $"Browser-backed DOM extraction failed: {ex.Message}"));
            }
            finally
            {
                if (process is not null)
                {
                    await TryStopProcessAsync(process);
                }

                TryDeleteDirectory(profileDirectory);
            }
        }

        return results;
    }

    private static Process? StartBrowser(string browserPath, string profileDirectory, string url)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = browserPath,
            Arguments = string.Join(" ", new[]
            {
                "--remote-debugging-port=0",
                "--no-first-run",
                "--disable-gpu",
                "--disable-crash-reporter",
                "--window-size=1400,1000",
                "--start-minimized",
                $"--user-data-dir=\"{profileDirectory}\"",
                $"\"{url}\""
            }),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized
        };

        return Process.Start(startInfo);
    }

    private static async Task<int?> WaitForDevToolsPortAsync(string profileDirectory, CancellationToken cancellationToken)
    {
        var activePortPath = Path.Combine(profileDirectory, "DevToolsActivePort");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(activePortPath))
            {
                var lines = await File.ReadAllLinesAsync(activePortPath, cancellationToken);
                if (lines.Length > 0 && int.TryParse(lines[0], out var port))
                {
                    return port;
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        return null;
    }

    private static async Task<BrowserSnapshot> TryReadDomSnapshotAsync(int port, string url, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        var target = await FindPageTargetAsync(httpClient, port, url, cancellationToken);
        if (target?.WebSocketDebuggerUrl is null)
        {
            return BrowserSnapshot.Failure("No page target was exposed by the browser session.");
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), cancellationToken);
        var session = new DevToolsSession(socket);
        await session.SendAsync("Page.enable", null, cancellationToken);
        await session.SendAsync("Runtime.enable", null, cancellationToken);

        BrowserSnapshot? lastCloudflareSnapshot = null;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await EvaluateSnapshotAsync(session, cancellationToken);
            if (snapshot.Success)
            {
                return snapshot;
            }

            if (snapshot.CloudflareDetected)
            {
                lastCloudflareSnapshot = snapshot;
            }

            await Task.Delay(1000, cancellationToken);
        }

        return lastCloudflareSnapshot ?? BrowserSnapshot.Failure("Browser session did not reach usable D2PT hero-grid DOM before the timeout.");
    }

    private static async Task<DevToolsTarget?> FindPageTargetAsync(HttpClient httpClient, int port, string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var targets = await httpClient.GetFromJsonAsync<List<DevToolsTarget>>($"http://127.0.0.1:{port}/json/list", cancellationToken);
                var pageTarget = targets?
                    .Where(target => string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(target => string.Equals(target.Url, url, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(target => target.Url?.Contains("dota2protracker.com", StringComparison.OrdinalIgnoreCase) == true)
                    .FirstOrDefault();
                if (pageTarget?.WebSocketDebuggerUrl is not null)
                {
                    return pageTarget;
                }
            }
            catch
            {
                // Keep polling until the browser publishes its page target.
            }

            await Task.Delay(500, cancellationToken);
        }

        return null;
    }

    private static async Task<BrowserSnapshot> EvaluateSnapshotAsync(DevToolsSession session, CancellationToken cancellationToken)
    {
        const string script = """
(() => {
  const html = document.documentElement?.outerHTML ?? "";
  const bodyText = document.body?.innerText ?? "";
  return {
    title: document.title ?? "",
    href: location.href ?? "",
    readyState: document.readyState ?? "",
    html,
    bodyText
  };
})()
""";

        var response = await session.SendAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true
        }, cancellationToken);

        var payload = response.GetProperty("result").GetProperty("value");
        var html = payload.GetProperty("html").GetString() ?? string.Empty;
        var bodyText = payload.GetProperty("bodyText").GetString() ?? string.Empty;
        var title = payload.GetProperty("title").GetString() ?? string.Empty;
        var finalUri = payload.GetProperty("href").GetString();

        if (IsCloudflarePage(title, bodyText, html))
        {
            return new BrowserSnapshot(
                Success: false,
                CloudflareDetected: true,
                Html: html,
                FinalUri: finalUri,
                Details: "Browser session reached a Cloudflare challenge page instead of the hero-grid DOM.");
        }

        if (LooksLikeHeroGridPage(title, bodyText, html))
        {
            return new BrowserSnapshot(
                Success: true,
                CloudflareDetected: false,
                Html: html,
                FinalUri: finalUri,
                Details: "Browser-backed DOM extraction succeeded.");
        }

        return BrowserSnapshot.Failure("Browser session has not reached the expected D2PT hero-grid content yet.", finalUri);
    }

    private static bool LooksLikeHeroGridPage(string title, string bodyText, string html)
    {
        return title.Contains("Dota2ProTracker Meta Hero Grids", StringComparison.OrdinalIgnoreCase) &&
               (bodyText.Contains("High Winrate", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Download Hero Grid Configuration", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Most played heroes with >50% winrate", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCloudflarePage(string title, string bodyText, string html)
    {
        return title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
               bodyText.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) ||
               bodyText.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("cf-turnstile-response", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) &&
               html.Contains("challenge", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryStopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Ignore browser shutdown errors during cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures for browser temp profiles.
        }
    }

    private sealed record BrowserSnapshot(bool Success, bool CloudflareDetected, string? Html, string? FinalUri, string Details)
    {
        public static BrowserSnapshot Failure(string details, string? finalUri = null)
            => new(false, false, null, finalUri, details);
    }

    private sealed class DevToolsSession(ClientWebSocket socket)
    {
        private int _messageId;

        public async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var messageId = Interlocked.Increment(ref _messageId);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = messageId,
                method,
                @params = parameters
            });

            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);

            while (true)
            {
                using var document = await ReceiveDocumentAsync(cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.GetInt32() == messageId)
                {
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        throw new InvalidOperationException($"DevTools returned an error: {errorElement}");
                    }

                    return root.GetProperty("result").Clone();
                }
            }
        }

        private async Task<JsonDocument> ReceiveDocumentAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Browser DevTools socket was closed before a DOM response was returned.");
                }

                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            stream.Position = 0;
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private sealed class DevToolsTarget
    {
        public string? Type { get; init; }
        public string? Url { get; init; }
        public string? WebSocketDebuggerUrl { get; init; }
    }
}
