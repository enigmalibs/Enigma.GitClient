using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// The connected accounts and the tokens behind them, against a throwaway configuration directory.
/// </summary>
public sealed class HostAccountServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileTokenStore _tokens;
    private readonly HostAccountService _accounts;

    public HostAccountServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-accounts-" + Guid.NewGuid().ToString("N"));

        AppPaths paths = new(_root);
        _tokens = new FileTokenStore(paths);
        _accounts = new HostAccountService(paths, _tokens);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static HostAccount Account(HostKind kind = HostKind.GitHub, string uri = "https://github.com")
        => HostAccount.Create(kind, new Uri(uri), "someone", "Someone on GitHub");

    [Fact]
    public async Task AddingAnAccountStoresItAndItsToken()
    {
        HostAccount account = Account();

        await _accounts.AddAsync(account, new SecretString("ghp_secret"), TestContext.Current.CancellationToken);

        HostAccount stored = Assert.Single(await _accounts.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal(account.Id, stored.Id);
        Assert.Equal(HostKind.GitHub, stored.Kind);
        Assert.Equal("Someone on GitHub", stored.DisplayName);
        Assert.Equal("someone", stored.UserName);
        Assert.Equal(new Uri("https://github.com"), stored.BaseUri);

        SecretString? token = await _accounts.GetTokenAsync(stored, TestContext.Current.CancellationToken);

        Assert.Equal("ghp_secret", token!.Reveal());
    }

    [Fact]
    public async Task TheAccountListNeverHoldsTheToken()
    {
        await _accounts.AddAsync(Account(), new SecretString("ghp_secret"), TestContext.Current.CancellationToken);

        string contents = File.ReadAllText(Path.Combine(_root, HostAccountService.FileName));

        Assert.DoesNotContain("ghp_secret", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("token", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountsSurviveARestart()
    {
        await _accounts.AddAsync(Account(), new SecretString("one"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(
            Account(HostKind.GitLab, "https://gitlab.com"),
            new SecretString("two"),
            TestContext.Current.CancellationToken);

        AppPaths paths = new(_root);
        HostAccountService reopened = new(paths, new FileTokenStore(paths));

        IReadOnlyList<HostAccount> stored = await reopened.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, account => account.Kind == HostKind.GitHub);
        Assert.Contains(stored, account => account.Kind == HostKind.GitLab);
    }

    [Fact]
    public async Task UpdatingKeepsTheStoredTokenUnlessANewOneIsGiven()
    {
        HostAccount account = Account();
        await _accounts.AddAsync(account, new SecretString("first"), TestContext.Current.CancellationToken);

        HostAccount renamed = new(account.Id, account.Kind, account.BaseUri, account.UserName, "A better name");

        await _accounts.UpdateAsync(renamed, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("A better name", Assert.Single(await _accounts.GetAllAsync(TestContext.Current.CancellationToken)).DisplayName);
        Assert.Equal("first", (await _accounts.GetTokenAsync(renamed, TestContext.Current.CancellationToken))!.Reveal());

        await _accounts.UpdateAsync(renamed, new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.Equal("second", (await _accounts.GetTokenAsync(renamed, TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task AddingTheSameAccountTwiceReplacesItRatherThanDuplicatingIt()
    {
        HostAccount account = Account();

        await _accounts.AddAsync(account, new SecretString("first"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(account, new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.Single(await _accounts.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("second", (await _accounts.GetTokenAsync(account, TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task RemovingAnAccountTakesItsTokenWithIt()
    {
        HostAccount account = Account();
        await _accounts.AddAsync(account, new SecretString("ghp_secret"), TestContext.Current.CancellationToken);

        Assert.True(await _accounts.RemoveAsync(account.Id, TestContext.Current.CancellationToken));

        Assert.Empty(await _accounts.GetAllAsync(TestContext.Current.CancellationToken));

        // A live credential nothing points at any more is the thing this must never leave behind.
        Assert.Null(await _tokens.TryGetAsync(account.TokenKey, TestContext.Current.CancellationToken));
        Assert.Empty(await _tokens.ListKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovingAnAccountThatIsNotThereSaysSo()
        => Assert.False(await _accounts.RemoveAsync("nothing", TestContext.Current.CancellationToken));

    [Fact]
    public async Task RemovingOneAccountLeavesTheOthersToken()
    {
        HostAccount first = Account();
        HostAccount second = Account(HostKind.GitLab, "https://gitlab.com");

        await _accounts.AddAsync(first, new SecretString("one"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(second, new SecretString("two"), TestContext.Current.CancellationToken);

        await _accounts.RemoveAsync(first.Id, TestContext.Current.CancellationToken);

        Assert.Equal("two", (await _accounts.GetTokenAsync(second, TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task AnAccountWithNoTokenStoredReportsNone()
    {
        HostAccount account = Account();

        await _accounts.UpdateAsync(account, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await _accounts.GetTokenAsync(account, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAccountKeepsTheProfileItBelongsToAcrossARestart()
    {
        HostAccount account = HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), "someone", "Work", "work-profile");

        await _accounts.AddAsync(account, new SecretString("one"), TestContext.Current.CancellationToken);

        AppPaths paths = new(_root);
        HostAccount stored = Assert.Single(await new HostAccountService(paths, new FileTokenStore(paths))
            .GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("work-profile", stored.ProfileId);
        Assert.True(stored.BelongsTo("work-profile"));
        Assert.False(stored.BelongsTo("home-profile"));
        Assert.False(stored.IsUnassigned);

        string contents = File.ReadAllText(Path.Combine(_root, HostAccountService.FileName));
        Assert.Contains("\"version\": 2", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVersionOneFileReadsAsAccountsThatBelongToNoProfile()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, HostAccountService.FileName),
            """
            {
              "version": 1,
              "accounts": [
                {
                  "id": "0123456789abcdef",
                  "kind": 1,
                  "baseUri": "https://github.com",
                  "userName": "someone",
                  "displayName": "Someone on GitHub"
                }
              ]
            }
            """);

        HostAccount stored = Assert.Single(await _accounts.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("0123456789abcdef", stored.Id);
        Assert.Equal("Someone on GitHub", stored.DisplayName);
        Assert.Null(stored.ProfileId);
        Assert.True(stored.IsUnassigned);
        Assert.False(stored.BelongsTo(null));
    }

    [Fact]
    public async Task AssigningGivesOneAccountToAProfileAndKeepsItsToken()
    {
        HostAccount earlier = Account();
        HostAccount other = Account(HostKind.GitLab, "https://gitlab.com");

        await _accounts.AddAsync(earlier, new SecretString("one"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(other, new SecretString("two"), TestContext.Current.CancellationToken);

        Assert.True(await _accounts.AssignAsync(earlier.Id, "work-profile", TestContext.Current.CancellationToken));

        IReadOnlyList<HostAccount> stored = await _accounts.GetAllAsync(TestContext.Current.CancellationToken);

        HostAccount moved = Assert.Single(stored, account => account.Id == earlier.Id);
        Assert.Equal("work-profile", moved.ProfileId);
        Assert.True(Assert.Single(stored, account => account.Id == other.Id).IsUnassigned);

        // Same id, same key: the token did not have to move.
        Assert.Equal("one", (await _accounts.GetTokenAsync(moved, TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task AssigningAnAccountThatIsNotThereSaysSo()
        => Assert.False(await _accounts.AssignAsync("nothing", "work-profile", TestContext.Current.CancellationToken));

    [Fact]
    public async Task RemovingAProfilesAccountsTakesOnlyThoseAndTheirTokens()
    {
        HostAccount work = HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), "a", "Work GitHub", "work-profile");
        HostAccount workLab = HostAccount.Create(HostKind.GitLab, new Uri("https://gitlab.com"), "a", "Work GitLab", "work-profile");
        HostAccount home = HostAccount.Create(HostKind.GitHub, new Uri("https://github.com"), "b", "Home GitHub", "home-profile");
        HostAccount earlier = Account();

        await _accounts.AddAsync(work, new SecretString("w1"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(workLab, new SecretString("w2"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(home, new SecretString("h"), TestContext.Current.CancellationToken);
        await _accounts.AddAsync(earlier, new SecretString("e"), TestContext.Current.CancellationToken);

        Assert.Equal(2, await _accounts.RemoveForProfileAsync("work-profile", TestContext.Current.CancellationToken));

        IReadOnlyList<HostAccount> left = await _accounts.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { home.Id, earlier.Id }, left.Select(account => account.Id));
        Assert.Null(await _tokens.TryGetAsync(work.TokenKey, TestContext.Current.CancellationToken));
        Assert.Null(await _tokens.TryGetAsync(workLab.TokenKey, TestContext.Current.CancellationToken));
        Assert.Equal("h", (await _accounts.GetTokenAsync(home, TestContext.Current.CancellationToken))!.Reveal());

        Assert.Equal(0, await _accounts.RemoveForProfileAsync("work-profile", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GivingAnAccountToAnotherProfileKeepsEverythingElse()
    {
        HostAccount account = Account();
        HostAccount moved = account.ForProfile("work-profile");

        Assert.Equal(account.Id, moved.Id);
        Assert.Equal(account.TokenKey, moved.TokenKey);
        Assert.Equal(account.DisplayName, moved.DisplayName);
        Assert.Equal("work-profile", moved.ProfileId);

        // A blank profile is no profile.
        Assert.True(moved.ForProfile("  ").IsUnassigned);
    }

    [Fact]
    public async Task AnUnreadableAccountListIsAnEmptyOneRatherThanABrokenApplication()
    {
        await _accounts.AddAsync(Account(), new SecretString("one"), TestContext.Current.CancellationToken);

        File.WriteAllText(Path.Combine(_root, HostAccountService.FileName), "{ not json");

        Assert.Empty(await _accounts.GetAllAsync(TestContext.Current.CancellationToken));
    }
}
