using System.Text;
using SignInToSniff.Content;
using Xunit;

namespace SignInToSniff.Tests;

public sealed class FormBodyParserTests
{
    [Fact]
    public void UrlEncoded_DecodesValuesAndPreservesRepeatedKeys()
    {
        var fields = FormBodyParser.Parse(
            "name=SignInToSniff&tag=proxy&tag=C%23+Avalonia&empty=",
            "application/x-www-form-urlencoded; charset=utf-8");

        Assert.Collection(fields,
            field => Assert.Equal(("name", "SignInToSniff"), (field.Name, field.Value)),
            field => Assert.Equal(("tag", "proxy"), (field.Name, field.Value)),
            field => Assert.Equal(("tag", "C# Avalonia"), (field.Name, field.Value)),
            field => Assert.Equal(("empty", string.Empty), (field.Name, field.Value)));
    }

    [Fact]
    public void Multipart_ReportsTextAndFileMetadata()
    {
        const string boundary = "sign-in-to-sniff-boundary";
        var body = $"--{boundary}\r\n" +
                   "Content-Disposition: form-data; name=\"description\"\r\n\r\n" +
                   "hello world\r\n" +
                   $"--{boundary}\r\n" +
                   "Content-Disposition: form-data; name=\"upload\"; filename=\"sample.txt\"\r\n" +
                   "Content-Type: text/plain\r\n\r\n" +
                   "abc\r\n" +
                   $"--{boundary}--\r\n";

        var fields = FormBodyParser.Parse(
            body,
            $"multipart/form-data; boundary=\"{boundary}\"",
            Encoding.UTF8.GetBytes(body));

        Assert.Equal(2, fields.Count);
        Assert.Equal("hello world", fields[0].Value);
        Assert.Equal("upload", fields[1].Name);
        Assert.Equal("sample.txt", fields[1].FileName);
        Assert.Equal("text/plain", fields[1].ContentType);
        Assert.Equal(3, fields[1].SizeBytes);
    }
}
