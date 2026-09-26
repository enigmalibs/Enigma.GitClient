using Avalonia.Data.Converters;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Sync;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// What each preference's choices are called in the interface.
/// </summary>
/// <remarks>
/// A dropdown that shows <c>SideBySide</c> is a dropdown showing an identifier. These converters
/// put the sentence in front of the user and leave the enum name in the code, without a ViewModel
/// per row to hold both.
/// </remarks>
public static class SettingsDescriptions
{
    /// <summary>Names a theme choice.</summary>
    public static readonly FuncValueConverter<ThemePreference, string> Theme =
        new(value => SettingsPageViewModel.Describe(value));

    /// <summary>Names a pull strategy.</summary>
    public static readonly FuncValueConverter<PullStrategy, string> Pull =
        new(value => SettingsPageViewModel.Describe(value));

    /// <summary>Names a date style.</summary>
    public static readonly FuncValueConverter<DateDisplay, string> Date =
        new(value => value == DateDisplay.Absolute ? "The date and time" : "How long ago");

    /// <summary>Names a file-list shape.</summary>
    public static readonly FuncValueConverter<FilesView, string> Files =
        new(value => value == FilesView.Tree ? "A tree of folders" : "A flat list");

    /// <summary>Names a diff shape.</summary>
    public static readonly FuncValueConverter<DiffView, string> Diff =
        new(value => value == DiffView.SideBySide ? "Side by side" : "Unified");
}
