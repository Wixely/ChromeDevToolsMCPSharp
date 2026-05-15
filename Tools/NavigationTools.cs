using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class NavigationTools
{
    [McpServerTool(Name = "list_pages"),
     Description("List all open pages (tabs) attached to the controlled Chrome with their stable ids, urls, and titles.")]
    public static async Task<string> ListPages(ChromeDevToolsService svc, CancellationToken ct = default)
    {
        await svc.GetBrowserAsync(ct);
        var summary = new List<object>();
        foreach (var p in svc.Pages)
        {
            string url, title;
            try { url = p.Page.Url; } catch { url = "(unavailable)"; }
            try { title = await p.Page.GetTitleAsync(); } catch { title = "(unavailable)"; }
            summary.Add(new { id = p.Id, current = p.Id == svc.CurrentPageId, url, title });
        }
        return JsonSerializer.Serialize(summary, JsonOpts.Default);
    }

    [McpServerTool(Name = "select_page"),
     Description("Set the current page id used by subsequent tool calls that omit the pageId argument.")]
    public static async Task<string> SelectPage(
        ChromeDevToolsService svc,
        [Description("Page id from list_pages.")] string pageId,
        CancellationToken ct = default)
    {
        await svc.GetBrowserAsync(ct);
        svc.SetCurrent(pageId);
        return JsonSerializer.Serialize(new { currentPageId = pageId }, JsonOpts.Default);
    }

    [McpServerTool(Name = "new_page"),
     Description("Open a new tab and optionally navigate to a URL. Returns the new page id.")]
    public static async Task<string> NewPage(
        ChromeDevToolsService svc,
        [Description("Optional URL to navigate the new page to.")] string? url = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "new_page");
        svc.EnsureWriteAllowed("new_page");
        if (!string.IsNullOrWhiteSpace(url)) svc.EnsureUrlAllowed(url);

        var browser = await svc.GetBrowserAsync(ct);
        var page = await browser.NewPageAsync();
        var entry = svc.Register(page);
        svc.SetCurrent(entry.Id);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await page.GoToAsync(url, new NavigationOptions { Timeout = svc.Options.DefaultActionTimeoutMs });
        }
        return JsonSerializer.Serialize(new { id = entry.Id, url = page.Url }, JsonOpts.Default);
    }

    [McpServerTool(Name = "close_page"),
     Description("Close a page (tab). Will not close the last remaining page on an attached external Chrome.")]
    public static async Task<string> ClosePage(
        ChromeDevToolsService svc,
        [Description("Page id from list_pages.")] string pageId,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "close_page");
        svc.EnsureWriteAllowed("close_page");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.CloseAsync(new PageCloseOptions());
        return JsonSerializer.Serialize(new { closed = true, id = entry.Id }, JsonOpts.Default);
    }

    [McpServerTool(Name = "navigate_page"),
     Description("Navigate a page to a URL. Waits for `load` by default.")]
    public static async Task<string> Navigate(
        ChromeDevToolsService svc,
        [Description("Target URL.")] string url,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Wait condition: load, domcontentloaded, networkidle0, networkidle2. Default load.")] string waitUntil = "load",
        [Description("Override navigation timeout in milliseconds.")] int? timeoutMs = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "navigate_page");
        svc.EnsureWriteAllowed("navigate_page");
        svc.EnsureUrlAllowed(url);
        var entry = await svc.GetPageAsync(pageId, ct);
        var resp = await entry.Page.GoToAsync(url, new NavigationOptions
        {
            Timeout = timeoutMs ?? svc.Options.DefaultActionTimeoutMs,
            WaitUntil = new[] { ParseWaitUntil(waitUntil) },
        });
        return JsonSerializer.Serialize(new
        {
            id = entry.Id,
            url = entry.Page.Url,
            status = (int?)resp?.Status,
            ok = resp?.Ok,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "go_back"),
     Description("Navigate back in history on a page.")]
    public static async Task<string> GoBack(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "go_back");
        svc.EnsureWriteAllowed("go_back");
        var entry = await svc.GetPageAsync(pageId, ct);
        var resp = await entry.Page.GoBackAsync(new NavigationOptions { Timeout = svc.Options.DefaultActionTimeoutMs });
        return JsonSerializer.Serialize(new { id = entry.Id, url = entry.Page.Url, status = (int?)resp?.Status }, JsonOpts.Default);
    }

    [McpServerTool(Name = "go_forward"),
     Description("Navigate forward in history on a page.")]
    public static async Task<string> GoForward(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "go_forward");
        svc.EnsureWriteAllowed("go_forward");
        var entry = await svc.GetPageAsync(pageId, ct);
        var resp = await entry.Page.GoForwardAsync(new NavigationOptions { Timeout = svc.Options.DefaultActionTimeoutMs });
        return JsonSerializer.Serialize(new { id = entry.Id, url = entry.Page.Url, status = (int?)resp?.Status }, JsonOpts.Default);
    }

    [McpServerTool(Name = "reload_page"),
     Description("Reload a page.")]
    public static async Task<string> Reload(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNavigation, "Navigation", "reload_page");
        svc.EnsureWriteAllowed("reload_page");
        var entry = await svc.GetPageAsync(pageId, ct);
        var resp = await entry.Page.ReloadAsync(new ReloadOptions { Timeout = svc.Options.DefaultActionTimeoutMs });
        return JsonSerializer.Serialize(new { id = entry.Id, url = entry.Page.Url, status = (int?)resp?.Status }, JsonOpts.Default);
    }

    [McpServerTool(Name = "wait_for_selector"),
     Description("Wait for a CSS selector to appear on a page.")]
    public static async Task<string> WaitForSelector(
        ChromeDevToolsService svc,
        [Description("CSS selector to wait for.")] string selector,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("If true, wait until the element is visible (default false = attached to DOM).")] bool visible = false,
        [Description("Override wait timeout in milliseconds.")] int? timeoutMs = null,
        CancellationToken ct = default)
    {
        var entry = await svc.GetPageAsync(pageId, ct);
        var element = await entry.Page.WaitForSelectorAsync(selector, new WaitForSelectorOptions
        {
            Timeout = timeoutMs ?? svc.Options.DefaultActionTimeoutMs,
            Visible = visible,
        });
        return JsonSerializer.Serialize(new { id = entry.Id, selector, found = element is not null }, JsonOpts.Default);
    }

    [McpServerTool(Name = "wait_for_navigation"),
     Description("Wait for the next navigation event on a page.")]
    public static async Task<string> WaitForNavigation(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Wait condition: load, domcontentloaded, networkidle0, networkidle2.")] string waitUntil = "load",
        [Description("Override timeout in milliseconds.")] int? timeoutMs = null,
        CancellationToken ct = default)
    {
        var entry = await svc.GetPageAsync(pageId, ct);
        var resp = await entry.Page.WaitForNavigationAsync(new NavigationOptions
        {
            Timeout = timeoutMs ?? svc.Options.DefaultActionTimeoutMs,
            WaitUntil = new[] { ParseWaitUntil(waitUntil) },
        });
        return JsonSerializer.Serialize(new { id = entry.Id, url = entry.Page.Url, status = (int?)resp?.Status }, JsonOpts.Default);
    }

    [McpServerTool(Name = "handle_dialog"),
     Description("Accept or dismiss the most recent JS dialog (alert/confirm/prompt) on a page. Optionally provide prompt text.")]
    public static async Task<string> HandleDialog(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("If true, accept the dialog; otherwise dismiss it.")] bool accept = true,
        [Description("Optional prompt text used when accepting a prompt dialog.")] string? promptText = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("handle_dialog");
        var entry = await svc.GetPageAsync(pageId, ct);
        var dialog = entry.LastDialog ?? throw new McpException("No pending dialog on this page.");
        if (accept) await dialog.Accept(promptText);
        else await dialog.Dismiss();
        return JsonSerializer.Serialize(new { id = entry.Id, accepted = accept }, JsonOpts.Default);
    }

    private static WaitUntilNavigation ParseWaitUntil(string value) =>
        value?.ToLowerInvariant() switch
        {
            "domcontentloaded" => WaitUntilNavigation.DOMContentLoaded,
            "networkidle0" => WaitUntilNavigation.Networkidle0,
            "networkidle2" => WaitUntilNavigation.Networkidle2,
            _ => WaitUntilNavigation.Load,
        };
}
