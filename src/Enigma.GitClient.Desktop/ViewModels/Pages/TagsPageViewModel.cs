using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.Formatting;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// One tag, as a list of tags shows it.
/// </summary>
/// <remarks>
/// The row carries the commands it offers rather than a reference to the page that built it, so a
/// context menu opening in its own popup tree can still reach them with a plain binding against the
/// row — and so the row does not have to know which page it belongs to.
/// </remarks>
public sealed class TagRowViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="tag">The tag the row stands for.</param>
    /// <param name="checkout">The command that checks the tag out, detaching HEAD.</param>
    /// <param name="delete">The command that deletes the tag.</param>
    /// <param name="selectInHistory">The command that selects the tagged commit in the history.</param>
    /// <param name="push">The command that pushes the tag to the remote.</param>
    /// <param name="deleteRemote">The command that deletes the tag from the remote.</param>
    /// <param name="selection">How many lines of the page are selected, which the line's menu reads.</param>
    public TagRowViewModel(
        GitTag tag,
        AsyncRelayCommand<TagRowViewModel> checkout,
        AsyncRelayCommand<TagRowViewModel> delete,
        RelayCommand<TagRowViewModel>? selectInHistory = null,
        AsyncRelayCommand<TagRowViewModel>? push = null,
        AsyncRelayCommand<TagRowViewModel>? deleteRemote = null,
        LineSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(delete);

        Tag = tag;
        CheckoutCommand = checkout;
        DeleteCommand = delete;
        SelectInHistoryCommand = selectInHistory;
        PushCommand = push;
        DeleteRemoteCommand = deleteRemote;
        Selection = selection ?? new LineSelection();
    }

    /// <summary>Gets the tag this row stands for.</summary>
    public GitTag Tag { get; }

    /// <summary>
    /// Gets how many lines of the page are selected: with several, the line's menu offers only what
    /// works on all of them — the deletes.
    /// </summary>
    public LineSelection Selection { get; }

    /// <summary>Gets the tag's name.</summary>
    public string Name => Tag.ShortName;

    /// <summary>Gets whether the tag is annotated or a plain pointer.</summary>
    public string Kind => Tag.IsAnnotated ? "annotated" : "lightweight";

    /// <summary>Gets the tag's message, empty for a lightweight tag.</summary>
    public string Message => Tag.Message;

    /// <summary>Gets a value indicating whether there is a message to show.</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>Gets who created the tag, empty for a lightweight tag.</summary>
    public string Tagger => Tag.Tagger?.Name ?? string.Empty;

    /// <summary>Gets a value indicating whether there is a tagger to show.</summary>
    public bool HasTagger => Tagger.Length > 0;

    /// <summary>Gets how long ago the tag's commit was written.</summary>
    public string Date => RelativeTime.Format(Tag.TargetDate);

    /// <summary>Gets the tagged commit's short hash.</summary>
    public string ShortSha => Tag.TargetSha.Length >= 7 ? Tag.TargetSha[..7] : Tag.TargetSha;

    /// <summary>
    /// Gets what the line's tooltip says: everything about the tag the line itself leaves out — its
    /// kind, its message, who made it, and the commit it points at with that commit's date.
    /// </summary>
    public string ToolTip
    {
        get
        {
            List<string> lines = [Tag.IsAnnotated ? "Annotated tag" : "Lightweight tag"];

            if (HasMessage)
            {
                lines.Add(Message.Trim());
            }

            if (HasTagger)
            {
                lines.Add($"Tagged by {Tagger}");
            }

            lines.Add($"{ShortSha} · {Date}");

            return string.Join('\n', lines);
        }
    }

    /// <summary>Gets the command that checks the tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutCommand { get; }

    /// <summary>
    /// Gets the command that deletes the tag here — and with it every other selected tag, when the
    /// line is one of several selected.
    /// </summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteCommand { get; }

    /// <summary>Gets the command that closes the dialog and selects the tagged commit in the history.</summary>
    public RelayCommand<TagRowViewModel>? SelectInHistoryCommand { get; }

    /// <summary>Gets the command that pushes the tag, on its own, to the remote.</summary>
    public AsyncRelayCommand<TagRowViewModel>? PushCommand { get; }

    /// <summary>Gets a value indicating whether the row's menu offers the push.</summary>
    public bool CanPush => PushCommand is not null;

    /// <summary>Gets the command that deletes the tag from the remote, keeping it here.</summary>
    public AsyncRelayCommand<TagRowViewModel>? DeleteRemoteCommand { get; }

    /// <summary>Gets a value indicating whether the row's menu offers the remote delete.</summary>
    public bool CanDeleteRemote => DeleteRemoteCommand is not null;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// ViewModel behind the tags page.
/// </summary>
/// <remarks>
/// A page of its own rather than half of the branches page: the two lists were alternatives behind a
/// switch, which meant one selection, one filter box and one header doing two jobs. The page shows
/// and filters; every write, with its dialog and its confirmation, belongs to
/// <see cref="ITagOperations"/>, <see cref="ICheckoutOperations"/> and <see cref="ISyncOperations"/>,
/// which the graph's own context menus call too — two places offering the same operation have to ask
/// the same questions.
/// </remarks>
public sealed class TagsPageViewModel : PageViewModelBase
{
    private readonly ITagOperations _tagOperations;
    private readonly ICheckoutOperations _checkoutOperations;
    private readonly ISyncOperations _syncOperations;
    private readonly ISettingsService _settings;
    private readonly IToolDialogService _tools;

    // Set while the order is being read from the settings, which must not be written back.
    private bool _applyingSettings;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="tagOperations">Performs the tag operations, dialogs and all.</param>
    /// <param name="checkoutOperations">Performs a checkout, including the questions it has to ask.</param>
    /// <param name="syncOperations">Pushes a tag to the remote, as the history's tag badges do.</param>
    /// <param name="settings">Remembers the order of the lines.</param>
    /// <param name="tools">Closes the dialog to select a tagged commit in the history.</param>
    public TagsPageViewModel(
        IRepositoryContext repositoryContext,
        ITagOperations tagOperations,
        ICheckoutOperations checkoutOperations,
        ISyncOperations syncOperations,
        ISettingsService settings,
        IToolDialogService tools)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(tagOperations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);
        ArgumentNullException.ThrowIfNull(syncOperations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);

        _tagOperations = tagOperations;
        _checkoutOperations = checkoutOperations;
        _syncOperations = syncOperations;
        _settings = settings;
        _tools = tools;

        // The history is under the dialog: selecting a line there closes it.
        SelectInHistoryCommand = new RelayCommand<TagRowViewModel>(
            row => _tools.RevealInHistory(row!.Tag.TargetSha),
            row => row is { Tag.TargetSha.Length: > 0 });

        // The order the reader chose last time — the tags' own, apart from the branches'.
        ApplySort(settings.Current, rebuild: false);
        settings.Changed += (_, e) => ApplySort(e.Settings, rebuild: true);
        ToggleSortDirectionCommand = new RelayCommand(() => SortDirection = SortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending);

        CreateCommand = new AsyncRelayCommand(OnCreateAsync, () => IsRepositoryOpen);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        CheckoutCommand = new AsyncRelayCommand<TagRowViewModel>(OnCheckoutAsync, row => row is not null);
        DeleteCommand = new AsyncRelayCommand<TagRowViewModel>(OnDeleteAsync, row => row is not null);
        DeleteRemoteCommand = new AsyncRelayCommand<TagRowViewModel>(OnDeleteRemoteAsync, row => row is not null);
        PushCommand = new AsyncRelayCommand<TagRowViewModel>(OnPushAsync, row => row is not null);
        DeleteSelectionCommand = new AsyncRelayCommand(OnDeleteSelectionAsync, () => SelectedTags.Count > 0);

        SelectedTags.CollectionChanged += (_, _) => OnSelectionChanged();
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Tags";

    /// <summary>Gets the tags the repository holds, filtered by the search box.</summary>
    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    /// <summary>Gets what the filter box says it filters.</summary>
    public string SearchPlaceholder => "Filter tags";

    // ---------------------------------------------------------------- the order

    /// <summary>Gets what the lines can be ordered by, for the "Sort by" box.</summary>
    public IReadOnlyList<RefSortKey> SortKeys { get; } = [RefSortKey.Date, RefSortKey.Name];

    /// <summary>
    /// Gets or sets what the lines are ordered by: the name, or the date of the tagged commit — the
    /// date every line shows. Remembered in the settings.
    /// </summary>
    public RefSortKey SortKey
    {
        get;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref field, value))
            {
                return;
            }

            OnSortChanged();

            if (!_applyingSettings)
            {
                _settings.Update(current => current with { TagSortKey = value });
                Rebuild();
            }
        }
    } = RefSortKey.Date;

    /// <summary>Gets or sets which way the lines are ordered; remembered in the settings.</summary>
    public SortDirection SortDirection
    {
        get;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref field, value))
            {
                return;
            }

            OnSortChanged();

            if (!_applyingSettings)
            {
                _settings.Update(current => current with { TagSortDirection = value });
                Rebuild();
            }
        }
    } = SortDirection.Descending;

    /// <summary>Gets a value indicating whether the lines run Z to A, newest first.</summary>
    public bool IsSortDescending => SortDirection == SortDirection.Descending;

    /// <summary>Gets what the direction button says: the order now, and that a click reverses it.</summary>
    public string SortDirectionTip => $"{RefSort.Describe(SortKey, SortDirection)} — click to reverse";

    /// <summary>Gets the command that reverses the order.</summary>
    public RelayCommand ToggleSortDirectionCommand { get; }

    /// <summary>
    /// Gets the command that closes the dialog and selects a tagged commit in the history under it.
    /// </summary>
    public RelayCommand<TagRowViewModel> SelectInHistoryCommand { get; }

    /// <summary>
    /// Gets or sets the tag the reader has selected — the first of them, when several are.
    /// </summary>
    public TagRowViewModel? SelectedTag
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Gets every tag the reader has selected: the list's own selection, which Ctrl+click, Shift+click
    /// and Ctrl+A make, and what the Delete key and the header's button act on.
    /// </summary>
    public ObservableCollection<TagRowViewModel> SelectedTags { get; } = [];

    /// <summary>Gets how many tags are selected, as every line's menu reads it.</summary>
    public LineSelection Selection { get; } = new();

    /// <summary>Gets what the header's delete button says: how many it would delete.</summary>
    public string DeleteSelectionLabel
        => SelectedTags.Count == 0 ? "Delete" : $"Delete ({SelectedTags.Count.ToString(CultureInfo.CurrentCulture)})";

    /// <summary>
    /// Gets the command that deletes every selected tag here, after one question naming them all — the
    /// header's button and the Delete key.
    /// </summary>
    public AsyncRelayCommand DeleteSelectionCommand { get; }

    /// <summary>
    /// Gets or sets a substring the shown tag names must contain.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                Rebuild();
            }
        }
    } = string.Empty;

    /// <summary>Gets a value indicating whether the page has nothing to show.</summary>
    public bool IsEmpty => Tags.Count == 0;

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to manage its tags."
            : SearchText.Trim().Length > 0
                ? "No tag matches this search."
                : "This repository has no tags yet.";

    /// <summary>Gets the command that opens the create-tag dialog.</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that checks a tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutCommand { get; }

    /// <summary>Gets the command that deletes a tag.</summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteCommand { get; }

    /// <summary>
    /// Gets the command that deletes a tag from the remote, keeping it here — every selected tag, when
    /// the line is one of several selected.
    /// </summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteRemoteCommand { get; }

    /// <summary>Gets the command that pushes a tag, on its own, to the remote.</summary>
    public AsyncRelayCommand<TagRowViewModel> PushCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        Rebuild();
    }

    /// <summary>
    /// Re-reads the repository's references and rebuilds the page.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
    {
        if (IsRepositoryOpen)
        {
            await RepositoryContext.RefreshAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }

        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        CreateCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => Rebuild();

    /// <summary>
    /// Rebuilds the list from whatever the repository context last read.
    /// </summary>
    /// <remarks>
    /// The selection is captured before the list is emptied and put back by name afterwards: every
    /// row is a new object, and the page rebuilds on a refresh, on an operation and on every
    /// keystroke in the filter box. A tag that is gone — deleted, filtered out — takes the selection
    /// with it.
    /// </remarks>
    /// <summary>
    /// Takes the order from the settings: the one remembered, or one changed elsewhere — a reset of
    /// every preference, say.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="rebuild">Whether to redraw the lines; not while the page is still being built.</param>
    private void ApplySort(AppSettings settings, bool rebuild)
    {
        if (settings.TagSortKey == SortKey && settings.TagSortDirection == SortDirection)
        {
            return;
        }

        _applyingSettings = true;

        try
        {
            SortKey = settings.TagSortKey;
            SortDirection = settings.TagSortDirection;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (rebuild)
        {
            Rebuild();
        }
    }

    private void OnSortChanged()
    {
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortDirectionTip));
    }

    private void Rebuild()
    {
        // Captured before the list is emptied: emptying it tells the list the selection is gone, and the
        // list says so back. What survives a rebuild is the names.
        string? selected = SelectedTag?.Name;
        HashSet<string> selectedNames = [.. SelectedTags.Select(row => row.Name)];

        SelectedTags.Clear();
        Tags.Clear();

        IEnumerable<GitTag> matching = RepositoryContext.Refs.Tags.Where(tag => Matches(tag.ShortName));

        foreach (GitTag tag in RefSort.Order(matching, tag => tag.ShortName, tag => tag.TargetDate, SortKey, SortDirection))
        {
            Tags.Add(new TagRowViewModel(tag, CheckoutCommand, DeleteCommand, SelectInHistoryCommand, PushCommand, DeleteRemoteCommand, Selection));
        }

        SelectedTag = selected is null
            ? null
            : Tags.FirstOrDefault(row => row.Name == selected);

        foreach (TagRowViewModel row in Tags)
        {
            if (selectedNames.Contains(row.Name) && !SelectedTags.Contains(row))
            {
                SelectedTags.Add(row);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private bool Matches(string name)
    {
        string search = SearchText.Trim();

        return search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- commands

    private Task OnCreateAsync() => Run(() => _tagOperations.CreateAsync());

    private async Task OnCheckoutAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _checkoutOperations.CheckoutAsync(row.Name)).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _tagOperations.DeleteAsync(NamesFor(row))).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteRemoteAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _tagOperations.DeleteRemoteAsync(NamesFor(row))).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteSelectionAsync()
    {
        if (SelectedTags.Count > 0)
        {
            await Run(() => _tagOperations.DeleteAsync(SelectedNames())).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The tags a line's menu acts on: the whole selection when the line is one of several selected,
    /// and the line alone otherwise — a right-click outside the selection selects that line first.
    /// </summary>
    private IReadOnlyList<string> NamesFor(TagRowViewModel row)
        => SelectedTags.Count > 1 && SelectedTags.Contains(row) ? SelectedNames() : [row.Name];

    /// <summary>The selected tags' names, in the order the list shows them.</summary>
    private IReadOnlyList<string> SelectedNames()
        => [.. Tags.Where(SelectedTags.Contains).Select(row => row.Name)];

    private void OnSelectionChanged()
    {
        Selection.Update(SelectedTags.Count);
        OnPropertyChanged(nameof(DeleteSelectionLabel));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
    }

    private async Task OnPushAsync(TagRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _syncOperations.PushTagAsync(row.Name)).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs one operation with the page marked busy, and rebuilds when it changed something.
    /// </summary>
    private async Task Run(Func<Task<bool>> operation)
    {
        IsBusy = true;

        try
        {
            if (await operation().ConfigureAwait(true))
            {
                Rebuild();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
