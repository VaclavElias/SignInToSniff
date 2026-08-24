# SignInToSniff

SignInToSniff is a staged, cross-platform desktop HTTP(S) traffic inspector built with .NET and Avalonia.

## Current milestone: UI shell

The first milestone is intentionally UI-only. It contains sample traffic so the request list, filtering, selection, request/response detail tabs, clearing, and auto-scroll behavior can be verified before live proxy traffic is introduced. **No network interception or certificate changes occur yet.**

### Run

```powershell
dotnet restore SignInToSniff.slnx
dotnet run --project src/SignInToSniff/SignInToSniff.csproj
```

### Manual verification

1. Confirm the application opens in a polished light theme and remains usable at its minimum window size.
2. Select each sample request, switch between the Request and Response tabs, and select/copy all or part of the URL above the tabs.
3. Filter for `api.example.com`, then use the × button to clear the filter.
4. Click **Add demo request** with auto-scroll enabled and disabled; disabling it must preserve both the scroll position and current selection.
5. Click **Clear logs** and confirm the empty state appears.
6. Confirm Start Proxy, Stop, and Certificate are disabled for this milestone.
7. In **View**, hide and restore the Time column, then enable **Show newest requests first** and confirm new demo requests appear at the top.

### Automated checks

```powershell
dotnet build SignInToSniff.slnx
dotnet test tests/SignInToSniff.Tests/SignInToSniff.Tests.csproj
```

Milestone 2 will remove the demo traffic and add deliberate Start/Stop controls for metadata-only HTTP capture on `127.0.0.1:8000`.
