using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Diagnostics;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// One line of the About dialog's attribution list: something this application redistributes, and the
/// licence it is redistributed under.
/// </summary>
/// <param name="Name">The component as its own project names it.</param>
/// <param name="License">Its licence, as an SPDX identifier or the licence's own name.</param>
public sealed record CreditEntry(string Name, string License);

/// <summary>
/// What the About dialog says: which product this is, which build of it is running, and what it is
/// built out of.
/// </summary>
/// <remarks>
/// Read-only: nothing here changes while the dialog is open. It derives from
/// <see cref="ViewModelBase"/> anyway, so it is the same shape as every other ViewModel in this folder.
/// </remarks>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// What the application redistributes, and under what terms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry was read from the package it names — the licence expression of its <c>.nuspec</c>,
    /// or the licence file the package ships — rather than recalled. One row per thing a reader would
    /// recognise, not one per assembly: the Avalonia row covers its platform backends, the
    /// Microsoft.Extensions row the hosting, logging and configuration packages, and the Enigma row the
    /// five Enigma libraries, which share an author and a licence.
    /// </para>
    /// <para>
    /// ANGLE is what Avalonia draws with on Windows, and ships in the Windows build only; Inter is the
    /// font, under its own licence rather than the MIT of the package that carries it.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<CreditEntry> DefaultCredits =
    [
        new CreditEntry("Avalonia", "MIT"),
        new CreditEntry("SkiaSharp", "MIT"),
        new CreditEntry("HarfBuzzSharp", "MIT"),
        new CreditEntry("ANGLE", "BSD-3-Clause"),
        new CreditEntry("CommunityToolkit.Mvvm", "MIT"),
        new CreditEntry("Microsoft.Extensions", "MIT"),
        new CreditEntry("BouncyCastle", "MIT"),
        new CreditEntry("Enigma libraries", "MIT"),
        new CreditEntry("Phosphor Icons", "MIT"),
        new CreditEntry("Inter", "SIL Open Font License 1.1"),
    ];

    /// <summary>
    /// Initialises a new instance over the running build.
    /// </summary>
    public AboutViewModel()
        : this(ProductInformation.Version, ProductInformation.BuildSha, ProductInformation.Copyright, DefaultCredits)
    {
    }

    /// <summary>
    /// Initialises a new instance over given values.
    /// </summary>
    /// <param name="version">The version to show.</param>
    /// <param name="buildSha">The short revision to show, or <see langword="null"/> to show no build line.</param>
    /// <param name="copyright">The copyright notice, or an empty string to show no copyright line.</param>
    /// <param name="credits">The attribution list.</param>
    /// <remarks>
    /// The values are arguments rather than read here because <see cref="ProductInformation"/> reports
    /// the assembly under test, which a test cannot vary: this is the seam that lets a build with no
    /// revision be asserted at all.
    /// </remarks>
    public AboutViewModel(string version, string? buildSha, string copyright, IReadOnlyList<CreditEntry> credits)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(copyright);
        ArgumentNullException.ThrowIfNull(credits);

        Version = version;
        BuildSha = buildSha;
        Copyright = copyright;
        Credits = credits;
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

    /// <summary>Gets what this application is built out of, and under what licence.</summary>
    public IReadOnlyList<CreditEntry> Credits { get; }
}
