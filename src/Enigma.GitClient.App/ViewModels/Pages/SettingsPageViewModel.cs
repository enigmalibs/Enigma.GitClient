using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diagnostics;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Sync;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the settings page: every preference the application remembers, and what the
/// build itself is.
/// </summary>
/// <remarks>
/// Every property here reads from and writes to <see cref="ISettingsService"/> rather than holding
/// its own copy, so a preference changed anywhere is the same preference, and nothing has to be
/// pushed back into the store when the page closes.
/// </remarks>
public sealed class SettingsPageViewModel : PageViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IGitEnvironment _git;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;

    private bool _applying;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="settings">The preferences this page edits.</param>
    /// <param name="git">Reports which git the client found, for the About card.</param>
    /// <param name="dialogs">Raises the confirmation before a reset.</param>
    /// <param name="infoBar">Reports that the reset happened.</param>
    public SettingsPageViewModel(
        IRepositoryContext repositoryContext,
        ISettingsService settings,
        IGitEnvironment git,
        IContentDialogService dialogs,
        IInfoBarService infoBar)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);

        _settings = settings;
        _git = git;
        _dialogs = dialogs;
        _infoBar = infoBar;

        ResetCommand = new AsyncRelayCommand(OnResetAsync);

        _settings.Changed += (_, _) => NotifyAll();
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Settings";

    /// <summary>Gets the themes to choose between.</summary>
    public IReadOnlyList<ThemePreference> Themes { get; } =
        [ThemePreference.System, ThemePreference.Dark, ThemePreference.Light];

    /// <summary>Gets the date styles to choose between.</summary>
    public IReadOnlyList<DateDisplay> DateDisplays { get; } = [DateDisplay.Relative, DateDisplay.Absolute];

    /// <summary>Gets the file-list shapes to choose between.</summary>
    public IReadOnlyList<FilesView> FilesViews { get; } = [FilesView.List, FilesView.Tree];

    /// <summary>Gets the diff shapes to choose between.</summary>
    public IReadOnlyList<DiffView> DiffViews { get; } = [DiffView.Unified, DiffView.SideBySide];

    /// <summary>
    /// Gets the pull strategies to choose between — and there are two, because this client never
    /// rebases.
    /// </summary>
    public IReadOnlyList<PullStrategy> PullStrategies { get; } =
        [PullStrategy.Merge, PullStrategy.FastForwardOnly];

    // ---------------------------------------------------------------- appearance

    /// <summary>Gets or sets which theme the application uses.</summary>
    public ThemePreference Theme
    {
        get => _settings.Current.Theme;
        set => Change(current => current with { Theme = value });
    }

    // ---------------------------------------------------------------- history and graph

    /// <summary>Gets or sets how many commits the history reads at a time.</summary>
    public int HistoryPageSize
    {
        get => _settings.Current.HistoryPageSize;
        set => Change(current => current with { HistoryPageSize = value });
    }

    /// <summary>Gets or sets a value indicating whether the history follows only first parents.</summary>
    public bool FirstParentOnly
    {
        get => _settings.Current.FirstParentOnly;
        set => Change(current => current with { FirstParentOnly = value });
    }

    /// <summary>Gets or sets how a commit's date is written.</summary>
    public DateDisplay DateDisplay
    {
        get => _settings.Current.DateDisplay;
        set => Change(current => current with { DateDisplay = value });
    }

    /// <summary>Gets or sets how tall a history row is.</summary>
    public double GraphRowHeight
    {
        get => _settings.Current.GraphRowHeight;
        set => Change(current => current with { GraphRowHeight = value });
    }

    /// <summary>Gets or sets the distance between two graph lanes.</summary>
    public double GraphLaneWidth
    {
        get => _settings.Current.GraphLaneWidth;
        set => Change(current => current with { GraphLaneWidth = value });
    }

    // ---------------------------------------------------------------- changed files

    /// <summary>Gets or sets how the changed files are arranged.</summary>
    public FilesView FilesView
    {
        get => _settings.Current.FilesView;
        set => Change(current => current with { FilesView = value });
    }

    /// <summary>Gets or sets how many files a tree will expand on its own.</summary>
    public int FilesAutoExpandLimit
    {
        get => _settings.Current.FilesAutoExpandLimit;
        set => Change(current => current with { FilesAutoExpandLimit = value });
    }

    // ---------------------------------------------------------------- diff

    /// <summary>Gets or sets how a diff is drawn.</summary>
    public DiffView DiffView
    {
        get => _settings.Current.DiffView;
        set => Change(current => current with { DiffView = value });
    }

    /// <summary>Gets or sets how many unchanged lines are shown around a change.</summary>
    public int DiffContextLines
    {
        get => _settings.Current.DiffContextLines;
        set => Change(current => current with { DiffContextLines = value });
    }

    /// <summary>Gets or sets a value indicating whether spaces and tabs are drawn.</summary>
    public bool ShowWhitespace
    {
        get => _settings.Current.ShowWhitespace;
        set => Change(current => current with { ShowWhitespace = value });
    }

    /// <summary>Gets or sets a value indicating whether whitespace-only changes are ignored.</summary>
    public bool IgnoreWhitespace
    {
        get => _settings.Current.IgnoreWhitespace;
        set => Change(current => current with { IgnoreWhitespace = value });
    }

    /// <summary>Gets or sets how many columns a tab occupies.</summary>
    public int TabWidth
    {
        get => _settings.Current.TabWidth;
        set => Change(current => current with { TabWidth = value });
    }

    /// <summary>Gets or sets a value indicating whether long lines wrap.</summary>
    public bool WrapLines
    {
        get => _settings.Current.WrapLines;
        set => Change(current => current with { WrapLines = value });
    }

    // ---------------------------------------------------------------- git

    /// <summary>Gets or sets an explicit path to the git executable.</summary>
    public string GitExecutablePath
    {
        get => _settings.Current.GitExecutablePath;
        set => Change(current => current with { GitExecutablePath = value });
    }

    /// <summary>Gets or sets what a pull is allowed to do.</summary>
    public PullStrategy Pull
    {
        get => _settings.Current.Pull;
        set => Change(current => current with { Pull = value });
    }

    /// <summary>
    /// Gets the sentence under the git path box, which says the obvious thing about changing it.
    /// </summary>
    public string GitExecutableHint =>
        "Leave this empty to let the client find git on the PATH. A path typed here is used as-is, "
        + "and takes effect the next time the application starts.";

    // ---------------------------------------------------------------- about

    /// <summary>Gets what this application is called.</summary>
    public string ProductName => ProductInformation.Name;

    /// <summary>Gets which build this is.</summary>
    public string ProductVersion => ProductInformation.GetVersion();

    /// <summary>Gets the product's scope statement, which is a promise rather than a limitation.</summary>
    public string ScopeStatement => ProductInformation.ScopeStatement;

    /// <summary>Gets which git the client found, once it has looked.</summary>
    public string GitDescription { get; private set => SetProperty(ref field, value); } = "Looking for git…";

    /// <summary>Gets which Avalonia this build draws with.</summary>
    public string AvaloniaVersion
        => typeof(global::Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Gets the licence this product is under.</summary>
    public string Licence => "MIT";

    /// <summary>Gets where the preferences are kept, so a user can find or delete the file.</summary>
    public string SettingsFileHint => SettingsService.FileName + " in your user configuration directory";

    /// <summary>Gets the command that puts every preference back to its default.</summary>
    public AsyncRelayCommand ResetCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);

        GitAvailability availability = await _git.GetAvailabilityAsync().ConfigureAwait(true);

        GitDescription = availability.IsUsable
            ? $"git {availability.Version} at {availability.ExecutablePath}"
            : availability.Message;
    }

    /// <summary>
    /// Applies a change to the store, unless this page is the one being updated.
    /// </summary>
    private void Change(Func<AppSettings, AppSettings> change)
    {
        if (_applying)
        {
            return;
        }

        _settings.Update(change);
    }

    private async Task OnResetAsync()
    {
        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Reset every preference";
            dialog.Content =
                "Put the theme, the history, the diff and the git preferences back to their defaults?\n\n"
                + "Connected accounts, their tokens and your recent repositories are not touched.";
            dialog.PrimaryButtonText = "Reset";
            dialog.CloseButtonText = "Keep them";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        await _settings.ResetAsync().ConfigureAwait(true);

        await _infoBar.ShowAsync(bar =>
        {
            bar.Title = "Preferences reset";
            bar.Message = "Everything is back to its default.";
            bar.Severity = Enigma.Avalonia.Desktop.Controls.InfoBar.InfoBarSeverity.Info;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Raises every property, because a change in the store can come from anywhere — another page,
    /// a reset, or this one.
    /// </summary>
    private void NotifyAll()
    {
        _applying = true;

        try
        {
            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(HistoryPageSize));
            OnPropertyChanged(nameof(FirstParentOnly));
            OnPropertyChanged(nameof(DateDisplay));
            OnPropertyChanged(nameof(GraphRowHeight));
            OnPropertyChanged(nameof(GraphLaneWidth));
            OnPropertyChanged(nameof(FilesView));
            OnPropertyChanged(nameof(FilesAutoExpandLimit));
            OnPropertyChanged(nameof(DiffView));
            OnPropertyChanged(nameof(DiffContextLines));
            OnPropertyChanged(nameof(ShowWhitespace));
            OnPropertyChanged(nameof(IgnoreWhitespace));
            OnPropertyChanged(nameof(TabWidth));
            OnPropertyChanged(nameof(WrapLines));
            OnPropertyChanged(nameof(GitExecutablePath));
            OnPropertyChanged(nameof(Pull));
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// Describes a theme choice for the dropdown.
    /// </summary>
    /// <param name="theme">The choice.</param>
    /// <returns>What to call it.</returns>
    public static string Describe(ThemePreference theme)
        => theme switch
        {
            ThemePreference.Dark => "Dark",
            ThemePreference.Light => "Light",
            _ => "Follow the system",
        };

    /// <summary>
    /// Describes a pull strategy for the dropdown.
    /// </summary>
    /// <param name="strategy">The choice.</param>
    /// <returns>What to call it.</returns>
    public static string Describe(PullStrategy strategy)
        => strategy == PullStrategy.FastForwardOnly
            ? "Fast-forward only — refuse when a merge would be needed"
            : "Merge — make a merge commit when one is needed";

    /// <summary>
    /// Describes a number for a label, in the current culture.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Describe(double value)
        => value.ToString("0", CultureInfo.CurrentCulture);
}
