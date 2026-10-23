using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// What the safety net's dialog says: that something went wrong and the application is still running,
/// what went wrong, how many more errors came, and the details for a bug report.
/// </summary>
/// <remarks>
/// It follows its batch: an error counted into it while the dialog is open shows at once, in the count
/// and in the details.
/// </remarks>
public sealed class UnhandledErrorViewModel : ViewModelBase
{
    private readonly UnhandledErrorBatch _batch;
    private readonly ISystemInterop _interop;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="batch">The errors to show.</param>
    /// <param name="interop">Puts the details on the clipboard.</param>
    /// <param name="remedy">What fixes it, when the application knows, or <see langword="null"/>.</param>
    public UnhandledErrorViewModel(UnhandledErrorBatch batch, ISystemInterop interop, string? remedy = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(interop);

        _batch = batch;
        _interop = interop;
        Remedy = remedy ?? string.Empty;

        CopyDetailsCommand = new AsyncRelayCommand(OnCopyDetailsAsync);

        batch.Changed += OnBatchChanged;
    }

    /// <summary>Gets the sentence that opens the dialog.</summary>
    public string Lead
        => "Something went wrong, but the application is still running. You can carry on, or close it and "
            + "start it again if something looks wrong.";

    /// <summary>Gets what went wrong.</summary>
    public string Message => _batch.Message;

    /// <summary>Gets what fixes it, when the application knows — the reset of unreadable stored tokens.</summary>
    public string Remedy { get; }

    /// <summary>Gets a value indicating whether there is a fix to offer.</summary>
    public bool HasRemedy => Remedy.Length > 0;

    /// <summary>Gets a value indicating whether more errors came after the first.</summary>
    public bool HasMore => _batch.More > 0;

    /// <summary>Gets how many more errors came after the first, as a sentence.</summary>
    public string MoreText
        => _batch.More == 1
            ? "1 more error occurred."
            : $"{_batch.More.ToString(CultureInfo.CurrentCulture)} more errors occurred.";

    /// <summary>Gets the details of every error, for a bug report.</summary>
    public string Details => _batch.Details;

    /// <summary>Gets the command that puts the details on the clipboard.</summary>
    public AsyncRelayCommand CopyDetailsCommand { get; }

    private Task OnCopyDetailsAsync() => _interop.CopyTextAsync(Details);

    private void OnBatchChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(Details));
    }
}
