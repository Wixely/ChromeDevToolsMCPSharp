namespace ChromeDevToolsMCPSharp.Configuration;

public sealed class ChromeDevToolsOptions
{
    public const string SectionName = "ChromeDevTools";

    /// <summary>
    /// HTTP URL of an existing debuggable Chrome instance (e.g. `http://localhost:9222`).
    /// When set, the server attaches via CDP instead of launching a new Chrome.
    /// </summary>
    public string? BrowserUrl { get; set; }

    /// <summary>Direct CDP WebSocket endpoint (`ws://...`). Takes precedence over BrowserUrl.</summary>
    public string? WebSocketEndpoint { get; set; }

    /// <summary>If true (and no BrowserUrl/WebSocketEndpoint), launch a fresh Chrome under server control.</summary>
    public bool AutoLaunch { get; set; } = false;

    /// <summary>Path to a Chrome/Chromium binary. Used only when AutoLaunch=true. Empty = PuppeteerSharp downloads its own.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Launch Chrome without UI (only when AutoLaunch=true). Default true.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Optional persistent user-data directory. Only used when AutoLaunch=true. Empty = isolated temp profile.</summary>
    public string? UserDataDir { get; set; }

    /// <summary>Extra command-line flags appended when AutoLaunch=true.</summary>
    public List<string> ExtraChromeArgs { get; set; } = new();

    /// <summary>If true, ignore TLS certificate errors in the controlled browser.</summary>
    public bool AcceptInsecureCertificates { get; set; } = false;

    /// <summary>Default viewport width applied to new pages. 0 = use browser default.</summary>
    public int ViewportWidth { get; set; } = 1280;

    /// <summary>Default viewport height applied to new pages. 0 = use browser default.</summary>
    public int ViewportHeight { get; set; } = 800;

    /// <summary>Default deviceScaleFactor applied to new pages.</summary>
    public double DeviceScaleFactor { get; set; } = 1.0;

    /// <summary>Connection timeout (ms) for attach / launch.</summary>
    public int ConnectionTimeoutMs { get; set; } = 30_000;

    /// <summary>Per-action default timeout (ms) for clicks, navigations, waits.</summary>
    public int DefaultActionTimeoutMs { get; set; } = 30_000;

    /// <summary>Maximum console messages retained per page.</summary>
    public int MaxConsoleBuffer { get; set; } = 500;

    /// <summary>Maximum network entries retained per page.</summary>
    public int MaxNetworkBuffer { get; set; } = 500;

    /// <summary>When true, evaluate_script is disabled (defence-in-depth against arbitrary JS execution).</summary>
    public bool DisableScriptEvaluation { get; set; } = false;

    /// <summary>When true, all mutating tools (navigation, input, evaluate, emulation, cookies) are blocked. Default true.</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Comma-or-array list of header names that should be redacted in network reports (case-insensitive).</summary>
    public List<string> RedactedHeaders { get; set; } = new() { "authorization", "cookie", "set-cookie", "proxy-authorization", "x-api-key" };

    /// <summary>Feature toggle: navigation tools (navigate, go_back, reload, …).</summary>
    public bool EnableNavigation { get; set; } = true;

    /// <summary>Feature toggle: input tools (click, type, fill, hover, press_key, upload_file).</summary>
    public bool EnableInput { get; set; } = true;

    /// <summary>Feature toggle: inspection tools (snapshot, screenshot, get_content, get_url).</summary>
    public bool EnableInspection { get; set; } = true;

    /// <summary>Feature toggle: console tools.</summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>Feature toggle: network tools.</summary>
    public bool EnableNetwork { get; set; } = true;

    /// <summary>Feature toggle: emulation tools (viewport, user agent, geolocation, device emulation).</summary>
    public bool EnableEmulation { get; set; } = true;

    /// <summary>Feature toggle: performance tracing tools.</summary>
    public bool EnablePerformance { get; set; } = true;

    /// <summary>Feature toggle: cookie tools.</summary>
    public bool EnableCookies { get; set; } = true;

    /// <summary>Optional allow-list of URL prefixes the server may navigate to. Empty = no restriction.</summary>
    public List<string> AllowedUrlPrefixes { get; set; } = new();

    /// <summary>Optional deny-list of URL prefixes blocked from navigation. Evaluated after AllowedUrlPrefixes.</summary>
    public List<string> BlockedUrlPrefixes { get; set; } = new();

    /// <summary>Directory where screenshots/traces are written. Defaults to <TEMP>/ChromeDevToolsMCPSharp.</summary>
    public string? OutputDirectory { get; set; }
}

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5709;
    public string Path { get; set; } = "/mcp";

    /// <summary>Service name when running as a Windows Service.</summary>
    public string WindowsServiceName { get; set; } = "ChromeDevToolsMCPSharp";

    /// <summary>Optional MCP endpoint password. Blank disables MCP password auth.</summary>
    public string Password { get; set; } = string.Empty;
}
