using System.Diagnostics;
using SignInToSniff.Launching;

namespace SignInToSniff.Launching;

public sealed class WindowsClientLauncher : IClientLauncher
{
    public Task<ClientLaunchResult> LaunchFreshChromeAsync(string proxyEndpoint)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ClientLaunchResult(false, "Fresh Chrome launch is currently implemented for Windows only."));
        }

        var chromePath = FindChromePath();
        if (chromePath is null)
        {
            return Task.FromResult(new ClientLaunchResult(false, "Google Chrome was not found in a standard installation location."));
        }

        try
        {
            var profileRoot = Path.Combine(Path.GetTempPath(), "SignInToSniff", "ChromeProfiles");
            var profilePath = Path.Combine(profileRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profilePath);

            var startInfo = new ProcessStartInfo(chromePath)
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add($"--proxy-server=http://{proxyEndpoint}");
            startInfo.ArgumentList.Add($"--user-data-dir={profilePath}");
            startInfo.ArgumentList.Add("--new-window");
            startInfo.ArgumentList.Add("http://httpforever.com/");

            var process = Process.Start(startInfo);
            if (process is null)
            {
                TryDeleteTemporaryProfile(profileRoot, profilePath);
                return Task.FromResult(new ClientLaunchResult(false, "Chrome could not be started."));
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                process.Dispose();
                TryDeleteTemporaryProfile(profileRoot, profilePath);
            };

            return Task.FromResult(new ClientLaunchResult(true));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new ClientLaunchResult(false, $"Chrome could not be started: {exception.Message}"));
        }
    }

    public Task<ClientLaunchResult> LaunchFreshTerminalAsync(string proxyEndpoint)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ClientLaunchResult(false, "Fresh Terminal launch is currently implemented for Windows only."));
        }

        try
        {
            var proxyUrl = $"http://{proxyEndpoint}";
            var startInfo = new ProcessStartInfo("wt.exe")
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("new");
            startInfo.ArgumentList.Add("new-tab");
            startInfo.ArgumentList.Add("--inheritEnvironment");
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add("SignInToSniff Intercepted");

            foreach (var variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
            {
                startInfo.Environment[variable] = proxyUrl;
            }

            foreach (var variable in new[] { "NO_PROXY", "no_proxy" })
            {
                startInfo.Environment[variable] = "localhost,127.0.0.1,::1";
            }

            return Task.FromResult(Process.Start(startInfo) is not null
                ? new ClientLaunchResult(true)
                : new ClientLaunchResult(false, "Windows Terminal could not be started."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new ClientLaunchResult(false,
                $"Windows Terminal could not be started. Check that wt.exe is installed and enabled: {exception.Message}"));
        }
    }

    private static string? FindChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryDeleteTemporaryProfile(string profileRoot, string profilePath)
    {
        try
        {
            var resolvedRoot = Path.GetFullPath(profileRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolvedProfile = Path.GetFullPath(profilePath);
            if (resolvedProfile.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolvedProfile))
            {
                Directory.Delete(resolvedProfile, recursive: true);
            }
        }
        catch
        {
            // Chrome can keep helper processes alive briefly. The OS can clean abandoned temp data later.
        }
    }
}
