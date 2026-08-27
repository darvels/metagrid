using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;
using MetaGrid.Infrastructure.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MetaGrid.UI.Services;

public sealed class D2ptOfficialGridRetrievalService(
    IAppPaths appPaths,
    ILoggingService loggingService) : ID2ptOfficialGridRetrievalService
{
    private static readonly Uri TargetUri = new("https://dota2protracker.com/meta-hero-grids");
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PageReadinessTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _singleFlight = new(1, 1);

    public async Task<D2ptOfficialGridRetrievalResult> RetrieveOfficialHighWinrateAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _singleFlight.WaitAsync(cancellationToken);
        var startedAt = Stopwatch.StartNew();
        var attemptDirectory = Path.Combine(appPaths.D2ptTempDirectory, $"retrieval-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(attemptDirectory);

        try
        {
            await loggingService.LogAsync(LogLevelKind.Information, "Official D2PT retrieval started.", new
            {
                forceRefresh,
                appPaths.D2ptWebView2ProfileDirectory,
                attemptDirectory
            }, cancellationToken);

            using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallTimeout.CancelAfter(OverallTimeout);

            var result = await HiddenD2ptWebViewHost.RunAsync(
                TargetUri.ToString(),
                appPaths.D2ptWebView2ProfileDirectory,
                attemptDirectory,
                overallTimeout.Token);

            await loggingService.LogAsync(
                result.Succeeded ? LogLevelKind.Information : LogLevelKind.Warning,
                "Official D2PT retrieval finished.",
                new
                {
                    result.Succeeded,
                    result.ProviderStatus,
                    result.Classification,
                    result.Message,
                    result.RuntimeVersion,
                    result.FinalUrl,
                    result.PageLoaded,
                    result.CloudflareChallenge,
                    result.HighWinrateButtonFound,
                    result.OfficialDownloadTriggered,
                    result.DownloadCompleted,
                    result.InitMs,
                    result.NavigationMs,
                    result.DownloadMs,
                    result.ElapsedMs,
                    result.SuggestedFilename
                },
                cancellationToken);

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return D2ptOfficialGridRetrievalResult.Failure(ProviderStatus.Unavailable, "RetrievalTimeout", "The official D2PT retrieval exceeded its bounded timeout.");
        }
        finally
        {
            startedAt.Stop();
            TryDeleteDirectory(attemptDirectory);
            _singleFlight.Release();
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
            // Best-effort temp cleanup only.
        }
    }

    private static class HiddenD2ptWebViewHost
    {
        public static Task<D2ptOfficialGridRetrievalResult> RunAsync(
            string url,
            string userDataFolder,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<D2ptOfficialGridRetrievalResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                using var form = new ProbeForm(url, userDataFolder, workingDirectory, tcs, cancellationToken);
                Application.Run(form);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }

        private sealed class ProbeForm : Form
        {
            private readonly string _url;
            private readonly string _userDataFolder;
            private readonly string _workingDirectory;
            private readonly TaskCompletionSource<D2ptOfficialGridRetrievalResult> _tcs;
            private readonly CancellationToken _cancellationToken;
            private readonly WebView2 _webView;
            private long _initMs;
            private long _navigationMs;
            private long _downloadMs;
            private string? _runtimeVersion;
            private string? _finalUrl;
            private string? _title;
            private bool _pageLoaded;
            private bool _cloudflareChallenge;
            private bool _highWinrateButtonFound;
            private bool _officialDownloadTriggered;
            private bool _downloadCompleted;
            private string? _suggestedFilename;
            private string? _mimeType;
            private string? _rawJson;
            private string? _tempFilePath;

            public ProbeForm(
                string url,
                string userDataFolder,
                string workingDirectory,
                TaskCompletionSource<D2ptOfficialGridRetrievalResult> tcs,
                CancellationToken cancellationToken)
            {
                _url = url;
                _userDataFolder = userDataFolder;
                _workingDirectory = workingDirectory;
                _tcs = tcs;
                _cancellationToken = cancellationToken;

                var bounds = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.VirtualScreen;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new System.Drawing.Point(Math.Max(bounds.Left, bounds.Right - 36), Math.Max(bounds.Top, bounds.Bottom - 36));
                Size = new System.Drawing.Size(24, 24);
                Opacity = 0.01d;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowIcon = false;
                ControlBox = false;

                _webView = new WebView2 { Dock = DockStyle.Fill };
                Controls.Add(_webView);
                Shown += OnShown;
            }

            protected override bool ShowWithoutActivation => true;

            private async void OnShown(object? sender, EventArgs e)
            {
                try
                {
                    var result = await ExecuteAsync();
                    _tcs.TrySetResult(result);
                }
                catch (WebView2RuntimeNotFoundException ex)
                {
                    _tcs.TrySetResult(D2ptOfficialGridRetrievalResult.Failure(ProviderStatus.Unavailable, "WebView2RuntimeMissing", ex.Message));
                }
                catch (OperationCanceledException)
                {
                    _tcs.TrySetResult(D2ptOfficialGridRetrievalResult.Failure(ProviderStatus.Unavailable, "RetrievalCancelled", "The official D2PT retrieval was cancelled."));
                }
                catch (Exception ex)
                {
                    _tcs.TrySetResult(D2ptOfficialGridRetrievalResult.Failure(ProviderStatus.Unavailable, "WebView2InitFailed", ex.Message));
                }
                finally
                {
                    Controls.Remove(_webView);
                    _webView.Dispose();
                    Close();
                }
            }

            private async Task<D2ptOfficialGridRetrievalResult> ExecuteAsync()
            {
                var overall = Stopwatch.StartNew();
                var phase = Stopwatch.StartNew();

                Directory.CreateDirectory(_userDataFolder);
                Directory.CreateDirectory(_workingDirectory);

                using var initTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                initTimeout.CancelAfter(InitializationTimeout);
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: _userDataFolder,
                    options: new CoreWebView2EnvironmentOptions("--disable-sync --no-first-run"));
                _initMs = phase.ElapsedMilliseconds;
                _runtimeVersion = environment.BrowserVersionString;

                await _webView.EnsureCoreWebView2Async(environment).WaitAsync(initTimeout.Token);
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var downloadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void NavigationCompleted(object? _, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (args.IsSuccess)
                    {
                        readyTcs.TrySetResult(true);
                    }
                    else
                    {
                        readyTcs.TrySetException(new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}"));
                    }
                }

                void DomContentLoaded(object? _, CoreWebView2DOMContentLoadedEventArgs args)
                    => readyTcs.TrySetResult(true);

                _webView.CoreWebView2.SourceChanged += (_, __) => _finalUrl = _webView.Source?.ToString();
                _webView.CoreWebView2.NavigationCompleted += NavigationCompleted;
                _webView.CoreWebView2.DOMContentLoaded += DomContentLoaded;
                _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;

                try
                {
                    phase.Restart();
                    _webView.CoreWebView2.Navigate(_url);

                    using var readinessTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                    readinessTimeout.CancelAfter(PageReadinessTimeout);
                    await readyTcs.Task.WaitAsync(readinessTimeout.Token);
                    _navigationMs = phase.ElapsedMilliseconds;

                    await Task.Delay(1200, _cancellationToken);
                    var snapshot = await ReadSnapshotAsync();
                    _finalUrl = snapshot.Href;
                    _title = snapshot.Title;
                    _pageLoaded = LooksLikeTargetPage(snapshot.Title, snapshot.BodyText, snapshot.Html);
                    _cloudflareChallenge = D2ptPageDataExtractor.LooksLikeCloudflareChallenge(snapshot.Html);

                    if (_cloudflareChallenge)
                    {
                        return CompleteFailure(ProviderStatus.CloudflareBlocked, "CloudflareChallenge", "Dota2ProTracker presented a Cloudflare challenge instead of the real meta hero grids page.", overall.ElapsedMilliseconds);
                    }

                    if (!_pageLoaded || string.IsNullOrWhiteSpace(_finalUrl) || !_finalUrl.Contains("meta-hero-grids", StringComparison.OrdinalIgnoreCase))
                    {
                        return CompleteFailure(ProviderStatus.UnexpectedResponse, "PageReadinessTimeout", "MetaGrid did not reach the real Dota2ProTracker meta hero grids page before timeout.", overall.ElapsedMilliseconds);
                    }

                    var clickResult = await IdentifyAndClickHighWinrateAsync();
                    _highWinrateButtonFound = clickResult.ButtonFound;
                    _officialDownloadTriggered = clickResult.ClickPerformed;

                    if (!_highWinrateButtonFound)
                    {
                        return CompleteFailure(ProviderStatus.UnexpectedResponse, "HighWinrateButtonNotFound", clickResult.Details, overall.ElapsedMilliseconds);
                    }

                    if (!_officialDownloadTriggered)
                    {
                        return CompleteFailure(ProviderStatus.UnexpectedResponse, "OfficialDownloadNotTriggered", clickResult.Details, overall.ElapsedMilliseconds);
                    }

                    phase.Restart();
                    using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                    downloadTimeout.CancelAfter(DownloadTimeout);
                    using var registration = downloadTimeout.Token.Register(() => downloadTcs.TrySetCanceled(downloadTimeout.Token));

                    try
                    {
                        await downloadTcs.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        return CompleteFailure(ProviderStatus.UnexpectedResponse, "DownloadFailed", "The official High Winrate download did not complete before timeout.", overall.ElapsedMilliseconds);
                    }

                    _downloadMs = phase.ElapsedMilliseconds;
                    if (!_downloadCompleted || string.IsNullOrWhiteSpace(_tempFilePath) || !File.Exists(_tempFilePath))
                    {
                        return CompleteFailure(ProviderStatus.UnexpectedResponse, "DownloadFailed", "The official D2PT download event fired, but no completed payload file was captured.", overall.ElapsedMilliseconds);
                    }

                    _rawJson = await File.ReadAllTextAsync(_tempFilePath, _cancellationToken);
                    return new D2ptOfficialGridRetrievalResult
                    {
                        Succeeded = true,
                        ProviderStatus = ProviderStatus.Online,
                        Classification = "OfficialDownloadPayload",
                        Message = "The official D2PT High Winrate payload was captured through the WebView2 download event.",
                        RawJson = _rawJson,
                        SuggestedFilename = _suggestedFilename,
                        MimeType = _mimeType,
                        RuntimeVersion = _runtimeVersion,
                        FinalUrl = _finalUrl,
                        Title = _title,
                        PageLoaded = _pageLoaded,
                        CloudflareChallenge = _cloudflareChallenge,
                        HighWinrateButtonFound = _highWinrateButtonFound,
                        OfficialDownloadTriggered = _officialDownloadTriggered,
                        DownloadCompleted = _downloadCompleted,
                        InitMs = _initMs,
                        NavigationMs = _navigationMs,
                        DownloadMs = _downloadMs,
                        ElapsedMs = overall.ElapsedMilliseconds,
                        TemporaryFilePath = _tempFilePath
                    };
                }
                finally
                {
                    _webView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                    _webView.CoreWebView2.DOMContentLoaded -= DomContentLoaded;
                    _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                }

                void OnDownloadStarting(object? _, CoreWebView2DownloadStartingEventArgs args)
                {
                    var downloadOperation = args.DownloadOperation;
                    var suggestedFilename = Path.GetFileName(downloadOperation.ResultFilePath);
                    if (string.IsNullOrWhiteSpace(suggestedFilename))
                    {
                        suggestedFilename = "dota2protracker_hero_grid_high_winrate_config.json";
                    }

                    _suggestedFilename = suggestedFilename;
                    _mimeType = downloadOperation.MimeType;
                    if (!suggestedFilename.Contains("high_winrate", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadTcs.TrySetException(new InvalidOperationException($"Unexpected official download filename: {suggestedFilename}"));
                        args.Cancel = true;
                        return;
                    }

                    var redirectedPath = Path.Combine(_workingDirectory, $"{Guid.NewGuid():N}-{suggestedFilename}");
                    _tempFilePath = redirectedPath;
                    args.ResultFilePath = redirectedPath;
                    args.Handled = true;

                    downloadOperation.StateChanged += (_, __) =>
                    {
                        if (downloadOperation.State == CoreWebView2DownloadState.Completed)
                        {
                            _downloadCompleted = true;
                            downloadTcs.TrySetResult(true);
                        }
                        else if (downloadOperation.State == CoreWebView2DownloadState.Interrupted)
                        {
                            downloadTcs.TrySetException(new InvalidOperationException("The official D2PT download was interrupted."));
                        }
                    };
                }
            }

            private D2ptOfficialGridRetrievalResult CompleteFailure(ProviderStatus status, string classification, string message, long elapsedMs)
                => new()
                {
                    Succeeded = false,
                    ProviderStatus = status,
                    Classification = classification,
                    Message = message,
                    SuggestedFilename = _suggestedFilename,
                    MimeType = _mimeType,
                    RuntimeVersion = _runtimeVersion,
                    FinalUrl = _finalUrl,
                    Title = _title,
                    PageLoaded = _pageLoaded,
                    CloudflareChallenge = _cloudflareChallenge,
                    HighWinrateButtonFound = _highWinrateButtonFound,
                    OfficialDownloadTriggered = _officialDownloadTriggered,
                    DownloadCompleted = _downloadCompleted,
                    InitMs = _initMs,
                    NavigationMs = _navigationMs,
                    DownloadMs = _downloadMs,
                    ElapsedMs = elapsedMs,
                    TemporaryFilePath = _tempFilePath
                };

            private async Task<ButtonProbeResult> IdentifyAndClickHighWinrateAsync()
            {
                const string script = """
(() => {
  const headings = Array.from(document.querySelectorAll('h3'));
  for (const heading of headings) {
    const headingText = (heading.textContent || '').replace(/\s+/g, ' ').trim();
    if (headingText !== 'High Winrate') {
      continue;
    }

    const card = heading.closest('div[class*="bg-gradient-to-br"]') || heading.parentElement?.parentElement || heading.parentElement;
    if (!card) {
      continue;
    }

    const text = (card.innerText || '').replace(/\s+/g, ' ').trim();
    if (!text.includes('Most played heroes with >50% winrate') &&
        !text.includes('Most played heroes with > 50% winrate') &&
        !text.includes('Balanced approach focusing on both popularity and success rate')) {
      continue;
    }

    const button = Array.from(card.querySelectorAll('button')).find(candidate =>
      (candidate.innerText || '').replace(/\s+/g, ' ').trim().startsWith('Download'));

    if (!button) {
      return JSON.stringify({
        buttonFound: false,
        clickPerformed: false,
        details: 'High Winrate card found but no associated Download button was located.'
      });
    }

    button.click();
    return JSON.stringify({
      buttonFound: true,
      clickPerformed: true,
      details: 'Identified the High Winrate card by heading and description, then clicked its Download button.'
    });
  }

  return JSON.stringify({
    buttonFound: false,
    clickPerformed: false,
    details: 'Could not confidently identify the High Winrate card/button.'
  });
})()
""";

                var result = await _webView.ExecuteScriptAsync(script);
                var json = JsonSerializer.Deserialize<string>(result) ?? "{}";
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return new ButtonProbeResult(
                    root.TryGetProperty("buttonFound", out var buttonFound) && buttonFound.GetBoolean(),
                    root.TryGetProperty("clickPerformed", out var clickPerformed) && clickPerformed.GetBoolean(),
                    root.TryGetProperty("details", out var details) ? details.GetString() ?? "No details." : "No details.");
            }

            private async Task<PageSnapshot> ReadSnapshotAsync()
            {
                const string script = """
(() => JSON.stringify({
  title: document.title ?? "",
  href: location.href ?? "",
  html: document.documentElement?.outerHTML ?? "",
  bodyText: document.body?.innerText ?? ""
}))()
""";

                var scriptResult = await _webView.ExecuteScriptAsync(script);
                var json = JsonSerializer.Deserialize<string>(scriptResult) ?? "{}";
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return new PageSnapshot(
                    root.TryGetProperty("href", out var href) ? href.GetString() : null,
                    root.TryGetProperty("title", out var title) ? title.GetString() : null,
                    root.TryGetProperty("html", out var html) ? html.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("bodyText", out var bodyText) ? bodyText.GetString() ?? string.Empty : string.Empty);
            }

            private static bool LooksLikeTargetPage(string? title, string bodyText, string html)
                => (title?.Contains("Dota2ProTracker Meta Hero Grids", StringComparison.OrdinalIgnoreCase) ?? false)
                   || bodyText.Contains("High Winrate", StringComparison.OrdinalIgnoreCase)
                   || html.Contains("meta-hero-grids", StringComparison.OrdinalIgnoreCase);
        }

        private sealed record ButtonProbeResult(bool ButtonFound, bool ClickPerformed, string Details);
        private sealed record PageSnapshot(string? Href, string? Title, string Html, string BodyText);
    }
}
