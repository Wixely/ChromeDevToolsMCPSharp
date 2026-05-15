using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;
using PuppeteerSharp.Input;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class InputTools
{
    [McpServerTool(Name = "click"),
     Description("Click the first element matching a CSS selector.")]
    public static async Task<string> Click(
        ChromeDevToolsService svc,
        [Description("CSS selector.")] string selector,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Mouse button: left, right, middle. Default left.")] string button = "left",
        [Description("Click count (1 = single, 2 = double, 3 = triple).")] int clickCount = 1,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "click");
        svc.EnsureWriteAllowed("click");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.ClickAsync(selector, new ClickOptions
        {
            Button = ParseButton(button),
            Count = Math.Max(1, clickCount),
        });
        return JsonSerializer.Serialize(new { id = entry.Id, selector, clicked = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "click_at"),
     Description("Click at absolute viewport coordinates.")]
    public static async Task<string> ClickAt(
        ChromeDevToolsService svc,
        [Description("X coordinate in CSS pixels.")] double x,
        [Description("Y coordinate in CSS pixels.")] double y,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Mouse button: left, right, middle.")] string button = "left",
        [Description("Click count.")] int clickCount = 1,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "click_at");
        svc.EnsureWriteAllowed("click_at");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.Mouse.ClickAsync((decimal)x, (decimal)y, new ClickOptions
        {
            Button = ParseButton(button),
            Count = Math.Max(1, clickCount),
        });
        return JsonSerializer.Serialize(new { id = entry.Id, x, y, clicked = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "hover"),
     Description("Hover over the first element matching a CSS selector.")]
    public static async Task<string> Hover(
        ChromeDevToolsService svc,
        [Description("CSS selector.")] string selector,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "hover");
        svc.EnsureWriteAllowed("hover");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.HoverAsync(selector);
        return JsonSerializer.Serialize(new { id = entry.Id, selector, hovered = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "type_text"),
     Description("Type text into the focused element (or the selector if provided), simulating keypresses.")]
    public static async Task<string> TypeText(
        ChromeDevToolsService svc,
        [Description("Text to type.")] string text,
        [Description("Optional CSS selector to focus first.")] string? selector = null,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Inter-keystroke delay in milliseconds.")] int delayMs = 0,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "type_text");
        svc.EnsureWriteAllowed("type_text");
        var entry = await svc.GetPageAsync(pageId, ct);
        if (!string.IsNullOrEmpty(selector))
        {
            await entry.Page.TypeAsync(selector, text, new TypeOptions { Delay = delayMs });
        }
        else
        {
            await entry.Page.Keyboard.TypeAsync(text, new TypeOptions { Delay = delayMs });
        }
        return JsonSerializer.Serialize(new { id = entry.Id, length = text.Length, selector }, JsonOpts.Default);
    }

    [McpServerTool(Name = "fill_input"),
     Description("Set the value of an input/textarea by clearing it and typing.")]
    public static async Task<string> Fill(
        ChromeDevToolsService svc,
        [Description("CSS selector.")] string selector,
        [Description("Value to set.")] string value,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "fill_input");
        svc.EnsureWriteAllowed("fill_input");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.EvaluateFunctionAsync(
            @"(sel) => { const el = document.querySelector(sel);
                if (el) { el.focus(); if ('value' in el) el.value = ''; }
            }", selector);
        await entry.Page.TypeAsync(selector, value);
        return JsonSerializer.Serialize(new { id = entry.Id, selector, length = value.Length }, JsonOpts.Default);
    }

    [McpServerTool(Name = "fill_form"),
     Description("Set the values of multiple inputs in one call. Pass a JSON object of selector -> value.")]
    public static async Task<string> FillForm(
        ChromeDevToolsService svc,
        [Description("JSON object mapping CSS selector to value, e.g. `{\"#name\":\"Joe\",\"#email\":\"a@b.c\"}`.")] string fieldsJson,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "fill_form");
        svc.EnsureWriteAllowed("fill_form");
        if (JsonNode.Parse(fieldsJson) is not JsonObject obj)
            throw new McpException("fieldsJson must be a JSON object of selector -> value.");
        var entry = await svc.GetPageAsync(pageId, ct);
        var filled = 0;
        foreach (var kvp in obj)
        {
            var selector = kvp.Key;
            var value = kvp.Value?.ToString() ?? string.Empty;
            await entry.Page.EvaluateFunctionAsync(
                @"(sel) => { const el = document.querySelector(sel);
                    if (el) { el.focus(); if ('value' in el) el.value = ''; }
                }", selector);
            await entry.Page.TypeAsync(selector, value);
            filled++;
        }
        return JsonSerializer.Serialize(new { id = entry.Id, filled }, JsonOpts.Default);
    }

    [McpServerTool(Name = "press_key"),
     Description("Press a single named key (e.g. Enter, Tab, ArrowDown, a). Uses the page keyboard.")]
    public static async Task<string> PressKey(
        ChromeDevToolsService svc,
        [Description("Key name as defined by the W3C `key` value (e.g. Enter, Escape, ArrowLeft, a).")] string key,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "press_key");
        svc.EnsureWriteAllowed("press_key");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.Keyboard.PressAsync(key);
        return JsonSerializer.Serialize(new { id = entry.Id, key, pressed = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "upload_file"),
     Description("Upload one or more local files into an <input type=\"file\"> element.")]
    public static async Task<string> UploadFile(
        ChromeDevToolsService svc,
        [Description("CSS selector for the file input.")] string selector,
        [Description("Comma-separated absolute paths to upload.")] string filePaths,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableInput, "Input", "upload_file");
        svc.EnsureWriteAllowed("upload_file");
        var paths = filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in paths)
        {
            if (!File.Exists(p)) throw new McpException($"File not found: {p}");
        }
        var entry = await svc.GetPageAsync(pageId, ct);
        var input = await entry.Page.QuerySelectorAsync(selector)
            ?? throw new McpException($"Selector '{selector}' did not match any element.");
        await input.UploadFileAsync(paths);
        return JsonSerializer.Serialize(new { id = entry.Id, selector, files = paths }, JsonOpts.Default);
    }

    private static MouseButton ParseButton(string value) =>
        value?.ToLowerInvariant() switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left,
        };
}
