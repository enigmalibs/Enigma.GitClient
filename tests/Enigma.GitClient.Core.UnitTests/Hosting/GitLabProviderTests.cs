using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Hosting.Providers;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// The GitLab provider, against scripted responses. No test here contacts the network.
/// </summary>
public sealed class GitLabProviderTests
{
    private static readonly HostAccount PublicAccount =
        HostAccount.Create(HostKind.GitLab, new Uri("https://gitlab.com"), "someone");

    private static readonly HostAccount SelfHosted =
        HostAccount.Create(HostKind.GitLab, new Uri("https://git.example.com"), "someone");

    private static readonly SecretString Token = new("glpat-token");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (GitLabProvider Provider, ScriptedHandler Handler) Build()
    {
        ScriptedHandler handler = new();

        return (new GitLabProvider(new StubClientFactory(handler)), handler);
    }

    [Fact]
    public void ItSaysWhichHostItIsAndAsksForNoIssueScope()
    {
        (GitLabProvider provider, _) = Build();

        Assert.Equal(HostKind.GitLab, provider.Kind);
        Assert.Equal("GitLab", provider.DisplayName);
        Assert.Equal(new Uri("https://gitlab.com"), provider.DefaultBaseUri);
        Assert.Contains("read_api", provider.TokenScopeHint, StringComparison.Ordinal);
        Assert.Contains("No issue or merge-request scope", provider.TokenScopeHint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://gitlab.com/group/repo.git", true)]
    [InlineData("git@gitlab.com:group/repo.git", true)]
    [InlineData("https://github.com/owner/repo.git", false)]
    [InlineData("https://git.example.com/group/repo.git", false)]
    public void ItClaimsThePublicHostAndNothingItCannotKnow(string remote, bool expected)
    {
        (GitLabProvider provider, _) = Build();

        Assert.Equal(expected, provider.MatchesRemote(RemoteUrl.Parse(remote)));
    }

    [Theory]
    [InlineData("https://gitlab.com", "https://gitlab.com/api/v4/")]
    [InlineData("https://git.example.com", "https://git.example.com/api/v4/")]
    [InlineData("https://git.example.com:8443", "https://git.example.com:8443/api/v4/")]
    public void TheApiIsAlwaysOnTheInstanceItself(string instance, string expected)
        => Assert.Equal(new Uri(expected), GitLabProvider.ApiBase(new Uri(instance)));

    [Fact]
    public async Task ValidatingATokenAsksWhoItBelongsTo()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""
            { "username": "ada", "name": "Ada Lovelace", "avatar_url": "https://gitlab.test/ada.png" }
            """));

        HostIdentity identity = await provider.ValidateCredentialAsync(
            PublicAccount,
            Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("ada", identity.UserName);
        Assert.Equal("Ada Lovelace", identity.DisplayName);

        HttpRequestMessage request = Assert.Single(handler.Requests);

        Assert.Equal(new Uri("https://gitlab.com/api/v4/user"), request.RequestUri);
        Assert.True(request.Headers.TryGetValues(GitLabProvider.TokenHeader, out IEnumerable<string>? values));
        Assert.Equal(["glpat-token"], values!);

        // The token goes in GitLab's own header, never in the query string where it would be logged.
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ASelfHostedInstanceIsAskedOnItsOwnHost()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "username": "ada" }"""));

        await provider.ValidateCredentialAsync(SelfHosted, Token, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://git.example.com/api/v4/user"), handler.Requests[0].RequestUri);
    }

    private const string OnePage = """
        [
          {
            "path_with_namespace": "group/subgroup/service",
            "path": "service",
            "name": "Service",
            "description": "The service",
            "default_branch": "main",
            "http_url_to_repo": "https://gitlab.com/group/subgroup/service.git",
            "ssh_url_to_repo": "git@gitlab.com:group/subgroup/service.git",
            "web_url": "https://gitlab.com/group/subgroup/service",
            "visibility": "private",
            "last_activity_at": "2026-09-01T10:11:12Z"
          },
          {
            "path_with_namespace": "ada/notes",
            "path": "notes",
            "name": "Notes",
            "description": null,
            "default_branch": "trunk",
            "http_url_to_repo": "https://gitlab.com/ada/notes.git",
            "ssh_url_to_repo": "git@gitlab.com:ada/notes.git",
            "web_url": "https://gitlab.com/ada/notes",
            "visibility": "public",
            "last_activity_at": "2026-08-20T09:00:00Z"
          }
        ]
        """;

    [Fact]
    public async Task ListingMapsEveryFieldTheInterfaceShows()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json(OnePage));

        HostRepositoryPage page = await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Repositories.Count);

        HostRepository first = page.Repositories[0];

        // A subgroup path stays a subgroup path: it is one project, not a group and a project.
        Assert.Equal("group/subgroup/service", first.FullName);
        Assert.Equal("service", first.Name);
        Assert.Equal("group/subgroup", first.Owner);
        Assert.Equal("The service", first.Description);
        Assert.Equal("main", first.DefaultBranch);
        Assert.Equal("https://gitlab.com/group/subgroup/service.git", first.CloneUrl);
        Assert.Equal("git@gitlab.com:group/subgroup/service.git", first.SshCloneUrl);
        Assert.Equal("https://gitlab.com/group/subgroup/service", first.WebUrl);
        Assert.True(first.IsPrivate);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 11, 12, TimeSpan.Zero), first.LastPushed);

        // Anything GitLab does not call public is private, including "internal".
        Assert.False(page.Repositories[1].IsPrivate);
    }

    [Fact]
    public async Task ListingAsksForTheProjectsTheAccountIsAMemberOf()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("[]"));

        await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(Search: "service", PageSize: 50),
            TestContext.Current.CancellationToken);

        string query = handler.Requests[0].RequestUri!.Query;

        Assert.Contains("membership=true", query, StringComparison.Ordinal);
        Assert.Contains("per_page=50", query, StringComparison.Ordinal);
        Assert.Contains("order_by=last_activity_at", query, StringComparison.Ordinal);

        // GitLab's listing does take a search term, so it is asked rather than filtered afterwards.
        Assert.Contains("search=service", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HostVisibility.Public, "visibility=public")]
    [InlineData(HostVisibility.Private, "visibility=private")]
    public async Task ListingPassesTheVisibilityFilterOn(HostVisibility visibility, string expected)
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("[]"));

        await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(Visibility: visibility),
            TestContext.Current.CancellationToken);

        Assert.Contains(expected, handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListingFollowsTheNextPageHeaderUntilItIsEmpty()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""[{ "path_with_namespace": "g/one", "path": "one" }]""");
            response.Headers.TryAddWithoutValidation("X-Next-Page", "2");

            return response;
        });

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""[{ "path_with_namespace": "g/two", "path": "two" }]""");
            response.Headers.TryAddWithoutValidation("X-Next-Page", "3");

            return response;
        });

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""[{ "path_with_namespace": "g/three", "path": "three" }]""");

            // Present and empty is how GitLab says "that was the last page".
            response.Headers.TryAddWithoutValidation("X-Next-Page", string.Empty);

            return response;
        });

        List<string> seen = [];
        string? cursor = null;

        for (int page = 0; page < 5; page++)
        {
            HostRepositoryPage current = await provider.ListRepositoriesAsync(
                PublicAccount,
                Token,
                new HostRepositoryQuery(Cursor: cursor),
                TestContext.Current.CancellationToken);

            foreach (HostRepository repository in current.Repositories)
            {
                seen.Add(repository.FullName);
            }

            cursor = current.NextCursor;

            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(["g/one", "g/two", "g/three"], seen);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("page=3", handler.Requests[2].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedTokenIsSaidToBeOne()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "message": "401 Unauthorized" }""", HttpStatusCode.Unauthorized));

        HostAuthenticationException exception = await Assert.ThrowsAsync<HostAuthenticationException>(
            () => provider.ValidateCredentialAsync(PublicAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("GitLab", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARateLimitCarriesItsResetTime()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        DateTimeOffset reset = DateTimeOffset.UtcNow.AddMinutes(10);

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("RateLimit-Remaining", "0");
            response.Headers.TryAddWithoutValidation(
                "RateLimit-Reset",
                reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

            return response;
        });

        HostRateLimitException exception = await Assert.ThrowsAsync<HostRateLimitException>(
            () => provider.ListRepositoriesAsync(
                PublicAccount,
                Token,
                new HostRepositoryQuery(),
                TestContext.Current.CancellationToken));

        Assert.Equal(reset.ToUnixTimeSeconds(), exception.ResetsAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task AWrongInstanceUrlSaysSo()
    {
        (GitLabProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("<html>sign in</html>"));

        HostRequestException exception = await Assert.ThrowsAsync<HostRequestException>(
            () => provider.ValidateCredentialAsync(PublicAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("instance URL", exception.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- deep links

    [Theory]
    [InlineData("https://gitlab.com/group/repo.git", "https://gitlab.com/group/repo/-/commit/abc123")]
    [InlineData("git@gitlab.com:group/repo.git", "https://gitlab.com/group/repo/-/commit/abc123")]
    [InlineData("https://gitlab.com/group/subgroup/repo.git", "https://gitlab.com/group/subgroup/repo/-/commit/abc123")]
    [InlineData("git@git.example.com:team/service.git", "https://git.example.com/team/service/-/commit/abc123")]
    [InlineData("ssh://git@altssh.gitlab.com:443/group/repo.git", "https://gitlab.com/group/repo/-/commit/abc123")]
    public void ACommitLinkKeepsTheWholeProjectPath(string remote, string expected)
    {
        (GitLabProvider provider, _) = Build();

        Assert.Equal(expected, provider.BuildCommitUrl(RemoteUrl.Parse(remote), "abc123"));
    }

    [Fact]
    public void ABranchAndAFileLinkUseGitLabsOwnDashedPaths()
    {
        (GitLabProvider provider, _) = Build();
        RemoteUrl remote = RemoteUrl.Parse("https://gitlab.com/group/subgroup/repo.git");

        Assert.Equal(
            "https://gitlab.com/group/subgroup/repo/-/tree/feature/some-work",
            provider.BuildBranchUrl(remote, "feature/some-work"));

        Assert.Equal(
            "https://gitlab.com/group/subgroup/repo/-/blob/main/src/a%20folder/file%20%231.cs#L42",
            provider.BuildFileUrl(remote, "main", "src/a folder/file #1.cs", 42));
    }

    [Fact]
    public void ALinkNeedsSomethingToPointAt()
    {
        (GitLabProvider provider, _) = Build();
        RemoteUrl remote = RemoteUrl.Parse("https://gitlab.com/group/repo.git");

        Assert.Null(provider.BuildCommitUrl(remote, string.Empty));
        Assert.Null(provider.BuildBranchUrl(remote, "   "));
        Assert.Null(provider.BuildFileUrl(remote, "main", string.Empty));
    }
}
