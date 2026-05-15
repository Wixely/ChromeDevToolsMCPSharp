using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class NetworkTools
{
    [McpServerTool(Name = "list_network_requests"),
     Description("Return buffered network requests for a page (capped by ChromeDevTools:MaxNetworkBuffer). Headers are not included; use get_network_request for details.")]
    public static async Task<string> List(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Optional resource type filter (e.g. document, xhr, fetch, image, script, stylesheet).")] string? resourceType = null,
        [Description("Optional URL substring filter (case-insensitive).")] string? urlContains = null,
        [Description("Only include failed requests when true.")] bool failedOnly = false,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNetwork, "Network", "list_network_requests");
        var entry = await svc.GetPageAsync(pageId, ct);
        IEnumerable<NetworkRecord> items = entry.Network;
        if (!string.IsNullOrWhiteSpace(resourceType))
            items = items.Where(r => string.Equals(r.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(urlContains))
            items = items.Where(r => r.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase));
        if (failedOnly)
            items = items.Where(r => r.Failure is not null);

        var summary = items.Select(r => new
        {
            r.RequestId,
            r.Method,
            r.Url,
            r.ResourceType,
            r.Status,
            r.FromCache,
            r.MimeType,
            r.Failure,
            r.StartedUtc,
            r.CompletedUtc,
            durationMs = r.DurationMs,
        });
        return JsonSerializer.Serialize(new { id = entry.Id, requests = summary }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_network_request"),
     Description("Return the full details (headers, post data, response body if available) for a buffered network request. Headers listed in ChromeDevTools:RedactedHeaders are redacted.")]
    public static async Task<string> Get(
        ChromeDevToolsService svc,
        [Description("RequestId returned by list_network_requests.")] string requestId,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("If true, include the response body (when available). Bodies above 1 MB are truncated.")] bool includeBody = false,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNetwork, "Network", "get_network_request");
        var entry = await svc.GetPageAsync(pageId, ct);
        var record = entry.Network.FirstOrDefault(r => r.RequestId == requestId)
            ?? throw new McpException($"No buffered request '{requestId}'. It may have aged out of the ring buffer.");

        var redacted = new HashSet<string>(svc.Options.RedactedHeaders, StringComparer.OrdinalIgnoreCase);

        var liveRequest = record.LiveRequest;
        if (liveRequest is null)
        {
            return JsonSerializer.Serialize(new { record, note = "Live request handle not available (likely already discarded)." }, JsonOpts.Default);
        }

        var requestHeaders = Redact(liveRequest.Headers, redacted);
        var response = liveRequest.Response;
        var responseHeaders = response is null ? null : Redact(response.Headers, redacted);

        string? body = null;
        var truncated = false;
        if (includeBody && response is not null)
        {
            try
            {
                var text = await response.TextAsync();
                if (text.Length > 1_000_000) { text = text[..1_000_000]; truncated = true; }
                body = text;
            }
            catch (Exception ex) { body = $"(failed to read body: {ex.Message})"; }
        }

        return JsonSerializer.Serialize(new
        {
            record,
            request = new
            {
                method = liveRequest.Method.ToString(),
                liveRequest.Url,
                liveRequest.PostData,
                headers = requestHeaders,
            },
            response = response is null ? null : new
            {
                status = (int)response.Status,
                response.StatusText,
                response.FromCache,
                headers = responseHeaders,
            },
            body,
            truncated,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "clear_network_log"),
     Description("Clear the in-memory network buffer for a page.")]
    public static async Task<string> Clear(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableNetwork, "Network", "clear_network_log");
        var entry = await svc.GetPageAsync(pageId, ct);
        entry.ClearNetwork();
        return JsonSerializer.Serialize(new { id = entry.Id, cleared = true }, JsonOpts.Default);
    }

    private static IDictionary<string, string>? Redact(IDictionary<string, string>? headers, HashSet<string> redacted)
    {
        if (headers is null) return null;
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in headers)
        {
            copy[kvp.Key] = redacted.Contains(kvp.Key) ? "<redacted>" : kvp.Value;
        }
        return copy;
    }
}
