using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class CookieTools
{
    [McpServerTool(Name = "list_cookies"),
     Description("List cookies visible to a page. Optionally pass extra URLs to include their cookies as well.")]
    public static async Task<string> List(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Optional comma-separated additional URLs to include cookies for.")] string? extraUrls = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableCookies, "Cookies", "list_cookies");
        var entry = await svc.GetPageAsync(pageId, ct);
        var urls = string.IsNullOrWhiteSpace(extraUrls)
            ? Array.Empty<string>()
            : extraUrls!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cookies = await entry.Page.GetCookiesAsync(urls);
        return JsonSerializer.Serialize(new { id = entry.Id, cookies }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_cookies"),
     Description("Set one or more cookies. Pass a JSON array of cookie objects (name, value, domain, path, url, expires, httpOnly, secure, sameSite).")]
    public static async Task<string> Set(
        ChromeDevToolsService svc,
        [Description("JSON array of cookie objects.")] string cookiesJson,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableCookies, "Cookies", "set_cookies");
        svc.EnsureWriteAllowed("set_cookies");
        var entry = await svc.GetPageAsync(pageId, ct);
        if (JsonNode.Parse(cookiesJson) is not JsonArray arr)
            throw new McpException("cookiesJson must be a JSON array.");
        var cookies = new List<CookieParam>();
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            cookies.Add(new CookieParam
            {
                Name = obj["name"]?.GetValue<string?>() ?? throw new McpException("Cookie missing name."),
                Value = obj["value"]?.GetValue<string?>() ?? string.Empty,
                Url = obj["url"]?.GetValue<string?>(),
                Domain = obj["domain"]?.GetValue<string?>(),
                Path = obj["path"]?.GetValue<string?>(),
                Expires = obj["expires"]?.GetValue<double?>(),
                HttpOnly = obj["httpOnly"]?.GetValue<bool?>(),
                Secure = obj["secure"]?.GetValue<bool?>(),
                SameSite = ParseSameSite(obj["sameSite"]?.GetValue<string?>()),
            });
        }
        await entry.Page.SetCookieAsync(cookies.ToArray());
        return JsonSerializer.Serialize(new { id = entry.Id, set = cookies.Count }, JsonOpts.Default);
    }

    [McpServerTool(Name = "delete_cookies"),
     Description("Delete one or more cookies. Pass a JSON array of objects with at least name+url or name+domain+path.")]
    public static async Task<string> Delete(
        ChromeDevToolsService svc,
        [Description("JSON array of cookie identifier objects.")] string cookiesJson,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableCookies, "Cookies", "delete_cookies");
        svc.EnsureWriteAllowed("delete_cookies");
        var entry = await svc.GetPageAsync(pageId, ct);
        if (JsonNode.Parse(cookiesJson) is not JsonArray arr)
            throw new McpException("cookiesJson must be a JSON array.");
        var cookies = new List<CookieParam>();
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            cookies.Add(new CookieParam
            {
                Name = obj["name"]?.GetValue<string?>() ?? throw new McpException("Cookie missing name."),
                Url = obj["url"]?.GetValue<string?>(),
                Domain = obj["domain"]?.GetValue<string?>(),
                Path = obj["path"]?.GetValue<string?>(),
            });
        }
        await entry.Page.DeleteCookieAsync(cookies.ToArray());
        return JsonSerializer.Serialize(new { id = entry.Id, deleted = cookies.Count }, JsonOpts.Default);
    }

    private static SameSite? ParseSameSite(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "strict" => SameSite.Strict,
            "lax" => SameSite.Lax,
            "none" => SameSite.None,
            _ => null,
        };
}
