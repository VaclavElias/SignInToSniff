using Avalonia.Threading;
using SignInToSniff.Threading;

namespace SignInToSniff.Threading;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
