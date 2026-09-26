using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Hosting.Providers;

/// <summary>
/// GitLab, public or self-hosted, over the v4 REST API.
/// </summary>
/// <remarks>
/// <para>
/// GitLab calls a repository a project and nests them in groups, so a path can be
/// <c>group/subgroup/project</c>. Nothing here flattens that: the full path is what the API returns
/// and what the web address uses.
/// </para>
/// <para>
/// A self-hosted instance is the ordinary case rather than the exception — the API always lives at
/// <c>&lt;instance&gt;/api/v4</c>, whatever the host is called.
/// </para>
/// </remarks>
public sealed class GitLabProvider : IRepositoryHostProvider
{
    /// <summary>The header GitLab authenticates a personal access token with.</summary>
    public const string TokenHeader = "PRIVATE-TOKEN";

    private readonly IHttpClientFactory _clients;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="clients">Supplies the shared hosting client.</param>
    public GitLabProvider(IHttpClientFactory clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
    }

    /// <inheritdoc />
    public HostKind Kind => HostKind.GitLab;

    /// <inheritdoc />
    public string DisplayName => "GitLab";

    /// <inheritdoc />
    public Uri DefaultBaseUri { get; } = new("https://gitlab.com");

    /// <inheritdoc />
    public string TokenScopeHint =>
        "A personal access token with the 'read_api' and 'read_repository' scopes. No issue or "
        + "merge-request scope is ever requested.";

    /// <inheritdoc />
    public bool MatchesRemote(RemoteUrl remote) => WellKnownHosts.Detect(remote) == HostKind.GitLab;

    /// <summary>
    /// Works out where an instance's API lives.
    /// </summary>
    /// <param name="baseUri">The instance's root.</param>
    /// <returns>The API base, with a trailing slash.</returns>
    public static Uri ApiBase(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        string root = baseUri.GetLeftPart(UriPartial.Authority);

        return new Uri(root + "/api/v4/");
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
        string login = JsonHelp.Text(root, "username") ?? account.UserName;

        return new HostIdentity(
            login,
            JsonHelp.Text(root, "name") is { Length: > 0 } name ? name : login,
            Uri.TryCreate(JsonHelp.Text(root, "avatar_url"), UriKind.Absolute, out Uri? avatar) ? avatar : null);
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

        using HttpResponseMessage response = await SendAsync(
            BuildListingUri(account, query),
            token,
            cancellationToken).ConfigureAwait(false);

        using JsonDocument document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        List<HostRepository> repositories = [];

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                repositories.Add(Map(element));
            }
        }

        return new HostRepositoryPage(repositories, NextPage(response));
    }

    /// <inheritdoc />
    public string? BuildCommitUrl(RemoteUrl remote, string sha)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(sha)
            ? $"{root}/-/commit/{Uri.EscapeDataString(sha)}"
            : null;

    /// <inheritdoc />
    public string? BuildBranchUrl(RemoteUrl remote, string branch)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(branch)
            ? $"{root}/-/tree/{JsonHelp.EscapePath(branch)}"
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

        return $"{root}/-/blob/{JsonHelp.EscapePath(reference)}/{JsonHelp.EscapePath(path)}{anchor}";
    }

    /// <summary>
    /// Reads the <c>X-Next-Page</c> header, which is how GitLab pages.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The next page's number, or <see langword="null"/> at the end.</returns>
    public static string? NextPage(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("X-Next-Page", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string value in values)
        {
            // The header is present and empty on the last page, which is GitLab's way of saying so.
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the browser root of a project from its remote address.
    /// </summary>
    private static string? Web(RemoteUrl remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (remote.Segments.Count < 1 || remote.Path.Length == 0)
        {
            return null;
        }

        string host = remote.Host;

        if (host.StartsWith("ssh.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("altssh.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[(host.IndexOf('.', StringComparison.Ordinal) + 1)..];
        }

        // The whole path, subgroups included: gitlab.com/group/subgroup/project is one project.
        return $"https://{host}/{JsonHelp.EscapePath(remote.Path)}";
    }

    private static Uri BuildListingUri(HostAccount account, HostRepositoryQuery query)
    {
        int size = Math.Clamp(query.PageSize, 1, 100);

        string path = "projects"
            + "?membership=true"
            + $"&per_page={size.ToString(CultureInfo.InvariantCulture)}"
            + "&order_by=last_activity_at"
            + "&simple=true";

        if (query.Visibility != HostVisibility.All)
        {
            path += query.Visibility == HostVisibility.Public ? "&visibility=public" : "&visibility=private";
        }

        if (query.Search is { Length: > 0 } search)
        {
            // GitLab's listing does take a search term, so it is passed on rather than filtered here.
            path += "&search=" + Uri.EscapeDataString(search);
        }

        if (query.Cursor is { Length: > 0 } cursor)
        {
            path += "&page=" + Uri.EscapeDataString(cursor);
        }

        return new Uri(ApiBase(account.BaseUri), path);
    }

    private static HostRepository Map(JsonElement element)
    {
        string fullName = JsonHelp.Text(element, "path_with_namespace") ?? string.Empty;
        string name = JsonHelp.Text(element, "path") ?? JsonHelp.Text(element, "name") ?? fullName;
        string visibility = JsonHelp.Text(element, "visibility") ?? "private";

        return new HostRepository(
            fullName,
            name,
            JsonHelp.Text(element, "description"),
            JsonHelp.Text(element, "default_branch") ?? "main",
            JsonHelp.Text(element, "http_url_to_repo") ?? string.Empty,
            JsonHelp.Text(element, "ssh_url_to_repo"),
            JsonHelp.Text(element, "web_url") ?? string.Empty,
            !visibility.Equals("public", StringComparison.OrdinalIgnoreCase),
            JsonHelp.Moment(element, "last_activity_at"));
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri address,
        SecretString token,
        CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(HostHttp.ClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, address);
        request.Headers.TryAddWithoutValidation(TokenHeader, token.Reveal());

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw HostResponse.Classify(response, "GitLab", RateLimitReset(response));
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
                "GitLab answered with something this client could not read. Check that the instance URL points at a GitLab instance.",
                exception);
        }
    }

    /// <summary>
    /// Reads GitLab's rate-limit headers.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>When the limit lifts, or <see langword="null"/> when this is not a rate limit.</returns>
    public static DateTimeOffset? RateLimitReset(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("RateLimit-Remaining", out IEnumerable<string>? remaining))
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

        if (!response.Headers.TryGetValues("RateLimit-Reset", out IEnumerable<string>? reset))
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
}

/// <summary>
/// The two things every provider does to a JSON answer, in one place.
/// </summary>
internal static class JsonHelp
{
    /// <summary>
    /// Reads a string property, or <see langword="null"/> when it is absent or not a string.
    /// </summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The value.</returns>
    public static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads a timestamp property.
    /// </summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The value, or <see langword="null"/> when it is absent or unparseable.</returns>
    public static DateTimeOffset? Moment(JsonElement element, string name)
        => DateTimeOffset.TryParse(
            Text(element, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset moment)
            ? moment
            : null;

    /// <summary>
    /// Escapes a path for a URL while leaving its slashes as slashes.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The escaped path.</returns>
    public static string EscapePath(string path)
    {
        string[] segments = path.Split('/');

        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }

        return string.Join('/', segments);
    }
}
