namespace SignInToSniff.Launching;

public sealed record ClientLaunchResult(bool Succeeded, string? ErrorMessage = null);

public interface IClientLauncher
{
    Task<ClientLaunchResult> LaunchFreshChromeAsync(string proxyEndpoint);

    Task<ClientLaunchResult> LaunchFreshTerminalAsync(string proxyEndpoint);
}
