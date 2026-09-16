using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Hosting.Providers;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// Hands a provider a client built over a scripted handler, so a test drives a whole API
/// conversation without a network.
/// </summary>
internal sealed class StubClientFactory : IHttpClientFactory
{
    public StubClientFactory(ScriptedHandler handler, bool withRetry = false)
    {
        Handler = handler;

        if (withRetry)
        {
            Retry = new HostRetryHandler((_, _) => Task.CompletedTask) { InnerHandler = handler };
            Client = new HttpClient(Retry);
        }
        else
        {
            Client = new HttpClient(handler);
        }
    }

    public ScriptedHandler Handler { get; }

    public HostRetryHandler? Retry { get; }

    private HttpClient Client { get; }

    public HttpClient CreateClient(string name) => Client;
}

/// <summary>
/// The GitHub provider, against scripted responses. No test here contacts the network.
/// </summary>
public sealed class GitHubProviderTests
{
    private static readonly HostAccount PublicAccount =
        HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), "octocat");

    private static readonly HostAccount EnterpriseAccount =
        HostAccount.Create(HostKind.GitHub, new Uri("https://github.contoso.com"), "octocat");

    private static readonly SecretString Token = new("ghp_token");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        HttpResponseMessage response = new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        return response;
    }

    private static (GitHubProvider Provider, ScriptedHandler Handler) Build(bool withRetry = false)
    {
        ScriptedHandler handler = new();

        return (new GitHubProvider(new StubClientFactory(handler, withRetry)), handler);
    }

    // ---------------------------------------------------------------- what it is

    [Fact]
    public void ItSaysWhichHostItIsAndAsksForNoIssueScope()
    {
        (GitHubProvider provider, _) = Build();

        Assert.Equal(HostKind.GitHub, provider.Kind);
        Assert.Equal("GitHub", provider.DisplayName);
        Assert.Equal(new Uri("https://github.com"), provider.DefaultBaseUri);
        Assert.Contains("repo", provider.TokenScopeHint, StringComparison.Ordinal);
        Assert.Contains("No issue or pull-request scope", provider.TokenScopeHint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", true)]
    [InlineData("git@github.com:owner/repo.git", true)]
    [InlineData("https://gitlab.com/owner/repo.git", false)]
    [InlineData("https://github.contoso.com/owner/repo.git", false)]
    public void ItClaimsThePublicHostAndNothingItCannotKnow(string remote, bool expected)
    {
        (GitHubProvider provider, _) = Build();

        Assert.Equal(expected, provider.MatchesRemote(RemoteUrl.Parse(remote)));
    }

    [Theory]
    [InlineData("https://github.com", "https://api.github.com/")]
    [InlineData("https://www.github.com", "https://api.github.com/")]
    [InlineData("https://api.github.com", "https://api.github.com/")]
    [InlineData("https://github.contoso.com", "https://github.contoso.com/api/v3/")]
    [InlineData("https://github.contoso.com:8443", "https://github.contoso.com:8443/api/v3/")]
    public void TheApiBaseFollowsTheInstance(string instance, string expected)
        => Assert.Equal(new Uri(expected), GitHubProvider.ApiBase(new Uri(instance)));

    // ---------------------------------------------------------------- identity

    [Fact]
    public async Task ValidatingATokenAsksWhoItBelongsTo()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""
            { "login": "octocat", "name": "The Octocat", "avatar_url": "https://avatars.test/octocat.png" }
            """));

        HostIdentity identity = await provider.ValidateCredentialAsync(
            PublicAccount,
            Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("octocat", identity.UserName);
        Assert.Equal("The Octocat", identity.DisplayName);
        Assert.Equal(new Uri("https://avatars.test/octocat.png"), identity.AvatarUrl);

        HttpRequestMessage request = Assert.Single(handler.Requests);

        Assert.Equal(new Uri("https://api.github.com/user"), request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("ghp_token", request.Headers.Authorization.Parameter);
        Assert.True(request.Headers.TryGetValues("X-GitHub-Api-Version", out IEnumerable<string>? version));
        Assert.Equal([GitHubProvider.ApiVersion], version!);
    }

    [Fact]
    public async Task AnAccountWithNoNameIsCalledByItsLogin()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "login": "octocat", "name": null }"""));

        HostIdentity identity = await provider.ValidateCredentialAsync(
            PublicAccount,
            Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("octocat", identity.DisplayName);
        Assert.Null(identity.AvatarUrl);
    }

    [Fact]
    public async Task AnEnterpriseServerIsAskedOnItsOwnPath()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "login": "someone" }"""));

        await provider.ValidateCredentialAsync(EnterpriseAccount, Token, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://github.contoso.com/api/v3/user"), handler.Requests[0].RequestUri);
    }

    // ---------------------------------------------------------------- listing

    private const string OnePage = """
        [
          {
            "full_name": "octocat/hello-world",
            "name": "hello-world",
            "description": "My first repository",
            "default_branch": "main",
            "clone_url": "https://github.com/octocat/hello-world.git",
            "ssh_url": "git@github.com:octocat/hello-world.git",
            "html_url": "https://github.com/octocat/hello-world",
            "private": false,
            "pushed_at": "2026-09-01T10:11:12Z"
          },
          {
            "full_name": "contoso/secret-plans",
            "name": "secret-plans",
            "description": null,
            "default_branch": "trunk",
            "clone_url": "https://github.com/contoso/secret-plans.git",
            "ssh_url": "git@github.com:contoso/secret-plans.git",
            "html_url": "https://github.com/contoso/secret-plans",
            "private": true,
            "pushed_at": "2026-08-20T09:00:00Z"
          }
        ]
        """;

    [Fact]
    public async Task ListingMapsEveryFieldTheInterfaceShows()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json(OnePage));

        HostRepositoryPage page = await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Repositories.Count);
        Assert.False(page.HasMore);

        HostRepository first = page.Repositories[0];

        Assert.Equal("octocat/hello-world", first.FullName);
        Assert.Equal("hello-world", first.Name);
        Assert.Equal("octocat", first.Owner);
        Assert.Equal("My first repository", first.Description);
        Assert.Equal("main", first.DefaultBranch);
        Assert.Equal("https://github.com/octocat/hello-world.git", first.CloneUrl);
        Assert.Equal("git@github.com:octocat/hello-world.git", first.SshCloneUrl);
        Assert.Equal("https://github.com/octocat/hello-world", first.WebUrl);
        Assert.False(first.IsPrivate);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 11, 12, TimeSpan.Zero), first.LastPushed);

        HostRepository second = page.Repositories[1];

        Assert.True(second.IsPrivate);
        Assert.Null(second.Description);
        Assert.Equal("trunk", second.DefaultBranch);
    }

    [Fact]
    public async Task ListingAsksForTheAccountsOwnRepositoriesNewestFirst()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("[]"));

        await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(PageSize: 50),
            TestContext.Current.CancellationToken);

        string query = handler.Requests[0].RequestUri!.Query;

        Assert.Contains("per_page=50", query, StringComparison.Ordinal);
        Assert.Contains("sort=pushed", query, StringComparison.Ordinal);
        Assert.Contains("affiliation=owner,collaborator,organization_member", query, StringComparison.Ordinal);
        Assert.Contains("visibility=all", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HostVisibility.Public, "visibility=public")]
    [InlineData(HostVisibility.Private, "visibility=private")]
    [InlineData(HostVisibility.All, "visibility=all")]
    public async Task ListingPassesTheVisibilityFilterOn(HostVisibility visibility, string expected)
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("[]"));

        await provider.ListRepositoriesAsync(
            PublicAccount,
            Token,
            new HostRepositoryQuery(Visibility: visibility),
            TestContext.Current.CancellationToken);

        Assert.Contains(expected, handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListingFollowsTheLinkHeaderAcrossThreePages()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""[{ "full_name": "o/one", "name": "one" }]""");
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.github.com/user/repos?page=2>; rel=\"next\", <https://api.github.com/user/repos?page=3>; rel=\"last\"");

            return response;
        });

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""[{ "full_name": "o/two", "name": "two" }]""");
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.github.com/user/repos?page=1>; rel=\"prev\", <https://api.github.com/user/repos?page=3>; rel=\"next\"");

            return response;
        });

        handler.Respond(_ => Json("""[{ "full_name": "o/three", "name": "three" }]"""));

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

        Assert.Equal(["o/one", "o/two", "o/three"], seen);
        Assert.Equal(3, handler.Requests.Count);

        // The cursor is followed exactly as GitHub gave it, rather than rebuilt from a page number.
        Assert.Equal(new Uri("https://api.github.com/user/repos?page=2"), handler.Requests[1].RequestUri);
        Assert.Equal(new Uri("https://api.github.com/user/repos?page=3"), handler.Requests[2].RequestUri);
    }

    [Fact]
    public void NextLink_IgnoresEveryRelationButNext()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation(
            "Link",
            "<https://api.github.com/user/repos?page=1>; rel=\"prev\", <https://api.github.com/user/repos?page=9>; rel=\"last\"");

        Assert.Null(GitHubProvider.NextLink(response));

        using HttpResponseMessage none = new(HttpStatusCode.OK);

        Assert.Null(GitHubProvider.NextLink(none));
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task ARejectedTokenIsSaidToBeOne()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "message": "Bad credentials" }""", HttpStatusCode.Unauthorized));

        HostAuthenticationException exception = await Assert.ThrowsAsync<HostAuthenticationException>(
            () => provider.ValidateCredentialAsync(PublicAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("GitHub", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARateLimitedForbiddenCarriesTheResetTime()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        DateTimeOffset reset = DateTimeOffset.UtcNow.AddMinutes(30);

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""{ "message": "API rate limit exceeded" }""", HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
            response.Headers.TryAddWithoutValidation(
                "x-ratelimit-reset",
                reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

            return response;
        });

        HostRateLimitException exception = await Assert.ThrowsAsync<HostRateLimitException>(
            () => provider.ListRepositoriesAsync(
                PublicAccount,
                Token,
                new HostRepositoryQuery(),
                TestContext.Current.CancellationToken));

        Assert.NotNull(exception.ResetsAt);
        Assert.Equal(reset.ToUnixTimeSeconds(), exception.ResetsAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task AForbiddenWithRequestsLeftIsAMissingScopeRatherThanARateLimit()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("""{ "message": "Resource not accessible" }""", HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "4999");

            return response;
        });

        await Assert.ThrowsAsync<HostAuthenticationException>(
            () => provider.ListRepositoriesAsync(
                PublicAccount,
                Token,
                new HostRepositoryQuery(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWrongInstanceUrlSaysSo()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("<html>not an api</html>"));

        HostRequestException exception = await Assert.ThrowsAsync<HostRequestException>(
            () => provider.ValidateCredentialAsync(PublicAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("instance URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerErrorIsRetriedOnceAndThenSurfaced()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build(withRetry: true);

        handler.Respond(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);

        HostRequestException exception = await Assert.ThrowsAsync<HostRequestException>(
            () => provider.ValidateCredentialAsync(PublicAccount, Token, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AServerErrorThatPassesOnTheRetryIsNotAFailureAtAll()
    {
        (GitHubProvider provider, ScriptedHandler handler) = Build(withRetry: true);

        handler.Respond(HttpStatusCode.ServiceUnavailable);
        handler.Respond(_ => Json("""{ "login": "octocat" }"""));

        HostIdentity identity = await provider.ValidateCredentialAsync(
            PublicAccount,
            Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("octocat", identity.UserName);
        Assert.Equal(2, handler.Requests.Count);
    }

    // ---------------------------------------------------------------- deep links

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo/commit/abc123")]
    [InlineData("git@github.com:owner/repo.git", "https://github.com/owner/repo/commit/abc123")]
    [InlineData("ssh://git@ssh.github.com/owner/repo.git", "https://github.com/owner/repo/commit/abc123")]
    [InlineData("https://github.contoso.com/team/repo.git", "https://github.contoso.com/team/repo/commit/abc123")]
    public void ACommitLinkFollowsWhereTheRemotePoints(string remote, string expected)
    {
        (GitHubProvider provider, _) = Build();

        Assert.Equal(expected, provider.BuildCommitUrl(RemoteUrl.Parse(remote), "abc123"));
    }

    [Fact]
    public void ABranchLinkKeepsTheSlashesInTheBranchName()
    {
        (GitHubProvider provider, _) = Build();

        Assert.Equal(
            "https://github.com/owner/repo/tree/feature/some-work",
            provider.BuildBranchUrl(RemoteUrl.Parse("https://github.com/owner/repo.git"), "feature/some-work"));
    }

    [Fact]
    public void AFileLinkEscapesWhatWouldOtherwiseEndTheUrl()
    {
        (GitHubProvider provider, _) = Build();

        string? url = provider.BuildFileUrl(
            RemoteUrl.Parse("https://github.com/owner/repo.git"),
            "main",
            "src/a folder/file #1.cs",
            42);

        Assert.Equal(
            "https://github.com/owner/repo/blob/main/src/a%20folder/file%20%231.cs#L42",
            url);
    }

    [Fact]
    public void AFileLinkWithNoLineHasNoAnchor()
    {
        (GitHubProvider provider, _) = Build();

        Assert.Equal(
            "https://github.com/owner/repo/blob/main/README.md",
            provider.BuildFileUrl(RemoteUrl.Parse("https://github.com/owner/repo.git"), "main", "README.md"));
    }

    [Theory]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/")]
    public void ARemoteThatNamesNoRepositoryHasNoLinks(string remote)
    {
        (GitHubProvider provider, _) = Build();
        RemoteUrl parsed = RemoteUrl.Parse(remote);

        Assert.Null(provider.BuildCommitUrl(parsed, "abc123"));
        Assert.Null(provider.BuildBranchUrl(parsed, "main"));
        Assert.Null(provider.BuildFileUrl(parsed, "main", "README.md"));
    }

    [Fact]
    public void ALinkNeedsSomethingToPointAt()
    {
        (GitHubProvider provider, _) = Build();
        RemoteUrl remote = RemoteUrl.Parse("https://github.com/owner/repo.git");

        Assert.Null(provider.BuildCommitUrl(remote, string.Empty));
        Assert.Null(provider.BuildBranchUrl(remote, "   "));
        Assert.Null(provider.BuildFileUrl(remote, "main", string.Empty));
    }
}
