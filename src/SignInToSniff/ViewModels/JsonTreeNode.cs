namespace SignInToSniff.ViewModels;

public sealed record JsonTreeNode(string Name, string Value, string ValueColor, IReadOnlyList<JsonTreeNode> Children)
{
    public bool HasValue => !string.IsNullOrEmpty(Value);
}
