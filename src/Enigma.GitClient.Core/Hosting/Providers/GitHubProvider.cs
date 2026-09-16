using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Hosting.Providers;

/// <summary>
/// GitHub, and GitHub Enterprise Server, over the REST API.
/// </summary>
/// <remarks>
/// <para>
/// Only three things are ever asked of GitHub: who the token belongs to, which repositories it can
/// reach, and — with no request at all — where a commit, a branch or a file lives in a browser. No
/// issue or pull-request scope is requested, because this product does not have them.
/// </para>
/// <para>
/// The API base is derived from the instance root the account carries: <c>github.com</c> answers on
/// <c>api.github.com</c>, and every Enterprise Server answers on <c>&lt;host&gt;/api/v3</c>.
/// </para>
/// </remarks>
public sealed class GitHubProvider : IRepositoryHostProvider
{
    /// <summary>The API version this client is written against and pins.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>The media type GitHub wants.</summary>
    public const string MediaType = "application/vnd.github+json";

    private readonly IHttpClientFactory _clients;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="clients">Supplies the shared hosting client.</param>
    public GitHubProvider(IHttpClientFactory clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
    }

    /// <inheritdoc />
    public HostKind Kind => HostKind.GitHub;

    /// <inheritdoc />
    public string DisplayName => "GitHub";

    /// <inheritdoc />
    public Uri DefaultBaseUri { get; } = new("https://github.com");

    /// <inheritdoc />
    public string TokenScopeHint =>
        "A personal access token with the 'repo' scope (or, for a fine-grained token, read access to "
        + "Contents and Metadata). No issue or pull-request scope is ever requested.";

    /// <inheritdoc />
    public bool MatchesRemote(RemoteUrl remote) => WellKnownHosts.Detect(remote) == HostKind.GitHub;

    /// <summary>
    /// Works out where an instance's API lives.
    /// </summary>
    /// <param name="baseUri">The instance's root.</param>
    /// <returns>The API base, with a trailing slash.</returns>
    public static Uri ApiBase(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        // github.com is the one instance whose API is on another host; every Enterprise Server
        // serves it from a path on its own.
        if (baseUri.Host.Equals(WellKnownHosts.GitHub, StringComparison.OrdinalIgnoreCase)
            || baseUri.Host.Equals("www." + WellKnownHosts.GitHub, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://api.github.com/");
        }

        if (baseUri.Host.Equals("api." + WellKnownHosts.GitHub, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("https://api.github.com/");
        }

        string root = baseUri.GetLeftPart(UriPartial.Authority);

        return new Uri(root + "/api/v3/");
    }

    /// <inheritdoc />
    public async Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(token);

        using HttpResponseMessage response = await SendAsync(
            new Uri(ApiBase(account.BaseUri), "user"),
            token,
            cancellationToken).ConfigureAwait(false);

        using JsonDocument document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        string login = Text(root, "login") ?? account.UserName;

        return new HostIdentity(
            login,
            Text(root, "name") is { Length: > 0 } name ? name : login,
            Uri.TryCreate(Text(root, "avatar_url"), UriKind.Absolute, out Uri? avatar) ? avatar : null);
    }

    /// <inheritdoc />
    public async Task<HostRepositoryPage> ListRepositoriesAsync(
        HostAccount account,
        SecretString token,
        HostRepositoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(query);

        Uri address = query.Cursor is { Length: > 0 } cursor
            ? new Uri(cursor, UriKind.Absolute)
            : BuildListingUri(account, query);

        using HttpResponseMessage response = await SendAsync(address, token, cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        List<HostRepository> repositories = [];

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                repositories.Add(Map(element));
            }
        }

        return new HostRepositoryPage(repositories, NextLink(response));
    }

    /// <inheritdoc />
    public string? BuildCommitUrl(RemoteUrl remote, string sha)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(sha)
            ? $"{root}/commit/{Uri.EscapeDataString(sha)}"
            : null;

    /// <inheritdoc />
    public string? BuildBranchUrl(RemoteUrl remote, string branch)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(branch)
            ? $"{root}/tree/{EscapePath(branch)}"
            : null;

    /// <inheritdoc />
    public string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null)
    {
        if (Web(remote) is not { } root || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string anchor = line is { } number and > 0
            ? "#L" + number.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        return $"{root}/blob/{EscapePath(reference)}/{EscapePath(path)}{anchor}";
    }

    /// <summary>
    /// Builds the browser root of a repository from its remote address.
    /// </summary>
    /// <param name="remote">The remote address.</param>
    /// <returns>The root, or <see langword="null"/> when the address names no repository.</returns>
    /// <remarks>
    /// Derived from the address rather than from anything remembered, so a fork, a rename or a
    /// transfer resolves to wherever the remote now points.
    /// </remarks>
    private static string? Web(RemoteUrl remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (remote.Segments.Count < 2)
        {
            return null;
        }

        string host = remote.Host;

        // ssh.github.com is the SSH endpoint of github.com, not a place with web pages on it.
        if (host.Equals("ssh." + WellKnownHosts.GitHub, StringComparison.OrdinalIgnoreCase)
            || host.Equals("www." + WellKnownHosts.GitHub, StringComparison.OrdinalIgnoreCase))
        {
            host = WellKnownHosts.GitHub;
        }

        string owner = remote.Segments[^2];
        string name = remote.Segments[^1];

        return $"https://{host}/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
    }

    /// <summary>
    /// Escapes a path for a URL while leaving its slashes as slashes.
    /// </summary>
    private static string EscapePath(string path)
    {
        string[] segments = path.Split('/');

        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }

        return string.Join('/', segments);
    }

    private static Uri BuildListingUri(HostAccount account, HostRepositoryQuery query)
    {
        int size = Math.Clamp(query.PageSize, 1, 100);

        string visibility = query.Visibility switch
        {
            HostVisibility.Public => "public",
            HostVisibility.Private => "private",
            _ => "all",
        };

        // Sorted by push date: the repository someone wants to clone is almost always one they have
        // touched recently, and it saves paging through an alphabet of archived forks.
        string path = "user/repos"
            + $"?per_page={size.ToString(CultureInfo.InvariantCulture)}"
            + "&sort=pushed"
            + "&affiliation=owner,collaborator,organization_member"
            + $"&visibility={visibility}";

        return new Uri(ApiBase(account.BaseUri), path);
    }

    private static HostRepository Map(JsonElement element)
    {
        string fullName = Text(element, "full_name") ?? string.Empty;
        string name = Text(element, "name") ?? fullName;

        return new HostRepository(
            fullName,
            name,
            Text(element, "description"),
            Text(element, "default_branch") ?? "main",
            Text(element, "clone_url") ?? string.Empty,
            Text(element, "ssh_url"),
            Text(element, "html_url") ?? string.Empty,
            element.TryGetProperty("private", out JsonElement isPrivate) && isPrivate.ValueKind == JsonValueKind.True,
            Moment(element, "pushed_at"));
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Moment(JsonElement element, string name)
        => DateTimeOffset.TryParse(
            Text(element, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset moment)
            ? moment
            : null;

    /// <summary>
    /// Reads the <c>Link</c> header's <c>rel="next"</c>, which is how GitHub pages.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The next page's address, or <see langword="null"/> at the end.</returns>
    public static string? NextLink(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string header in values)
        {
            foreach (string part in header.Split(','))
            {
                string candidate = part.Trim();

                int open = candidate.IndexOf('<', StringComparison.Ordinal);
                int close = candidate.IndexOf('>', StringComparison.Ordinal);

                if (open < 0 || close <= open || !candidate.Contains("rel=\"next\"", StringComparison.Ordinal))
                {
                    continue;
                }

                return candidate[(open + 1)..close];
            }
        }

        return null;
    }

    /// <summary>
    /// Reads GitHub's rate-limit headers, which is what turns a bare 403 into a sentence with a time
    /// in it.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>When the limit lifts, or <see langword="null"/> when this is not a rate limit.</returns>
    public static DateTimeOffset? RateLimitReset(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remaining))
        {
            return null;
        }

        foreach (string value in remaining)
        {
            if (!int.TryParse(value, CultureInfo.InvariantCulture, out int left) || left > 0)
            {
                return null;
            }
        }

        if (!response.Headers.TryGetValues("x-ratelimit-reset", out IEnumerable<string>? reset))
        {
            return null;
        }

        foreach (string value in reset)
        {
            if (long.TryParse(value, CultureInfo.InvariantCulture, out long seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri address,
        SecretString token,
        CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(HostHttp.ClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw HostResponse.Classify(response, "GitHub", RateLimitReset(response));
        }

        try
        {
            return await JsonDocument
                .ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HostRequestException(
                "GitHub answered with something this client could not read. Check that the instance URL points at a GitHub API.",
                exception);
        }
    }
}
