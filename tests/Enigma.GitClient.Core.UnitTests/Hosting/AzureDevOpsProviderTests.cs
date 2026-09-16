using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Hosting.Providers;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// The Azure DevOps provider, against scripted responses. No test here contacts the network.
/// </summary>
public sealed class AzureDevOpsProviderTests
{
    private static readonly HostAccount CloudAccount =
        HostAccount.Create(HostKind.AzureDevOps, new Uri("https://dev.azure.com/contoso"), "ada");

    private static readonly HostAccount LegacyAccount =
        HostAccount.Create(HostKind.AzureDevOps, new Uri("https://contoso.visualstudio.com"), "ada");

    private static readonly HostAccount ServerAccount =
        HostAccount.Create(HostKind.AzureDevOps, new Uri("https://tfs.example.com/tfs/DefaultCollection"), "ada");

    private static readonly SecretString Token = new("pat-token");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AzureDevOpsProvider Provider, ScriptedHandler Handler) Build()
    {
        ScriptedHandler handler = new();

        return (new AzureDevOpsProvider(new StubClientFactory(handler)), handler);
    }

    [Fact]
    public void ItSaysWhichHostItIsAndAsksForNoWorkItemScope()
    {
        (AzureDevOpsProvider provider, _) = Build();

        Assert.Equal(HostKind.AzureDevOps, provider.Kind);
        Assert.Equal("Azure DevOps", provider.DisplayName);
        Assert.Contains("Code: Read", provider.TokenScopeHint, StringComparison.Ordinal);
        Assert.Contains("No work-item or pull-request scope", provider.TokenScopeHint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://dev.azure.com/contoso/project/_git/repo", true)]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/project/repo", true)]
    [InlineData("https://contoso.visualstudio.com/project/_git/repo", true)]
    [InlineData("https://github.com/owner/repo.git", false)]
    public void ItClaimsEveryFormOfItsOwnHost(string remote, bool expected)
    {
        (AzureDevOpsProvider provider, _) = Build();

        Assert.Equal(expected, provider.MatchesRemote(RemoteUrl.Parse(remote)));
    }

    [Theory]
    [InlineData("https://dev.azure.com/contoso", "https://dev.azure.com/contoso/_apis/")]
    [InlineData("https://dev.azure.com/contoso/", "https://dev.azure.com/contoso/_apis/")]
    [InlineData("https://contoso.visualstudio.com", "https://contoso.visualstudio.com/_apis/")]
    [InlineData("https://tfs.example.com/tfs/DefaultCollection", "https://tfs.example.com/tfs/DefaultCollection/_apis/")]
    public void TheApiFollowsTheOrganisationOrCollectionPath(string instance, string expected)
        => Assert.Equal(new Uri(expected), AzureDevOpsProvider.ApiBase(new Uri(instance)));

    // ---------------------------------------------------------------- identity

    [Fact]
    public async Task ValidatingATokenAsksWhoTheConnectionBelongsTo()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""
            {
              "authenticatedUser": {
                "providerDisplayName": "Ada Lovelace",
                "properties": { "Account": { "$value": "ada@contoso.com" } }
              }
            }
            """));

        HostIdentity identity = await provider.ValidateCredentialAsync(
            CloudAccount,
            Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("ada@contoso.com", identity.UserName);
        Assert.Equal("Ada Lovelace", identity.DisplayName);

        HttpRequestMessage request = Assert.Single(handler.Requests);

        Assert.Equal(
            new Uri("https://dev.azure.com/contoso/_apis/connectionData?api-version=7.1"),
            request.RequestUri);

        // Basic with an empty user name and the token as the password.
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(":pat-token", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task ASignInPageIsTreatedAsARejectedToken()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        // Azure DevOps answers a bad token with a sign-in page and a 203, not a 401.
        handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation)
        {
            Content = new StringContent("<html><body>Sign in</body></html>", Encoding.UTF8, "text/html"),
        });

        HostAuthenticationException exception = await Assert.ThrowsAsync<HostAuthenticationException>(
            () => provider.ValidateCredentialAsync(CloudAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("Code: Read", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnauthorisedAnswerIsSaidToBeARejectedToken()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("{}", HttpStatusCode.Unauthorized));

        HostAuthenticationException exception = await Assert.ThrowsAsync<HostAuthenticationException>(
            () => provider.ValidateCredentialAsync(CloudAccount, Token, TestContext.Current.CancellationToken));

        Assert.Contains("Azure DevOps", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARateLimitedAnswerCarriesItsWait()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ =>
        {
            HttpResponseMessage response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));

            return response;
        });

        HostRateLimitException exception = await Assert.ThrowsAsync<HostRateLimitException>(
            () => provider.ListRepositoriesAsync(
                CloudAccount,
                Token,
                new HostRepositoryQuery(),
                TestContext.Current.CancellationToken));

        Assert.NotNull(exception.ResetsAt);
    }

    // ---------------------------------------------------------------- listing

    private const string Repositories = """
        {
          "count": 2,
          "value": [
            {
              "name": "service",
              "project": { "name": "Platform", "visibility": "private" },
              "defaultBranch": "refs/heads/main",
              "remoteUrl": "https://dev.azure.com/contoso/Platform/_git/service",
              "sshUrl": "git@ssh.dev.azure.com:v3/contoso/Platform/service",
              "webUrl": "https://dev.azure.com/contoso/Platform/_git/service"
            },
            {
              "name": "docs",
              "project": { "name": "Public", "visibility": "public" },
              "defaultBranch": "refs/heads/trunk",
              "remoteUrl": "https://dev.azure.com/contoso/Public/_git/docs",
              "sshUrl": "git@ssh.dev.azure.com:v3/contoso/Public/docs",
              "webUrl": "https://dev.azure.com/contoso/Public/_git/docs"
            }
          ]
        }
        """;

    [Fact]
    public async Task ListingMapsEveryFieldTheInterfaceShows()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json(Repositories));

        HostRepositoryPage page = await provider.ListRepositoriesAsync(
            CloudAccount,
            Token,
            new HostRepositoryQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Repositories.Count);

        // One call returns the whole organisation, so there is never a second page to ask for.
        Assert.False(page.HasMore);

        HostRepository first = page.Repositories[0];

        Assert.Equal("Platform/service", first.FullName);
        Assert.Equal("service", first.Name);
        Assert.Equal("Platform", first.Owner);
        Assert.Equal("main", first.DefaultBranch);
        Assert.Equal("https://dev.azure.com/contoso/Platform/_git/service", first.CloneUrl);
        Assert.Equal("git@ssh.dev.azure.com:v3/contoso/Platform/service", first.SshCloneUrl);
        Assert.True(first.IsPrivate);

        Assert.Equal("trunk", page.Repositories[1].DefaultBranch);
        Assert.False(page.Repositories[1].IsPrivate);

        Assert.Equal(
            new Uri("https://dev.azure.com/contoso/_apis/git/repositories?api-version=7.1"),
            handler.Requests[0].RequestUri);
    }

    [Fact]
    public async Task TheVisibilityFilterAndTheSearchAreAppliedToWhatCameBack()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json(Repositories));

        HostRepositoryPage privateOnly = await provider.ListRepositoriesAsync(
            CloudAccount,
            Token,
            new HostRepositoryQuery(Visibility: HostVisibility.Private),
            TestContext.Current.CancellationToken);

        // The endpoint takes neither filter, so the provider applies them rather than pretending.
        Assert.Equal("Platform/service", Assert.Single(privateOnly.Repositories).FullName);

        handler.Respond(_ => Json(Repositories));

        HostRepositoryPage searched = await provider.ListRepositoriesAsync(
            CloudAccount,
            Token,
            new HostRepositoryQuery(Search: "docs"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Public/docs", Assert.Single(searched.Repositories).FullName);
    }

    [Fact]
    public async Task ACollectionOnAServerIsAskedOnItsOwnPath()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "value": [] }"""));

        await provider.ListRepositoriesAsync(
            ServerAccount,
            Token,
            new HostRepositoryQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new Uri("https://tfs.example.com/tfs/DefaultCollection/_apis/git/repositories?api-version=7.1"),
            handler.Requests[0].RequestUri);
    }

    [Fact]
    public async Task TheLegacyOrganisationDomainWorksToo()
    {
        (AzureDevOpsProvider provider, ScriptedHandler handler) = Build();

        handler.Respond(_ => Json("""{ "value": [] }"""));

        await provider.ListRepositoriesAsync(
            LegacyAccount,
            Token,
            new HostRepositoryQuery(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new Uri("https://contoso.visualstudio.com/_apis/git/repositories?api-version=7.1"),
            handler.Requests[0].RequestUri);
    }

    // ---------------------------------------------------------------- deep links

    [Theory]
    [InlineData(
        "https://dev.azure.com/contoso/Platform/_git/service",
        "https://dev.azure.com/contoso/Platform/_git/service/commit/abc123")]
    [InlineData(
        "https://contoso@dev.azure.com/contoso/Platform/_git/service",
        "https://dev.azure.com/contoso/Platform/_git/service/commit/abc123")]
    [InlineData(
        "git@ssh.dev.azure.com:v3/contoso/Platform/service",
        "https://dev.azure.com/contoso/Platform/_git/service/commit/abc123")]
    [InlineData(
        "https://contoso.visualstudio.com/Platform/_git/service",
        "https://contoso.visualstudio.com/Platform/_git/service/commit/abc123")]
    [InlineData(
        "git@vs-ssh.visualstudio.com:v3/contoso/Platform/service",
        "https://contoso.visualstudio.com/Platform/_git/service/commit/abc123")]
    [InlineData(
        "https://tfs.example.com/tfs/DefaultCollection/Platform/_git/service",
        "https://tfs.example.com/tfs/DefaultCollection/Platform/_git/service/commit/abc123")]
    public void ACommitLinkIsFoundFromEveryAddressShape(string remote, string expected)
    {
        (AzureDevOpsProvider provider, _) = Build();

        Assert.Equal(expected, provider.BuildCommitUrl(RemoteUrl.Parse(remote), "abc123"));
    }

    [Fact]
    public void ABranchLinkUsesAzuresOwnVersionParameter()
    {
        (AzureDevOpsProvider provider, _) = Build();

        Assert.Equal(
            "https://dev.azure.com/contoso/Platform/_git/service?version=GBfeature%2Fsome-work",
            provider.BuildBranchUrl(
                RemoteUrl.Parse("https://dev.azure.com/contoso/Platform/_git/service"),
                "feature/some-work"));
    }

    [Fact]
    public void AFileLinkSaysWhetherItWasGivenACommitOrABranch()
    {
        (AzureDevOpsProvider provider, _) = Build();
        RemoteUrl remote = RemoteUrl.Parse("https://dev.azure.com/contoso/Platform/_git/service");

        Assert.Equal(
            "https://dev.azure.com/contoso/Platform/_git/service?path=%2Fsrc%2Fapp.txt&version=GCabc1234&line=42",
            provider.BuildFileUrl(remote, "abc1234", "src/app.txt", 42));

        Assert.Equal(
            "https://dev.azure.com/contoso/Platform/_git/service?path=%2Fsrc%2Fapp.txt&version=GBmain",
            provider.BuildFileUrl(remote, "main", "src/app.txt"));
    }

    [Theory]
    [InlineData("abc1234", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("main", false)]
    [InlineData("abc12", false)]
    [InlineData("release/2026", false)]
    public void AReferenceIsACommitWhenItLooksLikeOne(string reference, bool expected)
        => Assert.Equal(expected, AzureDevOpsProvider.LooksLikeSha(reference));

    [Fact]
    public void AnAddressWithNoRepositoryInItHasNoLinks()
    {
        (AzureDevOpsProvider provider, _) = Build();
        RemoteUrl remote = RemoteUrl.Parse("https://dev.azure.com/contoso");

        Assert.Null(provider.BuildCommitUrl(remote, "abc123"));
        Assert.Null(provider.BuildBranchUrl(remote, "main"));
        Assert.Null(provider.BuildFileUrl(remote, "main", "README.md"));
    }
}
