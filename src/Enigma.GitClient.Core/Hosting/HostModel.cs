using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// The repository hosts this client can talk to.
/// </summary>
public enum HostKind
{
    /// <summary>Not a host this client knows.</summary>
    Unknown,

    /// <summary>GitHub, including GitHub Enterprise Server.</summary>
    GitHub,

    /// <summary>GitLab, including a self-hosted instance.</summary>
    GitLab,

    /// <summary>Azure DevOps, including Azure DevOps Server.</summary>
    AzureDevOps,
}

/// <summary>
/// The host names this client recognises without being told.
/// </summary>
/// <remarks>
/// Only the public instances can be recognised by name. A self-hosted GitLab, a GitHub Enterprise
/// Server or an Azure DevOps Server is any host name at all, so those are recognised through the
/// account the user added for them — which is exactly what <see cref="HostProviderRegistry"/> does
/// before falling back to this table.
/// </remarks>
public static class WellKnownHosts
{
    /// <summary>GitHub's public instance.</summary>
    public const string GitHub = "github.com";

    /// <summary>GitLab's public instance.</summary>
    public const string GitLab = "gitlab.com";

    /// <summary>Azure DevOps' current public instance.</summary>
    public const string AzureDevOps = "dev.azure.com";

    /// <summary>Azure DevOps' legacy per-organisation domain.</summary>
    public const string AzureDevOpsLegacy = "visualstudio.com";

    /// <summary>
    /// Works out which host an address points at.
    /// </summary>
    /// <param name="remote">The parsed remote address.</param>
    /// <returns>The host kind, or <see cref="HostKind.Unknown"/>.</returns>
    public static HostKind Detect(RemoteUrl remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (remote.IsHost(GitHub) || remote.IsHost("ssh." + GitHub) || remote.IsHost("www." + GitHub))
        {
            return HostKind.GitHub;
        }

        if (remote.IsHost(GitLab) || remote.IsHost("ssh." + GitLab) || remote.IsHost("altssh." + GitLab))
        {
            return HostKind.GitLab;
        }

        // dev.azure.com, ssh.dev.azure.com, vs-ssh.visualstudio.com and <org>.visualstudio.com are
        // all the same product wearing four names.
        if (remote.IsUnder(AzureDevOps) || remote.IsUnder(AzureDevOpsLegacy))
        {
            return HostKind.AzureDevOps;
        }

        return HostKind.Unknown;
    }

    /// <summary>
    /// Works out which host an address points at.
    /// </summary>
    /// <param name="remoteUrl">The remote address, in any form git accepts.</param>
    /// <returns>The host kind, or <see cref="HostKind.Unknown"/> when it is not a known host.</returns>
    public static HostKind Detect(string? remoteUrl)
        => RemoteUrl.TryParse(remoteUrl, out RemoteUrl? remote) && remote is not null
            ? Detect(remote)
            : HostKind.Unknown;
}

/// <summary>
/// An account on a repository host, as the client remembers it.
/// </summary>
/// <remarks>
/// It deliberately holds no token. The secret lives in <see cref="Security.ITokenStore"/> under
/// <see cref="TokenKey"/>, so an account can be logged, serialised or shown in a dialog without any
/// risk of taking the credential with it.
/// </remarks>
public sealed record HostAccount
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="id">The account's stable identifier.</param>
    /// <param name="kind">Which host it is on.</param>
    /// <param name="baseUri">The instance's root, such as <c>https://github.com</c>.</param>
    /// <param name="userName">The account's login on the host.</param>
    /// <param name="displayName">What to call it in the interface.</param>
    [JsonConstructor]
    public HostAccount(string id, HostKind kind, Uri baseUri, string userName, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(baseUri);

        Id = id;
        Kind = kind;
        BaseUri = baseUri;
        UserName = userName ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? baseUri.Host : displayName;
    }

    /// <summary>Gets the account's stable identifier, which is also its token's key.</summary>
    public string Id { get; }

    /// <summary>Gets which host the account is on.</summary>
    public HostKind Kind { get; }

    /// <summary>
    /// Gets the instance's root — <c>https://github.com</c>, <c>https://gitlab.example.com</c>,
    /// <c>https://dev.azure.com/contoso</c>. Each provider derives its own API base from it.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>Gets the account's login on the host.</summary>
    public string UserName { get; }

    /// <summary>Gets what to call the account in the interface.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the host name of <see cref="BaseUri"/>, lower-cased.</summary>
    [JsonIgnore]
    public string Host => BaseUri.Host.ToLowerInvariant();

    /// <summary>Gets the key this account's token is stored under.</summary>
    [JsonIgnore]
    public string TokenKey => "host:" + Id;

    /// <summary>
    /// Builds a new account with a fresh identifier.
    /// </summary>
    /// <param name="kind">Which host it is on.</param>
    /// <param name="baseUri">The instance's root.</param>
    /// <param name="userName">The account's login on the host.</param>
    /// <param name="displayName">What to call it, or empty for the host name.</param>
    /// <returns>The account.</returns>
    public static HostAccount Create(HostKind kind, Uri baseUri, string userName, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        return new HostAccount(
            Guid.NewGuid().ToString("N"),
            kind,
            baseUri,
            userName,
            displayName ?? string.Empty);
    }

    /// <summary>
    /// Answers whether a remote address belongs to this account's instance.
    /// </summary>
    /// <param name="remote">The parsed remote address.</param>
    /// <returns><see langword="true"/> when the remote is on this instance's host.</returns>
    public bool Owns(RemoteUrl remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (remote.IsHost(Host))
        {
            return true;
        }

        // ssh.github.com, vs-ssh.visualstudio.com and friends: the SSH endpoint of an instance is a
        // sibling name, so a suffix match on the registrable part is what recognises it.
        string bare = Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? Host[4..] : Host;

        return remote.IsUnder(bare);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} {DisplayName} ({Host})";
}

/// <summary>
/// Who a token belongs to, as the host reports it.
/// </summary>
/// <param name="UserName">The login.</param>
/// <param name="DisplayName">The full name, or the login when the host has none.</param>
/// <param name="AvatarUrl">Where the account's picture is, if the host gave one.</param>
public sealed record HostIdentity(string UserName, string DisplayName, Uri? AvatarUrl = null);

/// <summary>
/// Which repositories to list.
/// </summary>
/// <param name="Search">Free text to filter on, or <see langword="null"/> for everything.</param>
/// <param name="Visibility">Which visibilities to include.</param>
/// <param name="PageSize">How many to ask the host for at a time.</param>
/// <param name="Cursor">
/// Where to carry on from, taken from a previous page's <see cref="HostRepositoryPage.NextCursor"/>;
/// <see langword="null"/> for the first page.
/// </param>
public sealed record HostRepositoryQuery(
    string? Search = null,
    HostVisibility Visibility = HostVisibility.All,
    int PageSize = 100,
    string? Cursor = null);

/// <summary>
/// Which repositories a listing includes.
/// </summary>
public enum HostVisibility
{
    /// <summary>Everything the account can see.</summary>
    All,

    /// <summary>Only the public ones.</summary>
    Public,

    /// <summary>Only the private ones.</summary>
    Private,
}

/// <summary>
/// One page of a repository listing.
/// </summary>
/// <param name="Repositories">The repositories on this page.</param>
/// <param name="NextCursor">Where the next page starts, or <see langword="null"/> at the end.</param>
public sealed record HostRepositoryPage(IReadOnlyList<HostRepository> Repositories, string? NextCursor = null)
{
    /// <summary>An empty page.</summary>
    public static readonly HostRepositoryPage Empty = new([]);

    /// <summary>Gets a value indicating whether there is another page to ask for.</summary>
    public bool HasMore => NextCursor is { Length: > 0 };
}

/// <summary>
/// A repository as a host describes it.
/// </summary>
/// <param name="FullName">The path that identifies it on the host, such as <c>owner/repo</c>.</param>
/// <param name="Name">Its own name, without the owner.</param>
/// <param name="Description">What the host says it is, if anything.</param>
/// <param name="DefaultBranch">The branch a clone lands on.</param>
/// <param name="CloneUrl">The HTTPS address to clone from.</param>
/// <param name="SshCloneUrl">The SSH address to clone from, when the host offers one.</param>
/// <param name="WebUrl">Where it lives in a browser.</param>
/// <param name="IsPrivate">Whether it is private.</param>
/// <param name="LastPushed">When it was last pushed to, if the host said.</param>
public sealed record HostRepository(
    string FullName,
    string Name,
    string? Description,
    string DefaultBranch,
    string CloneUrl,
    string? SshCloneUrl,
    string WebUrl,
    bool IsPrivate,
    DateTimeOffset? LastPushed = null)
{
    /// <summary>Gets the owner, which is everything before the last slash of the full name.</summary>
    public string Owner
    {
        get
        {
            int slash = FullName.LastIndexOf('/');
            return slash < 0 ? string.Empty : FullName[..slash];
        }
    }

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>
/// Something a repository host refused or could not do.
/// </summary>
public class HostException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong, in a sentence a user can read.</param>
    public HostException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong, in a sentence a user can read.</param>
    /// <param name="innerException">What caused it.</param>
    public HostException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message. Present for the exception contract; prefer the
    /// constructors that say what happened.
    /// </summary>
    public HostException()
    {
    }
}

/// <summary>
/// The host rejected the token: it is wrong, expired, or lacks the scope the call needed.
/// </summary>
public sealed class HostAuthenticationException : HostException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public HostAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public HostAuthenticationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message.
    /// </summary>
    public HostAuthenticationException()
        : base("The host rejected this token.")
    {
    }
}

/// <summary>
/// The host is rate-limiting this account.
/// </summary>
/// <remarks>
/// Kept apart from an ordinary refusal because it is the one failure with a cure the user can see:
/// waiting. The reset time is carried so the message can say when, rather than "try again later".
/// </remarks>
public sealed class HostRateLimitException : HostException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="resetsAt">When the limit lifts, if the host said.</param>
    public HostRateLimitException(string message, DateTimeOffset? resetsAt = null)
        : base(message)
        => ResetsAt = resetsAt;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public HostRateLimitException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message.
    /// </summary>
    public HostRateLimitException()
        : base("The host is rate-limiting this account.")
    {
    }

    /// <summary>Gets when the limit lifts, if the host said.</summary>
    public DateTimeOffset? ResetsAt { get; }

    /// <summary>
    /// Gets how long there is to wait, or <see langword="null"/> when the host did not say.
    /// </summary>
    public TimeSpan? RetryAfter
        => ResetsAt is { } reset && reset > DateTimeOffset.UtcNow ? reset - DateTimeOffset.UtcNow : null;
}

/// <summary>
/// The host answered, and the answer was a failure this client does not model more precisely.
/// </summary>
public sealed class HostRequestException : HostException
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="statusCode">What the host answered with.</param>
    public HostRequestException(string message, HttpStatusCode statusCode)
        : base(message)
        => StatusCode = statusCode;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public HostRequestException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with no message.
    /// </summary>
    public HostRequestException()
        : base("The host could not answer.")
    {
    }

    /// <summary>Gets the status the host answered with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the status as a number, for a message that shows it.
    /// </summary>
    public string StatusText => ((int)StatusCode).ToString(CultureInfo.InvariantCulture);
}
