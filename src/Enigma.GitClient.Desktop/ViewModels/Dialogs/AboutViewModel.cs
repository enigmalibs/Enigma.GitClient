using System;
using Enigma.GitClient.Core.Diagnostics;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// What the About dialog says: which product this is, and which build of it is running.
/// </summary>
/// <remarks>
/// Read-only: nothing here changes while the dialog is open. It derives from
/// <see cref="ViewModelBase"/> anyway, so it is the same shape as every other ViewModel in this folder.
/// </remarks>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance over the running build.
    /// </summary>
    public AboutViewModel()
        : this(ProductInformation.Version, ProductInformation.BuildSha, ProductInformation.Copyright)
    {
    }

    /// <summary>
    /// Initialises a new instance over given values.
    /// </summary>
    /// <param name="version">The version to show.</param>
    /// <param name="buildSha">The short revision to show, or <see langword="null"/> to show no build line.</param>
    /// <param name="copyright">The copyright notice, or an empty string to show no copyright line.</param>
    /// <remarks>
    /// The values are arguments rather than read here because <see cref="ProductInformation"/> reports
    /// the assembly under test, which a test cannot vary: this is the seam that lets a build with no
    /// revision be asserted at all.
    /// </remarks>
    public AboutViewModel(string version, string? buildSha, string copyright)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(copyright);

        Version = version;
        BuildSha = buildSha;
        Copyright = copyright;
    }

    /// <summary>Gets the product's name.</summary>
    public string DisplayName => ProductInformation.DisplayName;

    /// <summary>Gets the version of the running build.</summary>
    public string Version { get; }

    /// <summary>Gets the short commit this build was cut from, or <see langword="null"/> if it recorded none.</summary>
    public string? BuildSha { get; }

    /// <summary>
    /// Gets a value indicating whether there is a revision to show: a build with none shows no build
    /// line at all, not an empty one.
    /// </summary>
    public bool HasBuildSha => !string.IsNullOrEmpty(BuildSha);

    /// <summary>Gets the copyright notice, or an empty string if the assembly carries none.</summary>
    public string Copyright { get; }

    /// <summary>Gets a value indicating whether there is a copyright notice to show.</summary>
    public bool HasCopyright => Copyright.Length > 0;
}
