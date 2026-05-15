using System.ComponentModel;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PuppeteerSharp;
using PuppeteerSharp.Mobile;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class EmulationTools
{
    [McpServerTool(Name = "resize_page"),
     Description("Resize the viewport of a page.")]
    public static async Task<string> Resize(
        ChromeDevToolsService svc,
        [Description("Width in CSS pixels.")] int width,
        [Description("Height in CSS pixels.")] int height,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        [Description("Device scale factor (default 1).")] double deviceScaleFactor = 1.0,
        [Description("Treat as mobile (sets touch and meta viewport handling).")] bool mobile = false,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableEmulation, "Emulation", "resize_page");
        svc.EnsureWriteAllowed("resize_page");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.SetViewportAsync(new ViewPortOptions
        {
            Width = width,
            Height = height,
            DeviceScaleFactor = deviceScaleFactor,
            IsMobile = mobile,
            HasTouch = mobile,
        });
        return JsonSerializer.Serialize(new { id = entry.Id, width, height, deviceScaleFactor, mobile }, JsonOpts.Default);
    }

    [McpServerTool(Name = "emulate_device"),
     Description("Emulate a built-in Puppeteer device (e.g. `iPhone 13`, `iPad`, `Pixel 5`). See PuppeteerSharp.Mobile.DeviceDescriptors.")]
    public static async Task<string> EmulateDevice(
        ChromeDevToolsService svc,
        [Description("Device name as known to Puppeteer (case-insensitive).")] string device,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableEmulation, "Emulation", "emulate_device");
        svc.EnsureWriteAllowed("emulate_device");
        var entry = await svc.GetPageAsync(pageId, ct);

        var match = Enum.GetValues<DeviceDescriptorName>()
            .FirstOrDefault(d => string.Equals(d.ToString(), device.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase));
        if (match == default && !string.Equals(Enum.GetName(default(DeviceDescriptorName))!, device, StringComparison.OrdinalIgnoreCase))
            throw new McpException($"Unknown device '{device}'.");

        var descriptor = Puppeteer.Devices[match];
        await entry.Page.EmulateAsync(descriptor);
        return JsonSerializer.Serialize(new { id = entry.Id, device = match.ToString() }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_user_agent"),
     Description("Override the User-Agent header for subsequent requests on a page.")]
    public static async Task<string> SetUserAgent(
        ChromeDevToolsService svc,
        [Description("User-Agent string.")] string userAgent,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableEmulation, "Emulation", "set_user_agent");
        svc.EnsureWriteAllowed("set_user_agent");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.SetUserAgentAsync(new SetUserAgentOptions { UserAgent = userAgent });
        return JsonSerializer.Serialize(new { id = entry.Id, userAgent }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_geolocation"),
     Description("Override the page's reported geolocation.")]
    public static async Task<string> SetGeolocation(
        ChromeDevToolsService svc,
        [Description("Latitude (decimal degrees).")] double latitude,
        [Description("Longitude (decimal degrees).")] double longitude,
        [Description("Accuracy radius in meters (default 50).")] double accuracy = 50,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableEmulation, "Emulation", "set_geolocation");
        svc.EnsureWriteAllowed("set_geolocation");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.SetGeolocationAsync(new GeolocationOption
        {
            Latitude = (decimal)latitude,
            Longitude = (decimal)longitude,
            Accuracy = (decimal)accuracy,
        });
        return JsonSerializer.Serialize(new { id = entry.Id, latitude, longitude, accuracy }, JsonOpts.Default);
    }

    [McpServerTool(Name = "set_offline"),
     Description("Toggle offline emulation on a page.")]
    public static async Task<string> SetOffline(
        ChromeDevToolsService svc,
        [Description("True to go offline, false to restore network.")] bool offline,
        [Description("Page id. Defaults to the current page.")] string? pageId = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableEmulation, "Emulation", "set_offline");
        svc.EnsureWriteAllowed("set_offline");
        var entry = await svc.GetPageAsync(pageId, ct);
        await entry.Page.SetOfflineModeAsync(offline);
        return JsonSerializer.Serialize(new { id = entry.Id, offline }, JsonOpts.Default);
    }
}
