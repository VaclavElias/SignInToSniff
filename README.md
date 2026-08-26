# SignInToSniff

SignInToSniff is a staged, cross-platform desktop HTTP(S) traffic inspector built with .NET and Avalonia.

<img width="958" height="820" alt="image" src="https://github.com/user-attachments/assets/7e05b002-6bcb-496a-9caa-cf94c9dfdfaf" />

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
4. Select a captured request to inspect its URL, headers, body, status, response size, and duration. JSON request and response bodies provide selectable syntax-coloured, collapsible tree, and raw views. URL-encoded and multipart form bodies provide decoded field tables plus raw views. Small image responses are rendered directly in the Response Body pane.
5. Use global search, ordering options, auto-scroll, and the draggable Headers/Body splitter as needed. Search terms are combined with AND, so `google.com POST json` requires all three terms to match across the enabled fields. Use the search cog to include or exclude host, URL, file extension, method/status, headers, bodies, or metadata; its adaptive **Select all/Deselect all** action changes every scope at once. Select only **File extension** to find resources such as `JPG` regardless of letter case; query-string values are ignored for this scope.
6. Use the three-dot menu beside either Body heading to copy or download the displayed body. Image downloads preserve the original response bytes and use an extension based on the response content type.
7. Right-click a captured request to delete it, exclude its exact host, or exclude its site domain and all subdomains.
8. Open **Tools -> Manage exclusions** to add, review, or remove persistent exclusion rules.
9. Click **Stop** when finished.

The full-width footer reports total requests captured during the current app session, requests hidden by the active domain filter or exclusion rules, and the number of saved exclusion rules.

It also reports active client/server connection counts. Each request row shows the negotiated HTTP protocol, and the selected request shows bytes received/sent after completion. Proxy failures appear in the detail pane. Press **Delete** while the request list is focused to remove the selected request. Titanium diagnostic warnings and errors are written to `SignInToSniff/logs/proxy.log` under the current user's local application-data directory.

### Fresh Chrome

Choose **Intercept -> Fresh Chrome** to open an independent Chrome window already configured for interception. It uses a disposable profile and does not affect your normal Chrome profile or permanent Chrome settings.

### HTTPS inspection

1. Stop the proxy if it is running.
2. Open **Certificate** and choose **Install for current user**. Read and accept the warning only on a device you control. Machine-wide installation is also available on Windows and requires UAC elevation.
3. Start the proxy again, then launch Fresh Chrome.
4. Visit an `https://` page and confirm its requests, headers, and text bodies appear.
5. When HTTPS inspection is no longer wanted, stop the proxy and use the matching **Remove … trust** command. Machine-wide removal requires UAC on Windows.

The generated root certificate and private key are persisted in the current user's local application-data directory so the same trusted identity is reused across restarts. Trust-store changes are never performed at startup or without the confirmation dialog.

### HTTPS passthrough

Open **Tools -> Manage HTTPS passthrough** to review or edit hosts that should travel through the proxy without TLS decryption. SignInToSniff seeds recommended Microsoft identity, Dropbox, and Webex rules because those services may use certificate pinning or OS authentication flows that do not tolerate interception. Passthrough traffic continues normally, but its inner HTTPS headers and bodies cannot be captured. Customizations are saved in `SignInToSniff/https-passthrough.json` under local application data. You can also right-click a captured request to add its exact host or site domain for future connections.

### Exclusions

Exclusions hide matching traffic from the capture list; they do not block or reroute the request. Hidden requests remain in the bounded in-memory session so removing a rule restores them during the same app session. The exclusions window shows how many retained requests each rule currently hides. **Exact host** matches only the named host. **Domain and subdomains** matches the named site domain and every host below it. Rules are stored in `SignInToSniff/exclusions.json` under the current user's local application-data directory and restored when the app starts.

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

Text body capture is limited to 1 MiB per request or response. Image responses with a declared size up to 1 MiB are retained for a downscaled preview; images with an unknown or larger declared size and other known binary or streaming bodies are intentionally omitted. SignInToSniff does not modify the Windows system proxy, and clients will lose proxy connectivity if they remain configured after SignInToSniff stops.

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
14. Visit a direct PNG or JPEG URL smaller than 1 MiB, select its request, and confirm the image appears under **Response -> Body**. Use its three-dot menu to download the original image.
15. Send or receive an `application/json` or `application/*+json` body. Confirm the Body pane offers **Syntax**, **Tree**, and **Raw** tabs; expand nested tree nodes and verify strings, numbers, booleans, and null values are distinguishable.
16. From Fresh Terminal, send URL-encoded and multipart forms and confirm **Fields** shows decoded, repeated values and uploaded-file metadata while **Raw** preserves the captured payload:

```powershell
curl.exe https://httpbin.org/post -d "name=SignInToSniff&tag=proxy&tag=C%23+Avalonia"
curl.exe https://httpbin.org/post -F "description=viewer test" -F "upload=hello from SignInToSniff;filename=sample.txt;type=text/plain"
```

The disabled Main Chrome option is reserved for a later implementation that must show a confirmation before closing or relaunching the normal profile.

### Automated checks

```powershell
dotnet build SignInToSniff.slnx
dotnet test --project tests/SignInToSniff.Tests/SignInToSniff.Tests.csproj
```

The current prototype includes HTTPS diagnostics, persistent passthrough rules, bounded image-response previews, JSON syntax/tree viewers, and URL-encoded/multipart form viewers. HTML/XML and binary metadata viewers are the next content-viewer stages.
