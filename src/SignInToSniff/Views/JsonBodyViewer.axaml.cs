using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using SignInToSniff.ViewModels;

namespace SignInToSniff.Views;

public sealed partial class JsonBodyViewer : UserControl
{
    private const int MaxTreeNodes = 10_000;
    private const int MaxTreeDepth = 64;
    private const int MaxHighlightedCharacters = 256 * 1024;
    private int _renderVersion;

    public static readonly StyledProperty<string> BodyProperty =
        AvaloniaProperty.Register<JsonBodyViewer, string>(nameof(Body), string.Empty);

    public string Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public ObservableCollection<JsonTreeNode> Nodes { get; } = [];

    static JsonBodyViewer()
    {
        BodyProperty.Changed.AddClassHandler<JsonBodyViewer>((viewer, _) => viewer.ScheduleRender());
    }

    public JsonBodyViewer()
    {
        InitializeComponent();
    }

    private void ScheduleRender()
    {
        var version = ++_renderVersion;
        var body = Body;
        _ = RenderAsync(body, version);
    }

    private async Task RenderAsync(string body, int version)
    {
        var result = await Task.Run(() => new JsonRenderResult(
            BuildTree(body),
            body.Length <= MaxHighlightedCharacters
                ? Tokenize(body).ToArray()
                : [(body, "#172033")]));
        if (version != _renderVersion) return;

        Nodes.Clear();
        foreach (var node in result.Nodes) Nodes.Add(node);
        RenderSyntax(result.Tokens);
    }

    private void RenderSyntax(IReadOnlyList<(string Text, string Color)> tokens)
    {
        SyntaxText.Inlines?.Clear();
        if (SyntaxText.Inlines is null) return;

        foreach (var token in tokens)
        {
            SyntaxText.Inlines.Add(new Run(token.Text) { Foreground = Brush.Parse(token.Color) });
        }
    }

    private static IReadOnlyList<JsonTreeNode> BuildTree(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var count = 0;
            return [BuildNode("root", document.RootElement, 0, ref count)];
        }
        catch (JsonException exception)
        {
            return [new JsonTreeNode("Invalid JSON", exception.Message, "#B42318", [])];
        }
    }

    private static JsonTreeNode BuildNode(string name, JsonElement element, int depth, ref int count)
    {
        if (++count > MaxTreeNodes) return new JsonTreeNode(name, "… tree truncated", "#B54708", []);
        if (depth >= MaxTreeDepth) return new JsonTreeNode(name, "… depth limit", "#B54708", []);

        if (element.ValueKind == JsonValueKind.Object)
        {
            var children = new List<JsonTreeNode>();
            foreach (var property in element.EnumerateObject())
            {
                children.Add(BuildNode(property.Name, property.Value, depth + 1, ref count));
                if (count > MaxTreeNodes) break;
            }
            return new JsonTreeNode(name, $"{{{children.Count}}}", "#667085", children);
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var children = new List<JsonTreeNode>();
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                children.Add(BuildNode($"[{index++}]", item, depth + 1, ref count));
                if (count > MaxTreeNodes) break;
            }
            return new JsonTreeNode(name, $"[{children.Count}]", "#667085", children);
        }

        var value = element.ValueKind == JsonValueKind.String ? $"\"{element.GetString()}\"" : element.GetRawText();
        var color = element.ValueKind switch
        {
            JsonValueKind.String => "#067647",
            JsonValueKind.Number => "#175CD3",
            JsonValueKind.True or JsonValueKind.False => "#7A5AF8",
            JsonValueKind.Null => "#667085",
            _ => "#172033"
        };
        return new JsonTreeNode(name, value, color, []);
    }

    private static IEnumerable<(string Text, string Color)> Tokenize(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var start = index;
            var color = "#172033";
            if (text[index] == '"')
            {
                index++;
                while (index < text.Length)
                {
                    if (text[index] == '\\') index += Math.Min(2, text.Length - index);
                    else if (text[index++] == '"') break;
                    else { }
                }
                var lookahead = index;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead])) lookahead++;
                color = lookahead < text.Length && text[lookahead] == ':' ? "#9E3A8A" : "#067647";
            }
            else if (char.IsDigit(text[index]) || text[index] == '-')
            {
                while (index < text.Length && "-+0123456789.eE".Contains(text[index])) index++;
                color = "#175CD3";
            }
            else if (text.AsSpan(index).StartsWith("true") || text.AsSpan(index).StartsWith("false"))
            {
                index += text[index] == 't' ? 4 : 5;
                color = "#7A5AF8";
            }
            else if (text.AsSpan(index).StartsWith("null"))
            {
                index += 4;
                color = "#667085";
            }
            else
            {
                index++;
                while (index < text.Length && text[index] != '"' && !char.IsDigit(text[index]) &&
                       text[index] != '-' && !text.AsSpan(index).StartsWith("true") &&
                       !text.AsSpan(index).StartsWith("false") && !text.AsSpan(index).StartsWith("null")) index++;
            }
            yield return (text[start..index], color);
        }
    }

    private sealed record JsonRenderResult(
        IReadOnlyList<JsonTreeNode> Nodes,
        IReadOnlyList<(string Text, string Color)> Tokens);
}
