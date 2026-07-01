# Third-Party Notices

ChromeDevToolsMCPSharp is licensed under the MIT License (see `LICENSE`). It
depends on the third-party components listed below. Each remains under its own
license; this file is provided for attribution.

## NuGet packages

| Package | License |
| --- | --- |
| Microsoft.AspNetCore.App | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | MIT |
| PuppeteerSharp | MIT |
| ModelContextProtocol.AspNetCore | Apache-2.0 |
| Serilog.AspNetCore | Apache-2.0 |
| Serilog.Enrichers.Environment | Apache-2.0 |
| Serilog.Enrichers.Process | Apache-2.0 |
| Serilog.Enrichers.Thread | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |

The full text of the MIT and Apache-2.0 licenses is available at
<https://opensource.org/license/mit> and
<https://www.apache.org/licenses/LICENSE-2.0> respectively.

## Chromium

ChromeDevToolsMCPSharp does **not** ship Chromium (or any other browser binary)
as part of this project. When `ChromeDevTools:AutoLaunch=true`, PuppeteerSharp
downloads a matching Chromium build onto the host at runtime (cached under
`%USERPROFILE%\.cache\puppeteer` or `~/.cache/puppeteer`), or the server
connects to a browser you provide via `ExecutablePath` / a running CDP endpoint.
Chromium is licensed under BSD-3-Clause (with the licenses of its bundled
components). When downloaded this way it is not distributed by this project.

If you choose to bundle a browser binary into a distributable artifact of your
own (for example a Docker image or a self-contained package), you are
responsible for complying with its respective licence and any required
attribution.

## Trademarks

"Chrome", "Chromium", and "Chrome DevTools" are trademarks of Google LLC. Use of
these names in this project does not imply endorsement by, or affiliation with,
Google.
