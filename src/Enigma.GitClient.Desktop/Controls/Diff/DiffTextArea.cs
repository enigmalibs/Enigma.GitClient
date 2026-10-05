using System;
using AvaloniaEdit.Editing;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// The text area of a <see cref="DiffTextEditor"/>: AvaloniaEdit's own, built on a
/// <see cref="DiffTextView"/>.
/// </summary>
/// <remarks>
/// The text area takes its view only through a protected constructor, so a different view needs a
/// text area of its own to carry it. Styled as a <see cref="TextArea"/>, so it wears AvaloniaEdit's
/// template.
/// </remarks>
public sealed class DiffTextArea : TextArea
{
    /// <summary>
    /// Initialises a new instance on a new <see cref="DiffTextView"/>.
    /// </summary>
    public DiffTextArea()
        : base(new DiffTextView())
    {
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextArea);
}
