namespace InputSync.Core.Text;

/// <summary>
/// Minimal edit script turning observed source text into target input:
/// delete <see cref="Backspaces"/> characters, then type <see cref="Inserted"/>.
/// </summary>
public readonly record struct TextEdit(int Backspaces, string Inserted)
{
    public static readonly TextEdit Empty = new(0, string.Empty);

    public bool IsEmpty => Backspaces <= 0 && string.IsNullOrEmpty(Inserted);
}

/// <summary>
/// Pure prefix/suffix diff between two snapshots of source text.
/// Typing appends, Telex-style composition replaces the tail, Backspace
/// deletes. All outputs are applied on targets as WM_CHAR sequences only.
/// </summary>
public static class TextDiffer
{
    public static TextEdit Compute(string? before, string? after, int maxLength = 30000)
    {
        string oldText = before ?? string.Empty;
        string newText = after ?? string.Empty;
        if (oldText.Length > maxLength)
        {
            oldText = oldText[^maxLength..];
        }

        if (newText.Length > maxLength)
        {
            newText = newText[^maxLength..];
        }

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return TextEdit.Empty;
        }

        int prefix = 0;
        int shared = Math.Min(oldText.Length, newText.Length);
        while (prefix < shared && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        string oldTail = oldText[prefix..];
        string newTail = newText[prefix..];
        int suffix = 0;
        while (suffix < oldTail.Length
            && suffix < newTail.Length
            && oldTail[^(suffix + 1)] == newTail[^(suffix + 1)])
        {
            suffix++;
        }

        return new TextEdit(
            oldTail.Length - suffix,
            newTail.Substring(0, newTail.Length - suffix));
    }
}
