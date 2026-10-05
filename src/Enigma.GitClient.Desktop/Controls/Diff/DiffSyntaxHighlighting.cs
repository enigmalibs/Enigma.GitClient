using System;
using System.IO;
using Avalonia.Logging;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using TextMateInstallation = AvaloniaEdit.TextMate.TextMate.Installation;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// The syntax colours of one diff editor: a TextMate grammar chosen by the file's extension, in the
/// TextMate theme that matches the window's theme variant.
/// </summary>
/// <remarks>
/// <para>
/// Only the tokens are coloured here. The tints, the bands and the word highlight are the diff's own,
/// drawn behind the text by <see cref="DiffBackgroundRenderer"/>, so a keyword on an added line is
/// both: green behind, the keyword's colour in front. Text no token claims keeps the editor's own
/// foreground.
/// </para>
/// <para>
/// Dark+ and Light+ are Visual Studio Code's defaults, the pair Enigma.MarkdownEditor uses too: they
/// have the widest token coverage, and they are the colours most readers already know code in.
/// </para>
/// <para>
/// A file with an extension no bundled grammar knows stays plain. A grammar that fails while it
/// tokenizes — on a background thread — costs this editor its colours and nothing else: the failure
/// is logged and the highlighting taken off on the UI thread, rather than taking the process down.
/// </para>
/// </remarks>
public sealed class DiffSyntaxHighlighting : IDisposable
{
    /// <summary>The area the failures are logged under.</summary>
    public const string LogArea = "DiffSyntax";

    private readonly RegistryOptions _registry;
    private readonly TextMateInstallation _installation;

    private bool _disposed;

    /// <summary>
    /// Initialises a new instance: installs TextMate on an editor, in the theme for a variant, with no
    /// grammar until <see cref="SetFile"/> names one.
    /// </summary>
    /// <param name="editor">The editor to colour. Its document may be replaced afterwards.</param>
    /// <param name="variant">The theme variant the editor is showing.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DiffSyntaxHighlighting(TextEditor editor, ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(variant);

        // The variant is handed to the registry rather than corrected afterwards: the installation
        // applies the registry's default theme as it is built.
        Theme = ThemeNameFor(variant);

        _registry = new RegistryOptions(Theme);
        _installation = editor.InstallTextMate(_registry, initCurrentDocument: true, OnFailure);
    }

    /// <summary>Gets the TextMate theme the tokens are coloured in.</summary>
    public ThemeName Theme { get; private set; }

    /// <summary>Gets the grammar's scope, or <see langword="null"/> while the text is plain.</summary>
    public string? Scope { get; private set; }

    /// <summary>Gets a value indicating whether the highlighting has been taken off.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Maps a theme variant onto the TextMate theme that expresses it.
    /// </summary>
    /// <param name="variant">The variant.</param>
    /// <returns>Dark+ for the dark variant, Light+ for anything else.</returns>
    /// <remarks>
    /// Anything not dark is light: light themes' foregrounds are dark, and stay legible on either
    /// background should a variant this application does not define ever reach here.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="variant"/> is <see langword="null"/>.</exception>
    public static ThemeName ThemeNameFor(ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        return ThemeVariant.Dark.Equals(variant) ? ThemeName.DarkPlus : ThemeName.LightPlus;
    }

    /// <summary>
    /// Chooses the grammar for a file.
    /// </summary>
    /// <param name="path">The file's path, or <see langword="null"/> for none.</param>
    public void SetFile(string? path)
    {
        if (_disposed)
        {
            return;
        }

        string extension = string.IsNullOrEmpty(path) ? string.Empty : Path.GetExtension(path);
        string? scope = extension.Length > 0 ? _registry.GetScopeByExtension(extension) : null;

        if (string.Equals(scope, Scope, StringComparison.Ordinal))
        {
            return;
        }

        Scope = scope;
        _installation.SetGrammar(scope);
    }

    /// <summary>
    /// Recolours the tokens for a theme variant, live.
    /// </summary>
    /// <param name="variant">The variant the window has switched to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="variant"/> is <see langword="null"/>.</exception>
    public void Apply(ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        ThemeName name = ThemeNameFor(variant);

        // Loading a theme parses it, and a variant can be announced again without having changed.
        if (_disposed || name == Theme)
        {
            return;
        }

        _installation.SetTheme(_registry.LoadTheme(name));
        Theme = name;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Takes the colouring transformer back off the text view and stops the background
        // tokenizer; the editor goes back to its own foreground.
        _installation.Dispose();
    }

    /// <summary>
    /// What a tokenizing failure does: it is logged, and the highlighting comes off on the UI thread.
    /// </summary>
    /// <param name="exception">What the grammar threw.</param>
    internal void OnFailure(Exception exception)
    {
        Logger.TryGet(LogEventLevel.Warning, LogArea)?.Log(
            this,
            "Highlighting the diff failed and was turned off for this pane: {Exception}",
            exception);

        Dispatcher.UIThread.Post(Dispose);
    }
}
