# SignInToSniff

SignInToSniff is a staged, cross-platform desktop HTTP(S) traffic inspector built with .NET and Avalonia.

## Current milestone: opt-in HTTPS inspection

The app can deliberately start an HTTP(S) proxy on `127.0.0.1:8000` and display request/response metadata, headers, and bounded text bodies. JSON is formatted for readability, and gzip, Brotli, and deflate response bodies are decoded for display. It never changes system proxy settings. HTTPS decryption is strictly opt-in and requires explicit certificate installation confirmation.

### Run

```powershell
dotnet restore SignInToSniff.slnx
dotnet run --project src/SignInToSniff/SignInToSniff.csproj
```

## Manual

### Basic usage

1. Launch SignInToSniff and click **Start Proxy**.
2. Open **Intercept** and choose a client, or configure your own HTTP client to use `127.0.0.1:8000`.
3. Browse or send requests to an `http://` address.
4. Select a captured request to inspect its URL, headers, body, status, response size, and duration.
5. Use the domain filter, ordering options, auto-scroll toggle, and draggable Headers/Body splitter as needed.
6. Use the three-dot menu beside either Body heading to copy or download the displayed body.
7. Click **Stop** when finished.

### Fresh Chrome

Choose **Intercept -> Fresh Chrome** to open an independent Chrome window already configured for interception. It uses a disposable profile and does not affect your normal Chrome profile or permanent Chrome settings.

### HTTPS inspection

1. Stop the proxy if it is running.
2. Open **Certificate** and choose **Install for current user**. Read and accept the warning only on a device you control. Machine-wide installation is also available on Windows and requires UAC elevation.
3. Start the proxy again, then launch Fresh Chrome.
4. Visit an `https://` page and confirm its requests, headers, and text bodies appear.
5. When HTTPS inspection is no longer wanted, stop the proxy and use the matching **Remove … trust** command. Machine-wide removal requires UAC on Windows.

The generated root certificate and private key are persisted in the current user's local application-data directory so the same trusted identity is reused across restarts. Trust-store changes are never performed at startup or without the confirmation dialog.

### Fresh Terminal

Choose **Intercept -> Fresh Terminal** to open a new Windows Terminal window using your configured default profile. Proxy environment variables apply only to that terminal and programs launched from it.

Test the terminal without specifying a proxy manually:

```powershell
curl.exe http://httpforever.com/
```

### Manually configured clients

Set the client's HTTP proxy to `http://127.0.0.1:8000`. For example, from a regular PowerShell window:

```powershell
Invoke-WebRequest http://example.com -Proxy http://127.0.0.1:8000
```

Text body capture is limited to 1 MiB per request or response; known binary, streaming, and oversized bodies are intentionally omitted. SignInToSniff does not modify the Windows system proxy, and clients will lose proxy connectivity if they remain configured after SignInToSniff stops.

### Manual verification

1. Click **Start Proxy** and confirm the status changes to running; Stop becomes available.
2. Configure an HTTP client to use `127.0.0.1:8000` and request an `http://` URL.
3. Confirm the request appears, then select it and inspect request/response headers and status.
4. Select and copy all or part of the URL above the tabs.
5. Verify filtering, clearing, time-column visibility, ordering, and auto-scroll in both directions.
6. Click **Stop** and confirm traffic is no longer accepted on port 8000.
7. Start a second process on port 8000 and confirm SignInToSniff reports a useful startup error.
8. Confirm Certificate remains disabled and no certificate-store or system-proxy settings change.
9. With the proxy running, use **Intercept → Fresh Chrome** and confirm an isolated Chrome window opens through the proxy.
10. Use **Intercept → Fresh Terminal** and confirm Windows Terminal opens its default profile; tools launched there inherit temporary HTTP proxy variables.
11. Drag the horizontal splitter between Headers and Body in both detail tabs and confirm either section can be expanded.
12. Visit an HTTP page and confirm its HTML appears under **Response -> Body**.
13. From Fresh Terminal, send a small HTTP POST and confirm its payload appears under **Request -> Body**.

The disabled Main Chrome option is reserved for a later implementation that must show a confirmation before closing or relaunching the normal profile.

### Automated checks

```powershell
dotnet build SignInToSniff.slnx
dotnet test tests/SignInToSniff.Tests/SignInToSniff.Tests.csproj
```

The next milestone will refine HTTPS diagnostics, certificate lifecycle handling, and cross-platform trust guidance after manual verification.
