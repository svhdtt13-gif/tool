using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>PR#7 regression: observed-text diff must be exact — the single
/// text channel that prevents double emission.</summary>
public sealed class TextDifferTests
{
    [Fact]
    public void Append_SingleChar()
    {
        TextEdit edit = TextDiffer.Compute("ab", "abc");

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("c", edit.Inserted);
        Assert.False(edit.IsEmpty);
    }

    [Fact]
    public void TelexComposition_ReplacesTail()
    {
        TextEdit edit = TextDiffer.Compute("a", "â");

        Assert.Equal(1, edit.Backspaces);
        Assert.Equal("â", edit.Inserted);
    }

    [Fact]
    public void VniComposition_ReplacesTail()
    {
        TextEdit edit = TextDiffer.Compute("a6", "â");

        Assert.Equal(2, edit.Backspaces);
        Assert.Equal("â", edit.Inserted);
    }

    [Fact]
    public void TelexDoubleD_ReplacesTail()
    {
        TextEdit edit = TextDiffer.Compute("dd", "đ");

        Assert.Equal(2, edit.Backspaces);
        Assert.Equal("đ", edit.Inserted);
    }

    [Fact]
    public void DeleteMiddle_RemovesMiddle()
    {
        TextEdit edit = TextDiffer.Compute("abcd", "acd");

        Assert.Equal(1, edit.Backspaces);
        Assert.Equal(string.Empty, edit.Inserted);
    }

    [Fact]
    public void ReplaceMiddle_SwapsMiddle()
    {
        TextEdit edit = TextDiffer.Compute("abc", "aXc");

        Assert.Equal(1, edit.Backspaces);
        Assert.Equal("X", edit.Inserted);
    }

    [Fact]
    public void Backspace_DeletesOne()
    {
        TextEdit edit = TextDiffer.Compute("abc", "ab");

        Assert.Equal(1, edit.Backspaces);
        Assert.Equal(string.Empty, edit.Inserted);
    }

    [Fact]
    public void Enter_AppendsNewline()
    {
        TextEdit edit = TextDiffer.Compute("ab", "ab\r\n");

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("\r\n", edit.Inserted);
    }

    [Fact]
    public void Identical_IsEmpty()
    {
        Assert.True(TextDiffer.Compute("abc", "abc").IsEmpty);
        Assert.True(TextDiffer.Compute(null, "").IsEmpty);
        Assert.True(TextDiffer.Compute("", null).IsEmpty);
    }

    [Fact]
    public void Paste_AppendsBlock()
    {
        TextEdit edit = TextDiffer.Compute("", "Tiếng Việt");

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("Tiếng Việt", edit.Inserted);
    }

    [Fact]
    public void VietnamesePrecomposed_Append()
    {
        TextEdit edit = TextDiffer.Compute("Tiếng Việt có dấu: ", "Tiếng Việt có dấu: ă");

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("ă", edit.Inserted);
    }

    [Fact]
    public void MiddleEdit_ReplacesMiddle()
    {
        TextEdit edit = TextDiffer.Compute("ac", "abc");

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("b", edit.Inserted);
    }
}
