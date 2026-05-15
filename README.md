# ChromeDevToolsMCPSharp

A standalone C# **MCP (Model Context Protocol) server** that drives **Chrome via the Chrome DevTools Protocol** over Streamable HTTP. Uses [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp) (.NET port of puppeteer-core) to attach to a running Chrome (`--remote-debugging-port`) or launch one under server control.

This server is independent of (but in the same product family as) [PlaywrightMCPSharp](../PlaywrightMCPSharp). The focus here is **attaching to a debuggable Chrome instance** and exposing the CDP-level tool surface — pages, console, network, performance, emulation, cookies — rather than launching a fresh automation profile.

## Features

- HTTP MCP server using the Streamable HTTP transport.
- **Read-only mode by default** — navigation, input, evaluation, emulation and cookie writes stay disabled until explicitly enabled.
- Attach to an existing Chrome via `BrowserUrl` (`http://host:9222`) or `WebSocketEndpoint`, or auto-launch a fresh Chrome.
- Stable per-page ids; bounded per-page console and network ring buffers.
- Configuration via `ChromeDevToolsMCPSharp.json`, environment variables, or command line.
- Serilog logging to console and rolling files (daily + 50 MB rollover, 14-file retention).
- Runs as a console app, Windows Service, or Docker container.

## Quick start

The server does not control Chrome on its own — it needs to be **told how to reach a Chrome instance**. Pick one of the three modes below before starting it. If you skip this step, every tool call returns `ChromeDevTools is not configured to attach to anything…`.

### Mode A — Attach to a Chrome you launched (recommended)

1. Start Chrome with a remote debugging port. **Use a dedicated profile** so it doesn't fight your normal browser.

   Windows (PowerShell):
   ```powershell
   & "C:\Program Files\Google\Chrome\Application\chrome.exe" `
       --remote-debugging-port=9222 `
       --user-data-dir="$env:LOCALAPPDATA\ChromeMCP"
   ```

   Linux / macOS:
   ```sh
   google-chrome --remote-debugging-port=9222 --user-data-dir="$HOME/.chrome-mcp"
   ```

2. Open [`ChromeDevToolsMCPSharp.json`](ChromeDevToolsMCPSharp.json) and set:
   ```json
   "ChromeDevTools": {
     "BrowserUrl": "http://localhost:9222",
     "ReadOnly": false
   }
   ```
   Leave `ReadOnly` as `true` if you only want inspection (console, network, screenshots) and no navigation/clicks.

3. Run the server:
   ```sh
   dotnet run
   ```

4. Point your MCP client at `http://localhost:5709/mcp`. Call `list_pages` first — that's the tool that triggers the connection. Any tab already open in Chrome will show up with an id like `p01`.

### Mode B — Let the server launch Chrome for you

In `ChromeDevToolsMCPSharp.json`:

```json
"ChromeDevTools": {
  "AutoLaunch": true,
  "Headless": true,
  "ReadOnly": false
}
```

On the first tool call, the server downloads a matching Chromium build (~150 MB, one-time, cached under `%USERPROFILE%\.cache\puppeteer` or `~/.cache/puppeteer`) and launches it. Point `ExecutablePath` at an existing Chrome to skip the download.

### Mode C — Direct WebSocket endpoint

If you already have the CDP WebSocket URL (visit `http://localhost:9222/json/version` to find it):

```json
"ChromeDevTools": { "WebSocketEndpoint": "ws://localhost:9222/devtools/browser/<uuid>" }
```

Takes precedence over `BrowserUrl`.

### Verify it's working

- `http://localhost:5709/healthz` returns JSON `{"status":"ok", …}` once the server is up.
- The startup log shows which mode the server resolved:
  ```
  ChromeDevToolsMCPSharp startup
    Endpoint: http://localhost:5709/mcp
    Chrome attach: http://localhost:9222
    Read-only: False
  ```
- Calling `list_pages` from your MCP client is the moment the connection actually opens. If Chrome isn't reachable at the configured URL, you'll get a clear error there.

## Tools

### Navigation
`list_pages`, `select_page`, `new_page`, `close_page`, `navigate_page`, `go_back`, `go_forward`, `reload_page`, `wait_for_selector`, `wait_for_navigation`, `handle_dialog`.

### Input
`click`, `click_at`, `hover`, `type_text`, `fill_input`, `fill_form`, `press_key`, `upload_file`.

### Inspection
`get_url`, `get_title`, `get_content`, `take_snapshot` (accessibility tree), `take_screenshot`, `query_selector_count`, `evaluate_script` (gated by `DisableScriptEvaluation`).

### Console
`list_console_messages` (with level/substring filters), `clear_console_messages`.

### Network
`list_network_requests` (with resource-type/url/failed filters), `get_network_request` (headers + body, with redaction), `clear_network_log`.

### Emulation
`resize_page`, `emulate_device` (built-in puppeteer device descriptors), `set_user_agent`, `set_geolocation`, `set_offline`.

### Performance
`performance_start_trace`, `performance_stop_trace`, `performance_metrics`.

### Cookies
`list_cookies`, `set_cookies`, `delete_cookies`.

## Configuration

Configure via `ChromeDevToolsMCPSharp.json` or environment variables. Environment variables win over JSON; in Docker, use the `CHROMEDEVMCP_` prefix and `__` for nested keys.

| Setting | Default | Description |
| --- | --- | --- |
| `ChromeDevTools:BrowserUrl` | _(none)_ | URL of a Chrome started with `--remote-debugging-port=…`. |
| `ChromeDevTools:WebSocketEndpoint` | _(none)_ | CDP WebSocket endpoint (takes precedence over BrowserUrl). |
| `ChromeDevTools:AutoLaunch` | `false` | If true and no URL/endpoint is set, launch a Chrome under server control. |
| `ChromeDevTools:ExecutablePath` | _(none)_ | Custom Chrome/Chromium binary (only when AutoLaunch=true). Empty = PuppeteerSharp downloads its own. |
| `ChromeDevTools:Headless` | `true` | Launch headless (only when AutoLaunch=true). |
| `ChromeDevTools:UserDataDir` | _(none)_ | Persistent profile directory (only when AutoLaunch=true). |
| `ChromeDevTools:ExtraChromeArgs` | `[]` | Extra command-line flags when AutoLaunch=true. |
| `ChromeDevTools:AcceptInsecureCertificates` | `false` | Ignore TLS errors in the controlled browser. |
| `ChromeDevTools:ViewportWidth` / `Height` / `DeviceScaleFactor` | `1280` / `800` / `1` | Default viewport applied to new pages. |
| `ChromeDevTools:ConnectionTimeoutMs` | `30000` | Attach/launch timeout. |
| `ChromeDevTools:DefaultActionTimeoutMs` | `30000` | Default timeout for clicks, navigations, waits. |
| `ChromeDevTools:MaxConsoleBuffer` / `MaxNetworkBuffer` | `500` | Per-page ring-buffer sizes. |
| `ChromeDevTools:DisableScriptEvaluation` | `false` | When true, `evaluate_script` is blocked outright. |
| `ChromeDevTools:ReadOnly` | `true` | When true, all mutating tools are blocked. |
| `ChromeDevTools:RedactedHeaders` | `[authorization, cookie, set-cookie, proxy-authorization, x-api-key]` | Headers redacted by `get_network_request`. |
| `ChromeDevTools:EnableNavigation` / `EnableInput` / `EnableInspection` / `EnableConsole` / `EnableNetwork` / `EnableEmulation` / `EnablePerformance` / `EnableCookies` | `true` | Per-category feature toggles. |
| `ChromeDevTools:AllowedUrlPrefixes` | `[]` | Allow-list of URL prefixes for navigation. Empty = no restriction. |
| `ChromeDevTools:BlockedUrlPrefixes` | `[]` | Deny-list of URL prefixes (evaluated after allow-list). |
| `ChromeDevTools:OutputDirectory` | _(temp)_ | Where screenshots/traces are written. Defaults to `<TEMP>/ChromeDevToolsMCPSharp`. |
| `Server:Host` | `localhost` | Host to bind. |
| `Server:Port` | `5709` | HTTP port. |
| `Server:Path` | `/mcp` | MCP endpoint path. |
| `Server:WindowsServiceName` | `ChromeDevToolsMCPSharp` | Service name when running under SCM. |
| `Server:Password` | blank | Optional MCP endpoint password. |

When `Server:Password` is set, MCP requests must provide the password as `Authorization: Bearer <password>`, the Basic auth password, or `X-MCP-Password`.

Arrays use numeric indexes, e.g. `CHROMEDEVMCP_ChromeDevTools__AllowedUrlPrefixes__0=https://app.example.com/`. Booleans use `true` or `false`.

## Setting connection options via environment variables

The Quick start uses `ChromeDevToolsMCPSharp.json` because it's the easiest. The same settings can be supplied as environment variables (handy for Docker and Windows Services):

```powershell
$env:CHROMEDEVMCP_ChromeDevTools__BrowserUrl = "http://localhost:9222"
$env:CHROMEDEVMCP_ChromeDevTools__ReadOnly  = "false"
dotnet run
```

Env vars win over the JSON file.

## Docker

Tagged releases publish a multi-arch image to GitHub Container Registry:

```sh
docker pull ghcr.io/wixely/chromedevtoolsmcpsharp:<version>
docker run --rm -p 5709:5709 \
  -e CHROMEDEVMCP_ChromeDevTools__BrowserUrl=http://host.docker.internal:9222 \
  -e CHROMEDEVMCP_ChromeDevTools__ReadOnly=false \
  -e CHROMEDEVMCP_Server__Password=change-me \
  ghcr.io/wixely/chromedevtoolsmcpsharp:<version>
```

The image targets `linux/amd64` and `linux/arm64`. **AutoLaunch in Docker is not recommended** unless the image is rebuilt to include Chrome dependencies (`libnss3`, `libatk-bridge2.0-0`, fonts, etc.); the default behaviour is to attach to an external Chrome.

## Running as a Windows Service

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\ChromeDevToolsMCPSharp

sc.exe create ChromeDevToolsMCPSharp `
    binPath= "C:\Services\ChromeDevToolsMCPSharp\ChromeDevToolsMCPSharp.exe" `
    start= auto `
    DisplayName= "Chrome DevTools MCP (C#)"
sc.exe description ChromeDevToolsMCPSharp "MCP server driving Chrome via CDP."
sc.exe start ChromeDevToolsMCPSharp
```

Logs land in `<install-dir>\logs\chromedevmcp-*.log`.

## Safety model

- **Read-only by default.** All mutating tools call `EnsureWriteAllowed` and fail with a clear error naming the config key.
- **Script evaluation gate.** `evaluate_script` can be blocked outright with `ChromeDevTools:DisableScriptEvaluation=true`.
- **Feature toggles** for every category (Navigation, Input, Inspection, Console, Network, Emulation, Performance, Cookies).
- **URL allow/deny lists** for navigation.
- **Sensitive header redaction** in network reports.
- **Bounded buffers** for console messages and network records.
- **Endpoint password** (`Server:Password`) to gate the MCP transport.
