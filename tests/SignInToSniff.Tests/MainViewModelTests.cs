using SignInToSniff.ViewModels;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void DomainFilter_MatchesHostCaseInsensitively()
    {
        var viewModel = new MainViewModel();

        viewModel.DomainFilter = "API.EXAMPLE.COM";

        Assert.Single(viewModel.Sessions);
        Assert.Equal("api.example.com", viewModel.Sessions[0].Host);
    }

    [Fact]
    public void ClearLogs_RemovesSessionsAndSelection()
    {
        var viewModel = new MainViewModel();

        viewModel.ClearLogsCommand.Execute(null);

        Assert.Empty(viewModel.Sessions);
        Assert.Null(viewModel.SelectedSession);
        Assert.False(viewModel.HasSessions);
    }

    [Fact]
    public void AddSample_RespectsActiveFilter()
    {
        var viewModel = new MainViewModel
        {
            DomainFilter = "not-present.invalid"
        };

        viewModel.AddSampleCommand.Execute(null);

        Assert.Empty(viewModel.Sessions);
    }

    [Fact]
    public void AddSample_WithAutoScrollDisabled_DoesNotChangeSelection()
    {
        var viewModel = new MainViewModel { AutoScroll = false };
        var selected = viewModel.SelectedSession;

        viewModel.AddSampleCommand.Execute(null);

        Assert.Same(selected, viewModel.SelectedSession);
    }

    [Fact]
    public void NewestFirst_ReversesVisibleSessionOrderAndPreservesSelection()
    {
        var viewModel = new MainViewModel();
        var selected = viewModel.SelectedSession;

        viewModel.NewestFirst = true;

        Assert.Same(selected, viewModel.SelectedSession);
        Assert.True(viewModel.Sessions[0].StartedAt > viewModel.Sessions[^1].StartedAt);
    }

    [Fact]
    public void ClearDomainFilter_RestoresAllSessions()
    {
        var viewModel = new MainViewModel { DomainFilter = "api.example.com" };

        viewModel.ClearDomainFilterCommand.Execute(null);

        Assert.Equal(3, viewModel.Sessions.Count);
        Assert.Equal(string.Empty, viewModel.DomainFilter);
    }

    [Fact]
    public void AddSample_WithNewestFirst_InsertsAndReportsNewestAtTop()
    {
        var viewModel = new MainViewModel { NewestFirst = true };
        SignInToSniff.Models.CapturedSession? reportedSession = null;
        viewModel.VisibleSessionAdded += (_, session) => reportedSession = session;

        viewModel.AddSampleCommand.Execute(null);

        Assert.NotNull(reportedSession);
        Assert.Same(reportedSession, viewModel.Sessions[0]);
    }

    [Fact]
    public void SortingExistingSessions_DoesNotReportANewLiveSession()
    {
        var viewModel = new MainViewModel();
        var eventCount = 0;
        viewModel.VisibleSessionAdded += (_, _) => eventCount++;

        viewModel.NewestFirst = true;

        Assert.Equal(0, eventCount);
    }
}
