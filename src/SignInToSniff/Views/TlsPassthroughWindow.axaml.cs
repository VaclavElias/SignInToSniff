using Avalonia.Controls;
using Avalonia.Interactivity;
using SignInToSniff.Exclusions;
using SignInToSniff.ViewModels;

namespace SignInToSniff.Views;

public sealed partial class TlsPassthroughWindow : Window
{
    public TlsPassthroughWindow() => InitializeComponent();

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var domain = DomainInput.Text?.Trim() ?? string.Empty;
        if (Uri.CheckHostName(domain.TrimStart('*', '.')) == UriHostNameType.Unknown)
        {
            ValidationMessage.Text = "Enter a valid host or domain.";
            return;
        }
        var scope = ExactHostOption.IsChecked == true ? ExclusionScope.ExactHost : ExclusionScope.DomainAndSubdomains;
        await viewModel.AddTlsPassthroughRuleAsync(domain, scope);
        DomainInput.Clear();
        ValidationMessage.Text = string.Empty;
    }

    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Button { DataContext: ExclusionRule rule })
        {
            await viewModel.RemoveTlsPassthroughRuleAsync(rule);
        }
    }
}
