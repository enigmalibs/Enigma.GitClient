using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the identity page: the name and email git records on every commit.
/// </summary>
/// <remarks>
/// <para>
/// The page edits git's own configuration rather than a copy of it: what it shows is what git read a
/// moment ago, and a save is a <c>git config</c> write. Nothing is saved while typing — each save
/// rewrites a configuration file — so each section has its own Save, enabled once the values differ
/// from git's and are usable.
/// </para>
/// <para>
/// Names and emails are never logged: they identify a person. A failure is logged by what failed.
/// </para>
/// </remarks>
public sealed class IdentityPageViewModel : PageViewModelBase
{
    private readonly IGitIdentityService _identity;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<IdentityPageViewModel> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="identity">Reads and writes git's identity.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public IdentityPageViewModel(
        IRepositoryContext repositoryContext,
        IGitIdentityService identity,
        IInfoBarService infoBar,
        ILogger<IdentityPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _identity = identity;
        _infoBar = infoBar;
        _logger = logger;

        SaveGlobalCommand = new AsyncRelayCommand(OnSaveGlobalAsync, CanSaveGlobal);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Git identity";

    // ---------------------------------------------------------------- the global identity

    /// <summary>
    /// Gets the global identity as git last reported it.
    /// </summary>
    public GitIdentity GlobalIdentity
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsGlobalUnset));
                OnPropertyChanged(nameof(GlobalSummary));
                OnGlobalEdited();
            }
        }
    } = GitIdentity.Empty;

    /// <summary>Gets or sets the global name, as typed.</summary>
    public string GlobalName
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnGlobalEdited();
            }
        }
    } = string.Empty;

    /// <summary>Gets or sets the global email, as typed.</summary>
    public string GlobalEmail
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnGlobalEdited();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the typed values differ from what git has, trimmed.
    /// </summary>
    public bool IsGlobalChanged => !TypedGlobal.Equals(GlobalIdentity.Normalised());

    /// <summary>
    /// Gets what is wrong with the typed values, or an empty string. Only said once something has been
    /// typed: an identity git does not have yet is not a mistake of the reader's.
    /// </summary>
    public string GlobalError => IsGlobalChanged ? GitIdentityRules.Validate(TypedGlobal) ?? string.Empty : string.Empty;

    /// <summary>Gets a value indicating whether there is something wrong to say.</summary>
    public bool HasGlobalError => GlobalError.Length > 0;

    /// <summary>
    /// Gets a value indicating whether git has no complete global identity, which is when it refuses to
    /// commit in a repository that does not set one.
    /// </summary>
    public bool IsGlobalUnset => !GlobalIdentity.IsComplete;

    /// <summary>Gets the sentence saying who commits are made as.</summary>
    public string GlobalSummary
        => IsGlobalUnset
            ? "Not set. git refuses to commit until a name and an email are set, here or in the repository."
            : $"New commits are made as {GlobalIdentity}, unless a repository sets its own identity.";

    /// <summary>Gets the command that writes the typed values to the global configuration.</summary>
    public AsyncRelayCommand SaveGlobalCommand { get; }

    private GitIdentity TypedGlobal => new GitIdentity(GlobalName, GlobalEmail).Normalised();

    // ---------------------------------------------------------------- loading

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the identity from git again.
    /// </summary>
    /// <returns>A task that completes once the page shows what git has.</returns>
    /// <remarks>
    /// Values the reader has typed and not saved are kept: coming back to the page must not throw
    /// away an edit. Values nobody touched follow git.
    /// </remarks>
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            GitIdentity loaded = await _identity.GetGlobalAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
            bool edited = IsGlobalChanged;

            GlobalIdentity = loaded;

            if (!edited)
            {
                GlobalName = loaded.Name;
                GlobalEmail = loaded.Email;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException)
        {
            _logger.LogWarning("Reading the global git identity failed ({Kind})", exception.GetType().Name);

            await ReportAsync("Could not read the git identity", Describe(exception), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc />
    protected override void OnBusyChanged() => SaveGlobalCommand.NotifyCanExecuteChanged();

    // ---------------------------------------------------------------- commands

    private bool CanSaveGlobal() => !IsBusy && IsGlobalChanged && GitIdentityRules.Validate(TypedGlobal) is null;

    private async Task OnSaveGlobalAsync()
    {
        GitIdentity typed = TypedGlobal;

        if (GitIdentityRules.Validate(typed) is not null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _identity.SetGlobalAsync(typed, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            GlobalIdentity = typed;
            GlobalName = typed.Name;
            GlobalEmail = typed.Email;

            await ReportAsync("Global identity saved", $"New commits are made as {typed}.", InfoBarSeverity.Success)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException or ArgumentException)
        {
            // What was typed stays in the fields, so a retry is one click.
            _logger.LogWarning("Saving the global git identity failed ({Kind})", exception.GetType().Name);

            await ReportAsync("Could not save the git identity", Describe(exception), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Says what went wrong in git's own words where there are some — not the exception's message,
    /// which repeats the command line and so the values.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            GitCommandException { StandardError: { } error } when error.Trim().Length > 0 => error.Trim(),
            GitCommandException command => $"git stopped with exit code {command.ExitCode}.",
            ArgumentException { ParamName: { } parameter } argument
                => argument.Message.Replace($" (Parameter '{parameter}')", string.Empty, StringComparison.Ordinal),
            _ => exception.Message,
        };
    }

    private void OnGlobalEdited()
    {
        OnPropertyChanged(nameof(IsGlobalChanged));
        OnPropertyChanged(nameof(GlobalError));
        OnPropertyChanged(nameof(HasGlobalError));
        SaveGlobalCommand.NotifyCanExecuteChanged();
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
