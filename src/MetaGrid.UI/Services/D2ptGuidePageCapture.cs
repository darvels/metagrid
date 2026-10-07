using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MetaGrid.Core.Abstractions;

namespace MetaGrid.UI.Services;

public static class D2ptGuidePageCapture
{
    public static async Task<string> CaptureAsync(string url, string profile, CancellationToken cancellationToken, string? extractionScript = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
            using var form = new Form { ShowInTaskbar = false, Opacity = 0, Width = 32, Height = 32 };
            using var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);
            form.Shown += async (_, _) =>
            {
                try
                {
                    var environment = await CoreWebView2Environment.CreateAsync(null, profile).WaitAsync(timeout.Token);
                    await view.EnsureCoreWebView2Async(environment).WaitAsync(timeout.Token);
                    var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    view.CoreWebView2.NavigationCompleted += (_, e) =>
                    {
                        if (e.IsSuccess) ready.TrySetResult(true);
                        else ready.TrySetException(new IOException($"D2PT navigation failed: {e.WebErrorStatus}"));
                    };
                    view.CoreWebView2.Navigate(url);
                    await ready.Task.WaitAsync(timeout.Token);
                    await Task.Delay(1500, timeout.Token);
                    string html;
                    if (extractionScript is null)
                        html = await view.ExecuteScriptAsync("document.documentElement.outerHTML").WaitAsync(timeout.Token);
                    else
                    {
                        // ExecuteScriptAsync serializes a Promise as {}, rather than awaiting it.
                        await view.ExecuteScriptAsync("window.__metaGridProbe=null;(" + extractionScript
                            + ").then(v=>window.__metaGridProbe={value:v},e=>window.__metaGridProbe={error:String(e)});").WaitAsync(timeout.Token);
                        while (true)
                        {
                            await Task.Delay(200, timeout.Token);
                            var state = await view.ExecuteScriptAsync("window.__metaGridProbe").WaitAsync(timeout.Token);
                            if (state == "null") continue;
                            using var result = JsonDocument.Parse(state);
                            if (result.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                            html = result.RootElement.GetProperty("value").GetRawText();
                            break;
                        }
                    }
                    completion.TrySetResult(JsonSerializer.Deserialize<string>(html) ?? string.Empty);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { view.Dispose(); form.Close(); }
            };
            Application.Run(form);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { stopped.TrySetResult(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { return await completion.Task.WaitAsync(timeout.Token); }
        finally
        {
            timeout.Cancel();
            // Await STA disposal, but never indefinitely await a browser runtime.
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

public sealed class D2ptGuidePageCaptureService(IAppPaths paths) : ID2ptGuidePageCapture
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> CaptureAsync(CancellationToken cancellationToken)
        => await CapturePageAsync("https://dota2protracker.com/builds/io-pos2-1", cancellationToken);

    public Task<string> CaptureIndexAsync(CancellationToken cancellationToken)
        => CapturePageAsync("https://dota2protracker.com/builds", cancellationToken);

    public Task<string> CaptureStartingInventoriesAsync(string heroName, MetaGrid.Core.Models.GuideRole role, CancellationToken cancellationToken)
        => CapturePageAsync("https://dota2protracker.com/hero/legacy/" + Uri.EscapeDataString(heroName)
            + "?role=" + StartingInventoryRole(role), cancellationToken);

    public static string StartingInventoryRole(MetaGrid.Core.Models.GuideRole role) => role switch
    {
        MetaGrid.Core.Models.GuideRole.Carry => "carry", MetaGrid.Core.Models.GuideRole.Mid => "mid",
        MetaGrid.Core.Models.GuideRole.Offlane => "offlane", MetaGrid.Core.Models.GuideRole.Support => "support",
        MetaGrid.Core.Models.GuideRole.HardSupport => "hard-support", _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private async Task<string> CapturePageAsync(string url, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await D2ptGuidePageCapture.CaptureAsync(url,
                Path.Combine(paths.GuideCacheDirectory, "webview-profile"), cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
