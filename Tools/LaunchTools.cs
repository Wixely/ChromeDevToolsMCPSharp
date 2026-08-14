using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ChromeDevToolsMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ChromeDevToolsMCPSharp.Tools;

[McpServerToolType]
public static class LaunchTools
{
    [McpServerTool(Name = "launch_chrome"),
     Description("Launch the locally installed Chrome as a dedicated debuggable instance and attach to it. " +
                 "Chrome 136+ refuses to serve the DevTools remote-debugging API on the default user profile, " +
                 "so a regular Chrome window can never be attached to — this tool starts a separate instance " +
                 "with its own profile directory (--remote-debugging-port + --user-data-dir) and connects the " +
                 "server to it. If a debuggable Chrome is already listening on the port it attaches to that instead.")]
    public static async Task<string> LaunchChrome(
        ChromeDevToolsService svc,
        [Description("Remote debugging port. Defaults to the port in ChromeDevTools:BrowserUrl, or 9222.")] int? port = null,
        [Description("Run headless (--headless=new). Default false so the window is visible.")] bool headless = false,
        [Description("Dedicated profile directory. Defaults to ChromeDevTools:UserDataDir or <LocalAppData>/ChromeDevToolsMCPSharp/chrome-debug-profile. Must not be the regular Chrome profile.")] string? userDataDir = null,
        [Description("Optional URL to open in the launched browser. Default about:blank.")] string? url = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("launch_chrome");
        if (!string.IsNullOrWhiteSpace(url)) svc.EnsureUrlAllowed(url);

        var debugPort = port ?? PortFromBrowserUrl(svc.Options.BrowserUrl) ?? 9222;
        var browserUrl = $"http://127.0.0.1:{debugPort}";
        // Loopback CDP traffic must never go through a system/PAC proxy — a corporate proxy
        // answering for 127.0.0.1 makes a free port look occupied.
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        var probe = await ProbeAsync(http, debugPort, browserUrl, ct);
        if (probe.Status == ProbeStatus.DevToolsAvailable)
        {
            await svc.AttachToWebSocketAsync(probe.WsEndpoint!, ct);
            return JsonSerializer.Serialize(new
            {
                launched = false,
                attached = true,
                browserUrl,
                note = "A debuggable Chrome was already listening on this port; attached to it.",
            }, JsonOpts.Default);
        }
        if (probe.Status == ProbeStatus.PortBusyNoDevTools)
        {
            throw new McpException(
                $"Port {debugPort} is in use but /json/version does not answer, so whatever owns it is not a " +
                "debuggable Chrome. Most commonly it is a regular Chrome on the default user profile — Chrome 136+ " +
                "refuses the DevTools API there. Fully close that Chrome (including background processes) or pass " +
                "a different port, then call launch_chrome again.");
        }

        var exe = ResolveChromeExecutable(svc.Options.ExecutablePath);
        var profileDir = ResolveProfileDir(userDataDir ?? svc.Options.UserDataDir);
        Directory.CreateDirectory(profileDir);

        var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false };
        psi.ArgumentList.Add($"--remote-debugging-port={debugPort}");
        psi.ArgumentList.Add($"--user-data-dir={profileDir}");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        if (headless) psi.ArgumentList.Add("--headless=new");
        foreach (var extra in svc.Options.ExtraChromeArgs) psi.ArgumentList.Add(extra);
        psi.ArgumentList.Add(string.IsNullOrWhiteSpace(url) ? "about:blank" : url);

        using var proc = Process.Start(psi)
            ?? throw new McpException($"Failed to start Chrome process '{exe}'.");

        var timeoutMs = Math.Max(1_000, svc.Options.ConnectionTimeoutMs);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while ((probe = await ProbeAsync(http, debugPort, browserUrl, ct)).Status != ProbeStatus.DevToolsAvailable)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                var exitHint = proc.HasExited
                    ? $" The launcher process exited (code {proc.ExitCode}) — a Chrome already using profile '{profileDir}' " +
                      "without debugging enabled would cause this; close it first or use a different userDataDir."
                    : string.Empty;
                throw new McpException(
                    $"Chrome did not expose DevTools on port {debugPort} within {timeoutMs}ms.{exitHint}");
            }
            await Task.Delay(250, ct);
        }

        await svc.AttachToWebSocketAsync(probe.WsEndpoint!, ct);
        return JsonSerializer.Serialize(new
        {
            launched = true,
            attached = true,
            browserUrl,
            executable = exe,
            userDataDir = profileDir,
            headless,
        }, JsonOpts.Default);
    }

    private enum ProbeStatus { PortFree, DevToolsAvailable, PortBusyNoDevTools }

    private sealed record ProbeResult(ProbeStatus Status, string? WsEndpoint);

    private static async Task<ProbeResult> ProbeAsync(HttpClient http, int port, string browserUrl, CancellationToken ct)
    {
        // Raw TCP connect first: connect success is the only reliable "port occupied" signal.
        // Loopback-filtering security agents can take seconds to refuse a closed port, which an
        // HTTP-level timeout would misread as a busy port.
        using (var tcp = new TcpClient())
        {
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(4));
                await tcp.ConnectAsync(IPAddress.Loopback, port, connectCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new ProbeResult(ProbeStatus.PortFree, null);
            }
        }

        try
        {
            using var resp = await http.GetAsync($"{browserUrl}/json/version", ct);
            if (!resp.IsSuccessStatusCode)
                return new ProbeResult(ProbeStatus.PortBusyNoDevTools, null);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var ws = doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp)
                ? wsProp.GetString()
                : null;
            return string.IsNullOrWhiteSpace(ws)
                ? new ProbeResult(ProbeStatus.PortBusyNoDevTools, null)
                : new ProbeResult(ProbeStatus.DevToolsAvailable, ws);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // TCP accepted but no usable DevTools answer — treat as occupied.
            return new ProbeResult(ProbeStatus.PortBusyNoDevTools, null);
        }
    }

    private static int? PortFromBrowserUrl(string? browserUrl) =>
        Uri.TryCreate(browserUrl, UriKind.Absolute, out var uri) ? uri.Port : null;

    private static string ResolveProfileDir(string? configured)
    {
        var dir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChromeDevToolsMCPSharp", "chrome-debug-profile")
            : Path.GetFullPath(configured);

        // Chrome 136+ silently disables remote debugging on the real profile, so catch that early.
        var defaultProfile = Path.Combine("Google", "Chrome", "User Data");
        if (dir.Contains(defaultProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                $"Refusing to launch with user data dir '{dir}': that is the regular Chrome profile, and Chrome 136+ " +
                "does not allow remote debugging on it. Use a dedicated directory.");
        }
        return dir;
    }

    private static string ResolveChromeExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return configured;
            throw new McpException($"ChromeDevTools:ExecutablePath '{configured}' does not exist.");
        }

        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            })
            {
                if (!string.IsNullOrEmpty(root))
                    candidates.Add(Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
        }
        else
        {
            candidates.Add("/usr/bin/google-chrome");
            candidates.Add("/usr/bin/google-chrome-stable");
            candidates.Add("/usr/bin/chromium");
            candidates.Add("/usr/bin/chromium-browser");
        }

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new McpException(
                "Could not find a Chrome executable in the standard install locations. " +
                "Set ChromeDevTools:ExecutablePath to the chrome binary.");
    }
}
