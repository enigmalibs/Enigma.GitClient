using System;

namespace Enigma.GitClient.Core.Diagnostics;

/// <summary>
/// The product version as the application states it: the version the user is told, and the source
/// revision the build was cut from, if the build recorded one.
/// </summary>
/// <param name="Version">The version, never <see langword="null"/> and never empty.</param>
/// <param name="BuildSha">The short source revision, or <see langword="null"/> when the build stamped none.</param>
/// <remarks>
/// Separate from <see cref="ProductInformation"/> because the parsing is the part worth testing: that
/// class reads the running assembly, which a test cannot vary, while <see cref="From"/> is a pure
/// function over the two strings the assembly holds.
/// </remarks>
public readonly record struct ProductVersion(string Version, string? BuildSha)
{
    /// <summary>
    /// What <see cref="From"/> reports when neither the informational version nor the fallback says
    /// anything usable. A version is always shown, so there has to be one.
    /// </summary>
    public const string Unknown = "0.0.0";

    /// <summary>
    /// How many characters of a source revision are kept — the length git itself abbreviates to, and
    /// the length of the short hash on every history row.
    /// </summary>
    private const int ShaLength = 7;

    /// <summary>
    /// Reads an <c>AssemblyInformationalVersion</c> value.
    /// </summary>
    /// <param name="informationalVersion">
    /// The attribute's value — <c>X.Y.Z</c>, or <c>X.Y.Z+&lt;sha&gt;</c> when the build recorded the
    /// source revision, which the SDK does in a git checkout.
    /// </param>
    /// <param name="fallbackVersion">
    /// What to report when <paramref name="informationalVersion"/> says nothing usable — the plain
    /// assembly version. May itself be <see langword="null"/>.
    /// </param>
    /// <returns>The version to show, and the short revision if there was one.</returns>
    /// <remarks>
    /// The version is everything before the first <c>+</c>, verbatim: a pre-release such as
    /// <c>1.1.0-rc.1</c> is the product's version exactly as the project states it. A value is unusable
    /// only when there is nothing before the <c>+</c> — it falls back rather than throwing, because a
    /// missing attribute must not be the reason an About box cannot open.
    /// </remarks>
    public static ProductVersion From(string? informationalVersion, string? fallbackVersion)
    {
        string candidate = informationalVersion?.Trim() ?? string.Empty;

        if (candidate.Length > 0)
        {
            int separator = candidate.IndexOf('+', StringComparison.Ordinal);
            string version = separator < 0 ? candidate : candidate[..separator];

            if (version.Length > 0)
            {
                return new ProductVersion(version, Shorten(separator < 0 ? null : candidate[(separator + 1)..]));
            }
        }

        string fallback = fallbackVersion?.Trim() ?? string.Empty;

        return new ProductVersion(fallback.Length > 0 ? fallback : Unknown, null);
    }

    /// <summary>
    /// Abbreviates a source revision to <see cref="ShaLength"/> characters, keeping a shorter one whole.
    /// </summary>
    private static string? Shorten(string? revision)
    {
        string trimmed = revision?.Trim() ?? string.Empty;

        return trimmed.Length switch
        {
            0 => null,
            <= ShaLength => trimmed,
            _ => trimmed[..ShaLength],
        };
    }
}
