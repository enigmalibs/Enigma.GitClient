using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Pages;

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
    public TagRowViewModel(
        GitTag tag,
        AsyncRelayCommand<TagRowViewModel> checkout,
        AsyncRelayCommand<TagRowViewModel> delete)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(delete);

        Tag = tag;
        CheckoutCommand = checkout;
        DeleteCommand = delete;
    }

    /// <summary>Gets the tag this row stands for.</summary>
    public GitTag Tag { get; }

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

    /// <summary>Gets the command that checks the tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutCommand { get; }

    /// <summary>Gets the command that deletes the tag.</summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteCommand { get; }

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
/// <see cref="ITagOperations"/> and <see cref="ICheckoutOperations"/>, which the graph's own context
/// menu calls too — two places offering the same operation have to ask the same questions.
/// </remarks>
public sealed class TagsPageViewModel : PageViewModelBase
{
    private readonly ITagOperations _tagOperations;
    private readonly ICheckoutOperations _checkoutOperations;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="tagOperations">Performs the tag operations, dialogs and all.</param>
    /// <param name="checkoutOperations">Performs a checkout, including the questions it has to ask.</param>
    public TagsPageViewModel(
        IRepositoryContext repositoryContext,
        ITagOperations tagOperations,
        ICheckoutOperations checkoutOperations)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(tagOperations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);

        _tagOperations = tagOperations;
        _checkoutOperations = checkoutOperations;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);
        CreateCommand = new AsyncRelayCommand(OnCreateAsync, () => IsRepositoryOpen);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        CheckoutCommand = new AsyncRelayCommand<TagRowViewModel>(OnCheckoutAsync, row => row is not null);
        DeleteCommand = new AsyncRelayCommand<TagRowViewModel>(OnDeleteAsync, row => row is not null);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Tags";

    /// <summary>Gets the tags the repository holds, filtered by the search box.</summary>
    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    /// <summary>Gets what the filter box says it filters.</summary>
    public string SearchPlaceholder => "Filter tags";

    /// <summary>
    /// Gets or sets the tag the reader has selected.
    /// </summary>
    public TagRowViewModel? SelectedTag
    {
        get;
        set => SetProperty(ref field, value);
    }

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

    /// <summary>Gets the command that re-reads the references.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that opens the create-tag dialog.</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that checks a tag out, detaching HEAD.</summary>
    public AsyncRelayCommand<TagRowViewModel> CheckoutCommand { get; }

    /// <summary>Gets the command that deletes a tag.</summary>
    public AsyncRelayCommand<TagRowViewModel> DeleteCommand { get; }

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

        RefreshCommand.NotifyCanExecuteChanged();
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
    private void Rebuild()
    {
        string? selected = SelectedTag?.Name;

        Tags.Clear();

        foreach (GitTag tag in RepositoryContext.Refs.Tags)
        {
            if (Matches(tag.ShortName))
            {
                Tags.Add(new TagRowViewModel(tag, CheckoutCommand, DeleteCommand));
            }
        }

        SelectedTag = selected is null
            ? null
            : Tags.FirstOrDefault(row => row.Name == selected);

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
            await Run(() => _tagOperations.DeleteAsync(row.Name)).ConfigureAwait(true);
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
