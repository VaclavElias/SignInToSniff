using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SignInToSniff.ViewModels;
using SignInToSniff.Views;
using SignInToSniff.Proxy;
using SignInToSniff.Threading;

namespace SignInToSniff;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new TitaniumProxyEngine(), new AvaloniaUiDispatcher())
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
