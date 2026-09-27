using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// A hosting provider that answers from a script, so the page can be driven with no network and no
/// real account.
/// </summary>
internal sealed class FakeHostProvider : IRepositoryHostProvider
{
    private readonly Queue<HostRepositoryPage> _pages = new();

    public HostKind Kind => HostKind.GitHub;

    public string DisplayName => "GitHub";

    public Uri DefaultBaseUri { get; } = new("https://github.com");

    public string TokenScopeHint => "A token with the 'repo' scope. No issue or pull-request scope is ever requested.";

    public string GitUserName => "x-access-token";

    /// <summary>What validation answers, or the exception it throws instead.</summary>
    public HostIdentity Identity { get; set; } = new("octocat", "The Octocat");

    public Exception? ValidationFailure { get; set; }

    public Exception? ListingFailure { get; set; }

    public List<SecretString> TokensSeen { get; } = [];

    public List<HostRepositoryQuery> Queries { get; } = [];

    public void Returns(params HostRepositoryPage[] pages)
    {
        foreach (HostRepositoryPage page in pages)
        {
            _pages.Enqueue(page);
        }
    }

    public bool MatchesRemote(RemoteUrl remote) => WellKnownHosts.Detect(remote) == HostKind.GitHub;

    public Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
    {
        TokensSeen.Add(token);

        return ValidationFailure is not null
            ? Task.FromException<HostIdentity>(ValidationFailure)
            : Task.FromResult(Identity);
    }

    public Task<HostRepositoryPage> ListRepositoriesAsync(
        HostAccount account,
        SecretString token,
        HostRepositoryQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);

        return ListingFailure is not null
            ? Task.FromException<HostRepositoryPage>(ListingFailure)
            : Task.FromResult(_pages.Count > 0 ? _pages.Dequeue() : HostRepositoryPage.Empty);
    }

    public string? BuildCommitUrl(RemoteUrl remote, string sha)
        => $"https://github.com/{remote.Path}/commit/{sha}";

    public string? BuildBranchUrl(RemoteUrl remote, string branch)
        => $"https://github.com/{remote.Path}/tree/{branch}";

    public string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null)
        => $"https://github.com/{remote.Path}/blob/{reference}/{path}";
}
