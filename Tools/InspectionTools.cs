using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;
using PuppeteerSharp.PageAccessibility;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class InspectionTools
{
    [McpServerTool(Name = "get_url"),
     Description("Return the current URL of a page.")]
    public static async Task<string> GetUrl(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        var entry = await svc.GetPageAsync(pageId, ct);
        return JsonSerializer.Serialize(new { id = entry.Id, url = entry.Page.Url }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_title"),
     Description("Return the title of a page.")]
    public static async Task<string> GetTitle(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        var entry = await svc.GetPageAsync(pageId, ct);
        var title = await entry.Page.GetTitleAsync();
        return JsonSerializer.Serialize(new { id = entry.Id, title }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_content"),
     Description("Return the full serialized HTML of a page. Output may be large; truncated above 1 MB with a flag.")]
    public static async Task<string> GetContent(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInspection, "Inspection", "get_content");
        var entry = await svc.GetPageAsync(pageId, ct);
        var html = await entry.Page.GetContentAsync();
        var truncated = false;
        if (html.Length > 1_000_000)
        {
            html = html[..1_000_000];
            truncated = true;
        }
        return JsonSerializer.Serialize(new { id = entry.Id, length = html.Length, truncated, html }, JsonOpts.Default);
    }

    [McpServerTool(Name = "take_snapshot"),
     Description("Return a compact accessibility-tree snapshot of the page, suitable as model context.")]
    public static async Task<string> TakeSnapshot(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("If true, include the entire accessibility tree (not just interesting nodes).")] bool full = false,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInspection, "Inspection", "take_snapshot");
        var entry = await svc.GetPageAsync(pageId, ct);
        var snapshot = await entry.Page.Accessibility.SnapshotAsync(new AccessibilitySnapshotOptions
        {
            InterestingOnly = !full,
        });
        return JsonSerializer.Serialize(new { id = entry.Id, snapshot }, JsonOpts.Default);
    }

    [McpServerTool(Name = "take_screenshot"),
     Description("Capture a PNG screenshot of a page and write it to the OutputDirectory. Returns the local path.")]
    public static async Task<string> TakeScreenshot(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Capture the full scrollable page, not just the viewport. Default false.")] bool fullPage = false,
        [Description("Optional override file name (without directory).")] string? fileName = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInspection, "Inspection", "take_screenshot");
        var entry = await svc.GetPageAsync(pageId, ct);
        var name = string.IsNullOrWhiteSpace(fileName)
            ? $"screenshot-{entry.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png"
            : fileName!;
        var path = Path.Combine(svc.OutputDirectory, SanitizeFileName(name));
        await entry.Page.ScreenshotAsync(path, new ScreenshotOptions { FullPage = fullPage, Type = ScreenshotType.Png });
        var info = new FileInfo(path);
        return JsonSerializer.Serialize(new { id = entry.Id, path, bytes = info.Length, fullPage }, JsonOpts.Default);
    }

    [McpServerTool(Name = "query_selector_count"),
     Description("Return the number of elements matching a CSS selector on a page.")]
    public static async Task<string> Count(
        ChromeDevToolsService svc,
        [Description("CSS selector.")] string selector,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        var entry = await svc.GetPageAsync(pageId, ct);
        var count = await entry.Page.EvaluateFunctionAsync<int>(
            "(sel) => document.querySelectorAll(sel).length", selector);
        return JsonSerializer.Serialize(new { id = entry.Id, selector, count }, JsonOpts.Default);
    }

    [McpServerTool(Name = "evaluate_script"),
     Description("Evaluate a JavaScript expression in the page context and return the result. Disabled by ChromeDevTools:DisableScriptEvaluation=true.")]
    public static async Task<string> Evaluate(
        ChromeDevToolsService svc,
        [Description("JavaScript expression (NOT a function declaration). Last value of the expression is returned.")] string expression,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        if (svc.Options.DisableScriptEvaluation)
            throw new McpException("evaluate_script is disabled (ChromeDevTools:DisableScriptEvaluation=true).");
        svc.EnsureWriteAllowed("evaluate_script");
        var entry = await svc.GetPageAsync(pageId, ct);
        var result = await entry.Page.EvaluateExpressionAsync<System.Text.Json.JsonElement>(expression);
        return JsonSerializer.Serialize(new { id = entry.Id, result }, JsonOpts.Default);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "screenshot.png" : name;
    }
}
