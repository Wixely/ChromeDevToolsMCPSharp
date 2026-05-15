using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class PerformanceTools
{
    private static readonly Dictionary<string, string> _activeTraces = new();
    private static readonly object _lock = new();

    [McpServerTool(Name = "performance_start_trace"),
     Description("Start a Chrome DevTools trace on a page. Only one active trace per page id.")]
    public static async Task<string> StartTrace(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Capture screenshots in the trace.")] bool screenshots = false,
        [Description("Optional comma-separated trace categories. Empty = Puppeteer defaults.")] string? categories = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnablePerformance, "Performance", "performance_start_trace");
        svc.EnsureWriteAllowed("performance_start_trace");
        var entry = await svc.GetPageAsync(pageId, ct);

        var path = Path.Combine(svc.OutputDirectory,
            $"trace-{entry.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

        var options = new TracingOptions
        {
            Path = path,
            Screenshots = screenshots,
        };
        if (!string.IsNullOrWhiteSpace(categories))
        {
            options.Categories = categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        await entry.Page.Tracing.StartAsync(options);
        lock (_lock) { _activeTraces[entry.Id] = path; }
        return JsonSerializer.Serialize(new { id = entry.Id, tracePath = path, started = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "performance_stop_trace"),
     Description("Stop the active trace for a page and return the trace file path.")]
    public static async Task<string> StopTrace(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnablePerformance, "Performance", "performance_stop_trace");
        svc.EnsureWriteAllowed("performance_stop_trace");
        var entry = await svc.GetPageAsync(pageId, ct);

        await entry.Page.Tracing.StopAsync();
        string? path;
        lock (_lock) { _activeTraces.Remove(entry.Id, out path); }
        var info = path is null ? null : new FileInfo(path);
        return JsonSerializer.Serialize(new
        {
            id = entry.Id,
            tracePath = path,
            bytes = info?.Length,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "performance_metrics"),
     Description("Return Chrome's snapshot of page performance metrics (CDP `Performance.getMetrics`).")]
    public static async Task<string> Metrics(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnablePerformance, "Performance", "performance_metrics");
        var entry = await svc.GetPageAsync(pageId, ct);
        var metrics = await entry.Page.MetricsAsync();
        return JsonSerializer.Serialize(new { id = entry.Id, metrics }, JsonOpts.Default);
    }
}
