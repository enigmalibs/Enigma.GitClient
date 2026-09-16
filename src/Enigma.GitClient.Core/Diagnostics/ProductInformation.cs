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
    /// The scope statement the product is built to. Rebase is deliberately absent from this client,
    /// and issue and pull-request workflows are out of scope.
    /// </summary>
    public const string ScopeStatement =
        "Enigma.GitClient never rebases, and it does not handle issues or pull requests.";

    /// <summary>
    /// Gets the informational version of the running assembly, falling back to its assembly version.
    /// </summary>
    /// <returns>A displayable version string.</returns>
    public static string GetVersion()
    {
        Assembly assembly = typeof(ProductInformation).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the source-revision suffix the SDK appends (e.g. "1.0.0+9a1b2c3").
            int plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }

        Version? version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }
}
