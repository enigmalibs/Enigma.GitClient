using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Security;

/// <summary>
/// The token store, against a throwaway configuration directory.
/// </summary>
/// <remarks>
/// These are the tests that matter most in this feature: everything else here fails visibly, and a
/// token store that quietly writes a credential in the clear fails invisibly.
/// </remarks>
public sealed class TokenStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileTokenStore _store;

    public TokenStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-tokens-" + Guid.NewGuid().ToString("N"));
        _store = new FileTokenStore(new AppPaths(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string TokenFile => Path.Combine(_root, FileTokenStore.FileName);

    private string KeyFile => Path.Combine(_root, FileTokenStore.KeyFileName);

    // ---------------------------------------------------------------- the secret wrapper

    [Fact]
    public void ASecretNeverPrintsItself()
    {
        SecretString secret = new("ghp_averysecrettoken");

        Assert.Equal("***", secret.ToString());
        Assert.Equal("***", $"{secret}");
        Assert.Equal("***", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", secret));

        // And still hands the value over when it is actually asked for.
        Assert.Equal("ghp_averysecrettoken", secret.Reveal());
    }

    [Fact]
    public void ASecretsHashCodeIsNotAFingerprintOfIt()
    {
        // Same length, different value: a hash that told them apart would be a fingerprint an
        // attacker could compare against a guess.
        Assert.Equal(new SecretString("aaaaaa").GetHashCode(), new SecretString("bbbbbb").GetHashCode());
    }

    [Fact]
    public void TwoSecretsAreEqualWhenTheirValuesAre()
    {
        Assert.Equal(new SecretString("same"), new SecretString("same"));
        Assert.NotEqual(new SecretString("same"), new SecretString("other"));
        Assert.True(SecretString.Empty.IsEmpty);
        Assert.Equal(4, new SecretString("abcd").Length);
    }

    // ---------------------------------------------------------------- round trip

    [Fact]
    public async Task ASecretComesBackExactlyAsItWentIn()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_token-with-üñïçødé-and-#/?"), TestContext.Current.CancellationToken);

        SecretString? read = await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal("ghp_token-with-üñïçødé-and-#/?", read!.Reveal());
    }

    [Fact]
    public async Task AKeyThatWasNeverWrittenHasNothingBehindIt()
        => Assert.Null(await _store.TryGetAsync("host:missing", TestContext.Current.CancellationToken));

    [Fact]
    public async Task WritingTwiceKeepsTheSecondSecret()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        await _store.SetAsync("host:one", new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.Equal("second", (await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task SeveralSecretsLiveSideBySide()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        await _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.Equal("first", (await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken))!.Reveal());
        Assert.Equal("second", (await _store.TryGetAsync("host:two", TestContext.Current.CancellationToken))!.Reveal());

        IReadOnlyList<string> keys = await _store.ListKeysAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, keys.Count);
        Assert.Contains("host:one", keys);
        Assert.Contains("host:two", keys);
    }

    [Fact]
    public async Task DeletingRemovesTheSecretAndSaysWhetherThereWasOne()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        Assert.True(await _store.DeleteAsync("host:one", TestContext.Current.CancellationToken));
        Assert.Null(await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));
        Assert.False(await _store.DeleteAsync("host:one", TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- what reaches the disk

    [Fact]
    public async Task TheSecretIsNowhereInTheFile()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_averysecrettoken"), TestContext.Current.CancellationToken);

        string contents = File.ReadAllText(TokenFile);

        Assert.DoesNotContain("ghp_averysecrettoken", contents, StringComparison.Ordinal);
        Assert.Contains("host:one", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATamperedSecretFailsAuthenticationRatherThanDecryptingToRubbish()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_averysecrettoken"), TestContext.Current.CancellationToken);

        JsonNode document = JsonNode.Parse(File.ReadAllText(TokenFile))!;
        JsonNode entry = document["entries"]!["host:one"]!;

        byte[] cipher = Convert.FromBase64String(entry["cipher"]!.GetValue<string>());
        cipher[0] ^= 0xFF;
        entry["cipher"] = Convert.ToBase64String(cipher);

        File.WriteAllText(TokenFile, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecretCannotBeMovedToAnotherKey()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_averysecrettoken"), TestContext.Current.CancellationToken);

        JsonNode document = JsonNode.Parse(File.ReadAllText(TokenFile))!;
        JsonNode entry = document["entries"]!["host:one"]!;

        document["entries"]!["host:two"] = JsonNode.Parse(entry.ToJsonString());

        File.WriteAllText(TokenFile, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // The key is the associated data, so the copy is not a valid entry under its new name.
        await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:two", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnotherMachinesDataKeyCannotReadTheseSecrets()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_averysecrettoken"), TestContext.Current.CancellationToken);

        string other = Path.Combine(Path.GetTempPath(), "enigma-tokens-" + Guid.NewGuid().ToString("N"));

        try
        {
            FileTokenStore elsewhere = new(new AppPaths(other));
            await elsewhere.SetAsync("host:other", new SecretString("another"), TestContext.Current.CancellationToken);

            // The entries travel; the data key does not.
            File.Copy(TokenFile, Path.Combine(other, FileTokenStore.FileName), overwrite: true);

            await Assert.ThrowsAsync<TokenProtectionException>(
                () => elsewhere.TryGetAsync("host:one", TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(other))
            {
                Directory.Delete(other, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ACorruptFileSaysSoRatherThanLookingEmpty()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        File.WriteAllText(TokenFile, "{ this is not json");

        await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheFilesAreOnlyReadableByTheirOwner()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows protects the data key with DPAPI; there is no mode to assert.");

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            Assert.Equal(expected, File.GetUnixFileMode(KeyFile));
            Assert.Equal(expected, File.GetUnixFileMode(TokenFile));

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(_root));
        }
    }

    [Fact]
    public async Task TheDataKeyIsWrittenOnceAndReused()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        byte[] key = File.ReadAllBytes(KeyFile);

        await _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.Equal(key, File.ReadAllBytes(KeyFile));

        // And a store built afresh over the same directory reads what the first one wrote.
        FileTokenStore reopened = new(new AppPaths(_root));

        Assert.Equal("first", (await reopened.TryGetAsync("host:one", TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task EveryEntryGetsItsOwnNonce()
    {
        await _store.SetAsync("host:one", new SecretString("same value"), TestContext.Current.CancellationToken);
        await _store.SetAsync("host:two", new SecretString("same value"), TestContext.Current.CancellationToken);

        JsonNode document = JsonNode.Parse(File.ReadAllText(TokenFile))!;

        string first = document["entries"]!["host:one"]!["nonce"]!.GetValue<string>();
        string second = document["entries"]!["host:two"]!["nonce"]!.GetValue<string>();

        // Reusing a nonce under one key is the classic way to break AES-GCM outright.
        Assert.NotEqual(first, second);
        Assert.NotEqual(
            document["entries"]!["host:one"]!["cipher"]!.GetValue<string>(),
            document["entries"]!["host:two"]!["cipher"]!.GetValue<string>());
    }
}
