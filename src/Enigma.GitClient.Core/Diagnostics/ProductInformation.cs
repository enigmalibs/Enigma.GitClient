using System;
using System.Reflection;

namespace Enigma.GitClient.Core.Diagnostics;

/// <summary>
/// Identity of the Enigma.GitClient product, used for user agents, about screens and log headers.
/// </summary>
public static class ProductInformation
{
    /// <summary>
    /// The product name as shown to the user.
    /// </summary>
    public const string Name = "Enigma.GitClient";

    /// <summary>
    /// The product's name in words, as the surfaces that present the product by name show it: the
    /// splash screen, the About dialog and the Linux launcher entry.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Name"/> rather than in place of it: that one is also the product token of the
    /// HTTP user agent, which cannot carry a space.
    /// </remarks>
    public const string DisplayName = "Enigma git client";

    /// <summary>
    /// The scope statement the product is built to. Rebase is deliberately absent from this client,
    /// and issue and pull-request workflows are out of scope.
    /// </summary>
    public const string ScopeStatement =
        "Enigma.GitClient never rebases, and it does not handle issues or pull requests.";

    /// <summary>
    /// The version and the revision this build was cut from, read once from this assembly.
    /// </summary>
    /// <remarks>
    /// From <b>this</b> assembly rather than the entry assembly: Core and App carry one version between
    /// them, and under a test host the entry assembly is the runner.
    /// </remarks>
    private static readonly ProductVersion Product = ProductVersion.From(
        typeof(ProductInformation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        FallbackVersion());

    /// <summary>
    /// Gets the displayable version, as a property markup can read with <c>x:Static</c>.
    /// </summary>
    public static string Version => Product.Version;

    /// <summary>
    /// Gets the short commit this build was cut from, or <see langword="null"/> when it recorded none —
    /// a build from a source drop rather than from the repository.
    /// </summary>
    public static string? BuildSha => Product.BuildSha;

    /// <summary>
    /// Gets the copyright notice <c>Directory.Build.props</c> stamps on the assembly, or an empty string
    /// when it carries none.
    /// </summary>
    public static string Copyright { get; } =
        typeof(ProductInformation).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright?.Trim() ?? string.Empty;

    /// <summary>
    /// Gets the informational version of the running assembly, without the source revision the SDK
    /// appends, falling back to its assembly version.
    /// </summary>
    /// <returns>A displayable version string.</returns>
    public static string GetVersion() => Version;

    /// <summary>
    /// The plain assembly version, for when the informational one says nothing usable, trimmed to
    /// three components: the fourth is a zero nobody wrote.
    /// </summary>
    private static string? FallbackVersion()
    {
        Version? version = typeof(ProductInformation).Assembly.GetName().Version;

        return version is null ? null : version.ToString(version.Build >= 0 ? 3 : 2);
    }
}
