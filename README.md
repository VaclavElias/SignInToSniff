# SignInToSniff

SignInToSniff is a staged, cross-platform desktop HTTP(S) traffic inspector built with .NET and Avalonia.

## Current milestone: HTTP metadata capture

The app can deliberately start an HTTP proxy on `127.0.0.1:8000` and display request/response metadata and headers. It never changes system proxy settings. **HTTPS decryption, body capture, and certificate changes are not enabled yet.**

### Run

```powershell
dotnet restore SignInToSniff.slnx
dotnet run --project src/SignInToSniff/SignInToSniff.csproj
```

### Manual verification

1. Click **Start Proxy** and confirm the status changes to running; Stop becomes available.
2. Configure an HTTP client to use `127.0.0.1:8000` and request an `http://` URL.
3. Confirm the request appears, then select it and inspect request/response headers and status.
4. Select and copy all or part of the URL above the tabs.
5. Verify filtering, clearing, time-column visibility, ordering, and auto-scroll in both directions.
6. Click **Stop** and confirm traffic is no longer accepted on port 8000.
7. Start a second process on port 8000 and confirm SignInToSniff reports a useful startup error.
8. Confirm Certificate remains disabled and no certificate-store or system-proxy settings change.

### Automated checks

```powershell
dotnet build SignInToSniff.slnx
dotnet test tests/SignInToSniff.Tests/SignInToSniff.Tests.csproj
```

Example PowerShell traffic through the running proxy:

```powershell
Invoke-WebRequest http://example.com -Proxy http://127.0.0.1:8000
```

Milestone 3 will add bounded textual request/response body capture and formatting.
