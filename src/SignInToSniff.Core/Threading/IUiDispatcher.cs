namespace SignInToSniff.Threading;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
