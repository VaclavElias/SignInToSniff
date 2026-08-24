using SignInToSniff.Models;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class CapturedSessionTests
{
    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    [InlineData("APPLICATION/JSON")]
    public void JsonViewer_IsSelectedForJsonMediaTypes(string contentType)
    {
        var session = CreateSession() with
        {
            RequestContentType = contentType,
            ResponseContentType = contentType
        };

        Assert.True(session.HasRequestJson);
        Assert.True(session.HasResponseJson);
    }

    [Fact]
    public void ImagePreview_TakesPriorityOverJsonViewer()
    {
        var session = CreateSession() with
        {
            ResponseContentType = "application/json",
            ResponseImageBytes = [1]
        };

        Assert.False(session.HasResponseJson);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=test")]
    public void FormViewer_IsSelectedForSupportedFormMediaTypes(string contentType)
    {
        var session = CreateSession() with
        {
            RequestContentType = contentType,
            ResponseContentType = contentType
        };

        Assert.True(session.HasRequestForm);
        Assert.True(session.HasResponseForm);
        Assert.False(session.HasRequestPlainBody);
        Assert.False(session.HasResponsePlainBody);
    }

    private static CapturedSession CreateSession() => new(
        Guid.NewGuid(), DateTimeOffset.Now, "GET", 200, "example.com", "https://example.com/data",
        string.Empty, "{}", string.Empty, "{}", 1);
}
