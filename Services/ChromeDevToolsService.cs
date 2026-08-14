using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ChromeDevToolsMCPSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using PuppeteerSharp;

namespace ChromeDevToolsMCPSharp.Services;

/// <summary>
/// Manages a single Chrome attachment (or auto-launched instance) for the lifetime of the MCP
/// server. Provides a stable id for each page, bounded console + network ring buffers, and
/// uniform safety gates that every tool can call.
/// </summary>
public sealed class ChromeDevToolsService : IAsyncDisposable
{
    private readonly ChromeDevToolsOptions _options;
    private readonly ILogger<ChromeDevToolsService> _log;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PageEntry> _pages = new();
    private IBrowser? _browser;
    private string? _currentPageId;
    private bool _disposed;

    public ChromeDevToolsService(IOptions<ChromeDevToolsOptions> options, ILogger<ChromeDevToolsService> log)
    {
        _options = options.Value;
        _log = log;
    }

    public ChromeDevToolsOptions Options => _options;
    public bool IsReadOnly => _options.ReadOnly;

    public void EnsureWriteAllowed(string operation)
    {
        if (_options.ReadOnly)
        {
            throw new McpException(
                $"MCP tool '{operation}' is blocked by server configuration. " +
                "Set ChromeDevTools:ReadOnly=false to allow this action.");
        }
    }

    public void EnsureFeature(bool flag, string category, string operation)
    {
        if (!flag)
        {
            throw new McpException(
                $"MCP tool '{operation}' is disabled: ChromeDevTools:Enable{category}=false.");
        }
    }

    public void EnsureUrlAllowed(string url)
    {
        if (_options.AllowedUrlPrefixes.Count > 0 &&
            !_options.AllowedUrlPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            throw new McpException($"URL '{url}' is not in ChromeDevTools:AllowedUrlPrefixes.");
        }
        if (_options.BlockedUrlPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            throw new McpException($"URL '{url}' is blocked by ChromeDevTools:BlockedUrlPrefixes.");
        }
    }

    public string OutputDirectory
    {
        get
        {
            var dir = string.IsNullOrWhiteSpace(_options.OutputDirectory)
                ? Path.Combine(Path.GetTempPath(), "ChromeDevToolsMCPSharp")
                : _options.OutputDirectory!;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true })
            return _browser;

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            _browser?.Dispose();
            _browser = await ConnectOrLaunchAsync(ct);
            HookBrowser(_browser);
            // Adopt any pages that already exist on the attached browser.
            foreach (var page in await _browser.PagesAsync())
            {
                Register(page);
            }
            return _browser;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Attaches (or re-attaches) to a debuggable Chrome at the given CDP WebSocket endpoint,
    /// replacing any current attachment. An existing connection is disconnected, never killed —
    /// the browser may not be owned by this server.
    /// </summary>
    public async Task<IBrowser> AttachToWebSocketAsync(string wsEndpoint, CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true })
            {
                _browser.Disconnect();
            }
            _log.LogInformation("Attaching to Chrome via WS endpoint {Endpoint}", wsEndpoint);
            _browser = await Puppeteer.ConnectAsync(new ConnectOptions
            {
                BrowserWSEndpoint = wsEndpoint,
                AcceptInsecureCerts = _options.AcceptInsecureCertificates,
                ProtocolTimeout = Math.Max(1_000, _options.ConnectionTimeoutMs),
                DefaultViewport = BuildDefaultViewport(),
            });
            HookBrowser(_browser);
            foreach (var page in await _browser.PagesAsync())
            {
                Register(page);
            }
            return _browser;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<PageEntry> GetPageAsync(string? pageId, CancellationToken ct)
    {
        await GetBrowserAsync(ct);
        var id = pageId ?? _currentPageId;
        if (string.IsNullOrWhiteSpace(id))
        {
            // Pick the first attached page (or fail clearly).
            var first = _pages.Values.FirstOrDefault();
            if (first is null)
                throw new McpException("No pages are attached. Call new_page or navigate_page first.");
            _currentPageId = first.Id;
            return first;
        }
        if (!_pages.TryGetValue(id, out var entry))
            throw new McpException($"Unknown page id '{id}'. Call list_pages.");
        return entry;
    }

    public IReadOnlyCollection<PageEntry> Pages => _pages.Values.ToArray();

    public void SetCurrent(string pageId)
    {
        if (!_pages.ContainsKey(pageId))
            throw new McpException($"Unknown page id '{pageId}'.");
        _currentPageId = pageId;
    }

    public string? CurrentPageId => _currentPageId;

    public PageEntry Register(IPage page)
    {
        var existing = _pages.Values.FirstOrDefault(p => ReferenceEquals(p.Page, page));
        if (existing is not null) return existing;

        var id = $"p{_pages.Count + 1:D2}";
        while (_pages.ContainsKey(id))
        {
            id = $"p{Random.Shared.Next(1000, 9999)}";
        }
        var entry = new PageEntry(id, page, _options.MaxConsoleBuffer, _options.MaxNetworkBuffer);
        if (!_pages.TryAdd(id, entry))
            return _pages[id];

        page.Console += entry.OnConsole;
        page.Request += entry.OnRequest;
        page.Response += entry.OnResponse;
        page.RequestFailed += entry.OnRequestFailed;
        page.RequestFinished += entry.OnRequestFinished;
        page.Close += (_, _) => Unregister(entry);
        page.Dialog += entry.OnDialog;

        _currentPageId ??= id;
        _log.LogInformation("Registered page {Id} {Url}", id, SafeUrl(page));
        return entry;
    }

    private void Unregister(PageEntry entry)
    {
        if (_pages.TryRemove(entry.Id, out _))
        {
            entry.Detach();
            if (_currentPageId == entry.Id)
            {
                _currentPageId = _pages.Keys.FirstOrDefault();
            }
            _log.LogInformation("Unregistered page {Id}", entry.Id);
        }
    }

    private void HookBrowser(IBrowser browser)
    {
        browser.TargetCreated += async (_, e) =>
        {
            try
            {
                if (e.Target.Type == TargetType.Page)
                {
                    var page = await e.Target.PageAsync();
                    if (page is not null) Register(page);
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "TargetCreated handler failed"); }
        };
        browser.Disconnected += (_, _) =>
        {
            foreach (var entry in _pages.Values.ToArray())
            {
                Unregister(entry);
            }
            _log.LogWarning("Browser disconnected");
        };
    }

    private ViewPortOptions? BuildDefaultViewport() =>
        (_options.ViewportWidth > 0 && _options.ViewportHeight > 0)
            ? new ViewPortOptions
            {
                Width = _options.ViewportWidth,
                Height = _options.ViewportHeight,
                DeviceScaleFactor = _options.DeviceScaleFactor,
            }
            : null;

    private async Task<IBrowser> ConnectOrLaunchAsync(CancellationToken ct)
    {
        var timeout = Math.Max(1_000, _options.ConnectionTimeoutMs);
        var defaultViewport = BuildDefaultViewport();

        if (!string.IsNullOrWhiteSpace(_options.WebSocketEndpoint))
        {
            _log.LogInformation("Attaching to Chrome via WS endpoint");
            return await Puppeteer.ConnectAsync(new ConnectOptions
            {
                BrowserWSEndpoint = _options.WebSocketEndpoint,
                AcceptInsecureCerts = _options.AcceptInsecureCertificates,
                ProtocolTimeout = timeout,
                DefaultViewport = defaultViewport,
            });
        }
        if (!string.IsNullOrWhiteSpace(_options.BrowserUrl))
        {
            _log.LogInformation("Attaching to Chrome via browser URL {Url}", _options.BrowserUrl);
            return await Puppeteer.ConnectAsync(new ConnectOptions
            {
                BrowserURL = _options.BrowserUrl,
                AcceptInsecureCerts = _options.AcceptInsecureCertificates,
                ProtocolTimeout = timeout,
                DefaultViewport = defaultViewport,
            });
        }
        if (!_options.AutoLaunch)
        {
            throw new McpException(
                "ChromeDevTools is not configured to attach to anything. Set ChromeDevTools:BrowserUrl, " +
                "ChromeDevTools:WebSocketEndpoint, or ChromeDevTools:AutoLaunch=true.");
        }

        _log.LogInformation("Launching Chrome (AutoLaunch=true, Headless={Headless})", _options.Headless);
        var launchOptions = new LaunchOptions
        {
            Headless = _options.Headless,
            ExecutablePath = string.IsNullOrWhiteSpace(_options.ExecutablePath) ? null : _options.ExecutablePath,
            UserDataDir = string.IsNullOrWhiteSpace(_options.UserDataDir) ? null : _options.UserDataDir,
            AcceptInsecureCerts = _options.AcceptInsecureCertificates,
            Timeout = timeout,
            DefaultViewport = defaultViewport,
            Args = _options.ExtraChromeArgs.ToArray(),
        };

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            // Download an aligned Chromium if a custom path was not supplied.
            var fetcher = new BrowserFetcher();
            await fetcher.DownloadAsync();
        }
        return await Puppeteer.LaunchAsync(launchOptions);
    }

    private static string SafeUrl(IPage page)
    {
        try { return page.Url; } catch { return "(unknown)"; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _pages.Values) entry.Detach();
        _pages.Clear();
        if (_browser is not null)
        {
            try { await _browser.DisposeAsync(); }
            catch (Exception ex) { _log.LogDebug(ex, "Browser dispose failed"); }
        }
    }
}

public sealed class PageEntry
{
    private readonly object _lock = new();
    private readonly LinkedList<ConsoleRecord> _console = new();
    private readonly LinkedList<NetworkRecord> _network = new();
    private readonly Dictionary<string, NetworkRecord> _byRequestId = new();
    private readonly int _maxConsole;
    private readonly int _maxNetwork;
    private Dialog? _lastDialog;

    public PageEntry(string id, IPage page, int maxConsole, int maxNetwork)
    {
        Id = id;
        Page = page;
        _maxConsole = Math.Max(50, maxConsole);
        _maxNetwork = Math.Max(50, maxNetwork);
    }

    public string Id { get; }
    public IPage Page { get; }

    public IReadOnlyList<ConsoleRecord> Console
    {
        get { lock (_lock) { return _console.ToArray(); } }
    }

    public IReadOnlyList<NetworkRecord> Network
    {
        get { lock (_lock) { return _network.ToArray(); } }
    }

    public Dialog? LastDialog => _lastDialog;

    public void ClearConsole() { lock (_lock) { _console.Clear(); } }
    public void ClearNetwork()
    {
        lock (_lock)
        {
            _network.Clear();
            _byRequestId.Clear();
        }
    }

    internal void OnConsole(object? _, ConsoleEventArgs e)
    {
        try
        {
            var record = new ConsoleRecord(
                Type: e.Message.Type.ToString(),
                Text: e.Message.Text ?? string.Empty,
                Url: e.Message.Location?.URL,
                LineNumber: e.Message.Location?.LineNumber,
                ColumnNumber: e.Message.Location?.ColumnNumber,
                TimeUtc: DateTimeOffset.UtcNow);
            lock (_lock)
            {
                _console.AddLast(record);
                while (_console.Count > _maxConsole) _console.RemoveFirst();
            }
        }
        catch { /* never let event handlers throw */ }
    }

    internal void OnRequest(object? _, RequestEventArgs e)
    {
        var req = e.Request;
        var record = new NetworkRecord
        {
            RequestId = string.IsNullOrEmpty(req.Id) ? SafeId(req) : req.Id,
            Method = req.Method.ToString(),
            Url = req.Url,
            ResourceType = req.ResourceType.ToString(),
            StartedUtc = DateTimeOffset.UtcNow,
            Status = null,
            FromCache = false,
            LiveRequest = req,
        };
        lock (_lock)
        {
            _byRequestId[record.RequestId] = record;
            _network.AddLast(record);
            while (_network.Count > _maxNetwork)
            {
                var first = _network.First!.Value;
                _network.RemoveFirst();
                _byRequestId.Remove(first.RequestId);
            }
        }
    }

    internal void OnResponse(object? _, ResponseCreatedEventArgs e)
    {
        var res = e.Response;
        var id = string.IsNullOrEmpty(res.Request.Id) ? SafeId(res.Request) : res.Request.Id;
        lock (_lock)
        {
            if (_byRequestId.TryGetValue(id, out var rec))
            {
                rec.Status = (int)res.Status;
                rec.FromCache = res.FromCache;
                rec.MimeType = TryGet(res.Headers, "content-type");
            }
        }
    }

    internal void OnRequestFailed(object? _, RequestEventArgs e)
    {
        var id = string.IsNullOrEmpty(e.Request.Id) ? SafeId(e.Request) : e.Request.Id;
        lock (_lock)
        {
            if (_byRequestId.TryGetValue(id, out var rec))
            {
                rec.Failure = e.Request.FailureText;
                rec.CompletedUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    internal void OnRequestFinished(object? _, RequestEventArgs e)
    {
        var id = string.IsNullOrEmpty(e.Request.Id) ? SafeId(e.Request) : e.Request.Id;
        lock (_lock)
        {
            if (_byRequestId.TryGetValue(id, out var rec))
            {
                rec.CompletedUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    internal void OnDialog(object? _, DialogEventArgs e)
    {
        _lastDialog = e.Dialog;
    }

    internal void Detach()
    {
        try
        {
            Page.Console -= OnConsole;
            Page.Request -= OnRequest;
            Page.Response -= OnResponse;
            Page.RequestFailed -= OnRequestFailed;
            Page.RequestFinished -= OnRequestFinished;
            Page.Dialog -= OnDialog;
        }
        catch { /* page may already be closed */ }
    }

    private static string SafeId(IRequest req)
    {
        // PuppeteerSharp's IRequest doesn't expose a stable CDP id publicly; URL+method+counter
        // would race under high traffic. Hash code is good enough for tying response→request
        // within a single page session.
        return req.GetHashCode().ToString("x");
    }

    private static string? TryGet(IDictionary<string, string>? headers, string name)
    {
        if (headers is null) return null;
        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }
}

public sealed record ConsoleRecord(
    string Type,
    string Text,
    string? Url,
    int? LineNumber,
    int? ColumnNumber,
    DateTimeOffset TimeUtc);

public sealed class NetworkRecord
{
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public int? Status { get; set; }
    public bool FromCache { get; set; }
    public string? MimeType { get; set; }
    public string? Failure { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IRequest? LiveRequest { get; set; }

    public double? DurationMs => CompletedUtc.HasValue
        ? (CompletedUtc.Value - StartedUtc).TotalMilliseconds
        : null;
}
