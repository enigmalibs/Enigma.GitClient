using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Every profile, and the one whose repositories the start window lists.
/// </summary>
/// <param name="Profiles">Every profile, in the order they were added; never empty.</param>
/// <param name="Selected">The profile whose list is shown, one of <paramref name="Profiles"/>.</param>
public sealed record ProfileChoice(IReadOnlyList<IdentityProfile> Profiles, IdentityProfile Selected);

/// <summary>
/// Says which profile's list of repositories is the one in use, and remembers the user's choice.
/// </summary>
/// <remarks>
/// It only remembers the choice and never writes git's configuration itself: the start window's picker
/// and the Profiles page's Use, which choose a profile, also switch git's identity to it.
/// </remarks>
public interface IProfileSelection
{
    /// <summary>
    /// Reads the profiles — creating the default one when there is none — and works out the selected one.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The profiles and the selected one: the profile last chosen while it still exists, the first one
    /// otherwise.
    /// </returns>
    Task<ProfileChoice> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Remembers a profile as the one whose list is shown.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    void Select(string profileId);
}

/// <summary>
/// Default <see cref="IProfileSelection"/>: the profiles from their store, the choice in the settings.
/// </summary>
public sealed class ProfileSelection : IProfileSelection
{
    private readonly IIdentityProfileStore _profiles;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProfileSelection> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="profiles">Keeps the profiles, and creates the default one.</param>
    /// <param name="settings">Remembers the chosen profile.</param>
    /// <param name="logger">Receives a profiles file that could not be read or written.</param>
    public ProfileSelection(IIdentityProfileStore profiles, ISettingsService settings, ILogger<ProfileSelection> logger)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _profiles = profiles;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProfileChoice> LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IdentityProfile> profiles;

        try
        {
            profiles = await _profiles.EnsureAnyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The start window must still list something. The default profile stands in, under the
            // identifier it would have been saved with, so the repositories added meanwhile are still
            // its own once the file can be written again.
            _logger.LogWarning(exception, "The profiles could not be read or created; the default profile stands in");

            profiles = [IdentityProfile.CreateDefault()];
        }

        string remembered = _settings.Current.SelectedProfileId;
        IdentityProfile selected = profiles[0];

        foreach (IdentityProfile profile in profiles)
        {
            if (string.Equals(profile.Id, remembered, StringComparison.Ordinal))
            {
                selected = profile;
                break;
            }
        }

        return new ProfileChoice(profiles, selected);
    }

    /// <inheritdoc />
    public void Select(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (!string.Equals(_settings.Current.SelectedProfileId, profileId, StringComparison.Ordinal))
        {
            _settings.Update(current => current with { SelectedProfileId = profileId });
        }
    }
}
