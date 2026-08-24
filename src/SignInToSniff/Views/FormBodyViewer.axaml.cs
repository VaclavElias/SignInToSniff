using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using SignInToSniff.Content;

namespace SignInToSniff.Views;

public sealed partial class FormBodyViewer : UserControl
{
    private int _parseVersion;

    public static readonly StyledProperty<string> BodyProperty =
        AvaloniaProperty.Register<FormBodyViewer, string>(nameof(Body), string.Empty);
    public static readonly StyledProperty<string?> ContentTypeProperty =
        AvaloniaProperty.Register<FormBodyViewer, string?>(nameof(ContentType));
    public static readonly StyledProperty<byte[]?> MultipartBytesProperty =
        AvaloniaProperty.Register<FormBodyViewer, byte[]?>(nameof(MultipartBytes));

    public string Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public string? ContentType
    {
        get => GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    public byte[]? MultipartBytes
    {
        get => GetValue(MultipartBytesProperty);
        set => SetValue(MultipartBytesProperty, value);
    }

    public ObservableCollection<FormField> Fields { get; } = [];

    static FormBodyViewer()
    {
        BodyProperty.Changed.AddClassHandler<FormBodyViewer>((viewer, _) => viewer.ScheduleParse());
        ContentTypeProperty.Changed.AddClassHandler<FormBodyViewer>((viewer, _) => viewer.ScheduleParse());
        MultipartBytesProperty.Changed.AddClassHandler<FormBodyViewer>((viewer, _) => viewer.ScheduleParse());
    }

    public FormBodyViewer() => InitializeComponent();

    private void ScheduleParse()
    {
        var version = ++_parseVersion;
        var body = Body;
        var contentType = ContentType;
        var bytes = MultipartBytes;
        _ = ParseAsync(body, contentType, bytes, version);
    }

    private async Task ParseAsync(string body, string? contentType, byte[]? bytes, int version)
    {
        var fields = await Task.Run(() => FormBodyParser.Parse(body, contentType, bytes));
        if (version != _parseVersion) return;
        Fields.Clear();
        foreach (var field in fields) Fields.Add(field);
    }
}
