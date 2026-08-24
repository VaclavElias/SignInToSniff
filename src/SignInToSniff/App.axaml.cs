using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SignInToSniff.ViewModels;
using SignInToSniff.Views;
using SignInToSniff.Proxy;
using SignInToSniff.Threading;
using SignInToSniff.Launching;
using SignInToSniff.Persistence;
using SignInToSniff.Exclusions;

namespace SignInToSniff;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ExclusionRule[] recommendedTlsPassthroughRules =
            [
                new("microsoftonline.com", ExclusionScope.DomainAndSubdomains),
                new("microsoftonline-p.com", ExclusionScope.DomainAndSubdomains),
                new("login.windows.net", ExclusionScope.ExactHost),
                new("login.microsoft.com", ExclusionScope.DomainAndSubdomains),
                new("login.live.com", ExclusionScope.ExactHost),
                new("account.live.com", ExclusionScope.ExactHost),
                new("msauth.net", ExclusionScope.DomainAndSubdomains),
                new("msftauth.net", ExclusionScope.DomainAndSubdomains),
                new("enterpriseregistration.windows.net", ExclusionScope.ExactHost),
                new("dropbox.com", ExclusionScope.DomainAndSubdomains),
                new("webex.com", ExclusionScope.DomainAndSubdomains)
            ];
            var tlsPassthroughRules = new HostRuleSet(new JsonExclusionStore(
                "https-passthrough.json",
                recommendedTlsPassthroughRules));
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    new TitaniumProxyEngine(tlsPassthroughRules),
                    new AvaloniaUiDispatcher(),
                    new WindowsClientLauncher(),
                    new JsonExclusionStore(),
                    tlsPassthroughRules)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
