using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol.Server;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class ConsoleTools
{
    [McpServerTool(Name = "list_console_messages"),
     Description("Return buffered console messages for a page (capped by ChromeDevTools:MaxConsoleBuffer).")]
    public static async Task<string> List(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Optional level filter: log, info, warn, warning, error, debug.")] string? level = null,
        [Description("Optional substring filter on message text (case-insensitive).")] string? contains = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableConsole, "Console", "list_console_messages");
        var entry = await svc.GetPageAsync(pageId, ct);
        IEnumerable<ConsoleRecord> messages = entry.Console;
        if (!string.IsNullOrWhiteSpace(level))
            messages = messages.Where(m => string.Equals(m.Type, level, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(contains))
            messages = messages.Where(m => m.Text.Contains(contains, StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(new { id = entry.Id, messages = messages.ToArray() }, JsonOpts.Default);
    }

    [McpServerTool(Name = "clear_console_messages"),
     Description("Clear the in-memory console buffer for a page.")]
    public static async Task<string> Clear(
        ChromeDevToolsService svc,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableConsole, "Console", "clear_console_messages");
        var entry = await svc.GetPageAsync(pageId, ct);
        entry.ClearConsole();
        return JsonSerializer.Serialize(new { id = entry.Id, cleared = true }, JsonOpts.Default);
    }
}
