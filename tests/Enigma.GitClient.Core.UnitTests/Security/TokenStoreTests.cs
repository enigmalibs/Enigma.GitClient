using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
        _store = new FileTokenStore(new AppPaths(_root), new PlatformDataKeyProtector());
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

        TokenProtectionException failure = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));

        // One entry, not the store: replacing that secret fixes it.
        Assert.Equal(TokenProtectionReason.EntryUnreadable, failure.Reason);
        Assert.Equal("host:one", failure.Key);
        Assert.False(failure.IsStoreWide);
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
        TokenProtectionException failure = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:two", TestContext.Current.CancellationToken));

        Assert.Equal(TokenProtectionReason.EntryUnreadable, failure.Reason);
        Assert.Equal("host:two", failure.Key);
    }

    [Fact]
    public async Task AnotherMachinesDataKeyCannotReadTheseSecrets()
    {
        await _store.SetAsync("host:one", new SecretString("ghp_averysecrettoken"), TestContext.Current.CancellationToken);

        string other = Path.Combine(Path.GetTempPath(), "enigma-tokens-" + Guid.NewGuid().ToString("N"));

        try
        {
            FileTokenStore elsewhere = new(new AppPaths(other), new PlatformDataKeyProtector());
            await elsewhere.SetAsync("host:other", new SecretString("another"), TestContext.Current.CancellationToken);

            // The entries travel; the data key does not.
            File.Copy(TokenFile, Path.Combine(other, FileTokenStore.FileName), overwrite: true);

            TokenProtectionException failure = await Assert.ThrowsAsync<TokenProtectionException>(
                () => elsewhere.TryGetAsync("host:one", TestContext.Current.CancellationToken));

            // That store's own key opens fine; it is this entry that was sealed under another.
            Assert.Equal(TokenProtectionReason.EntryUnreadable, failure.Reason);
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

        TokenProtectionException failure = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));

        Assert.Equal(TokenProtectionReason.FileUnreadable, failure.Reason);
        Assert.True(failure.IsStoreWide);
        Assert.Equal("The stored tokens file is not readable. Remove it and sign in again.", failure.Message);
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
        FileTokenStore reopened = new(new AppPaths(_root), new PlatformDataKeyProtector());

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

    // ---------------------------------------------------------------- an unreadable store (BUG-3024)

    /// <summary>
    /// A protector that cannot open any key — what DPAPI does once a Windows password reset has taken the
    /// master key the data key was sealed with. Sealing still works, as it does for DPAPI.
    /// </summary>
    private sealed class RefusingProtector : IDataKeyProtector
    {
        public byte[] Protect(byte[] key) => key;

        public byte[] Unprotect(byte[] stored) => throw new CryptographicException("Key not valid for use in specified state.");
    }

    private FileTokenStore Refusing() => new(new AppPaths(_root), new RefusingProtector());

    [Fact]
    public async Task AKeyThatCannotBeOpened_MakesTheWholeStoreUnreadable_OnReadAndOnWrite()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        FileTokenStore store = Refusing();

        TokenProtectionException read = await Assert.ThrowsAsync<TokenProtectionException>(
            () => store.TryGetAsync("host:one", TestContext.Current.CancellationToken));
        TokenProtectionException write = await Assert.ThrowsAsync<TokenProtectionException>(
            () => store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken));

        Assert.Equal(TokenProtectionReason.KeyUnavailable, read.Reason);
        Assert.Equal(TokenProtectionReason.KeyUnavailable, write.Reason);
        Assert.True(read.IsStoreWide);
        Assert.Null(read.Key);
        Assert.IsType<CryptographicException>(read.InnerException);

        // The message the user was shown on 2026-10-23 is kept.
        Assert.StartsWith("The stored tokens belong to a different machine or user account", read.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeyOfTheWrongSize_MakesTheWholeStoreUnreadable()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        // On Linux the key file holds the key itself; seven bytes are no AES key. (On Windows DPAPI
        // refuses them first, with the same outcome.)
        File.WriteAllBytes(KeyFile, [1, 2, 3, 4, 5, 6, 7]);

        TokenProtectionException failure = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));

        Assert.Equal(TokenProtectionReason.KeyUnavailable, failure.Reason);
    }

    [Fact]
    public async Task AKeyFileDeletedWithSecretsLeft_IsNotSilentlyReplaced()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        File.Delete(KeyFile);

        TokenProtectionException read = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.TryGetAsync("host:one", TestContext.Current.CancellationToken));
        TokenProtectionException write = await Assert.ThrowsAsync<TokenProtectionException>(
            () => _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken));

        Assert.Equal(TokenProtectionReason.KeyUnavailable, read.Reason);
        Assert.Equal(TokenProtectionReason.KeyUnavailable, write.Reason);

        // A new key would have turned "host:one" into a damaged entry for good, without a word.
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public async Task ReplacingTheOnlySecret_WhenItsKeyIsGone_StartsAFreshKey()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        File.Delete(KeyFile);

        await _store.SetAsync("host:one", new SecretString("again"), TestContext.Current.CancellationToken);

        Assert.Equal("again", (await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public void TheExceptionsOwnConstructors_SayNoReason()
    {
        Assert.Equal(TokenProtectionReason.Unspecified, new TokenProtectionException().Reason);
        Assert.Equal(TokenProtectionReason.Unspecified, new TokenProtectionException("message").Reason);
        Assert.Equal(TokenProtectionReason.Unspecified, new TokenProtectionException("message", null).Reason);
        Assert.True(new TokenProtectionException().IsStoreWide);
        Assert.Equal("A stored secret could not be read.", new TokenProtectionException().Message);
    }

    // ---------------------------------------------------------------- the reset

    [Fact]
    public async Task ResetDeletesTheSecretsAndTheKey()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        await _store.ResetAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(TokenFile));
        Assert.False(File.Exists(KeyFile));
        Assert.Empty(await _store.ListKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetOfAnEmptyStore_DoesNothing()
    {
        await _store.ResetAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(TokenFile));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public async Task AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain()
    {
        await _store.SetAsync("host:one", new SecretString("lost"), TestContext.Current.CancellationToken);
        byte[] oldKey = File.ReadAllBytes(KeyFile);
        FileTokenStore refusing = Refusing();

        await refusing.ResetAsync(TestContext.Current.CancellationToken);
        await refusing.SetAsync("host:one", new SecretString("typed again"), TestContext.Current.CancellationToken);

        // A fresh key: the one the refusing store could not open is gone.
        Assert.NotEqual(oldKey, File.ReadAllBytes(KeyFile));
        Assert.Equal("typed again", (await _store.TryGetAsync("host:one", TestContext.Current.CancellationToken))!.Reveal());
    }

    [Fact]
    public async Task TheKeyAfterAReset_IsOnlyReadableByItsOwner()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        await _store.ResetAsync(TestContext.Current.CancellationToken);
        await _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken);

        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows protects the data key with DPAPI; there is no mode to assert.");

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(TokenFile));
        }
    }

    // ---------------------------------------------------------------- the check

    [Fact]
    public async Task Check_OfAHealthyStore_ListsItsKeys_AllReadable()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        await _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken);

        TokenStoreStatus status = await _store.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsReadable);
        Assert.Equal(2, status.Keys.Count);
        Assert.Empty(status.UnreadableKeys);
        Assert.True(status.CanRead("host:one"));
        Assert.False(status.CanRead("host:missing"));
        Assert.False(status.Holds("host:missing"));
    }

    [Fact]
    public async Task Check_OfAnEmptyStore_IsReadableAndEmpty()
    {
        TokenStoreStatus status = await _store.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsReadable);
        Assert.Empty(status.Keys);
    }

    [Fact]
    public async Task Check_OfAStoreWhoseKeyCannotBeOpened_SaysSo_WithoutThrowing()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);

        TokenStoreStatus status = await Refusing().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TokenProtectionReason.KeyUnavailable, status.Failure);
        Assert.False(status.IsReadable);
        Assert.True(status.Holds("host:one"));
        Assert.False(status.CanRead("host:one"));
    }

    [Fact]
    public async Task Check_OfAStoreWhoseKeyIsGone_SaysSo()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        File.Delete(KeyFile);

        TokenStoreStatus status = await _store.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TokenProtectionReason.KeyUnavailable, status.Failure);
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public async Task Check_OfAFileThatIsNotJson_SaysSo()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        File.WriteAllText(TokenFile, "{ this is not json");

        TokenStoreStatus status = await _store.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TokenProtectionReason.FileUnreadable, status.Failure);
        Assert.Empty(status.Keys);
    }

    [Fact]
    public async Task Check_NamesTheOneDamagedEntry_AmongGoodOnes()
    {
        await _store.SetAsync("host:one", new SecretString("first"), TestContext.Current.CancellationToken);
        await _store.SetAsync("host:two", new SecretString("second"), TestContext.Current.CancellationToken);

        JsonNode document = JsonNode.Parse(File.ReadAllText(TokenFile))!;
        JsonNode entry = document["entries"]!["host:two"]!;
        byte[] tag = Convert.FromBase64String(entry["tag"]!.GetValue<string>());
        tag[0] ^= 0xFF;
        entry["tag"] = Convert.ToBase64String(tag);
        File.WriteAllText(TokenFile, document.ToJsonString());

        TokenStoreStatus status = await _store.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsReadable);
        Assert.Equal(["host:two"], status.UnreadableKeys);
        Assert.True(status.CanRead("host:one"));
        Assert.False(status.CanRead("host:two"));
        Assert.True(status.Holds("host:two"));
    }
}
