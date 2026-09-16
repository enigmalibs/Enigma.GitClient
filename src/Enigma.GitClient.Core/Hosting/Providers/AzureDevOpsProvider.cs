using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Hosting.Providers;

/// <summary>
/// Azure DevOps — the current <c>dev.azure.com</c> form, the legacy <c>&lt;org&gt;.visualstudio.com</c>
/// one, and Azure DevOps Server with a collection path — over REST 7.1.
/// </summary>
/// <remarks>
/// <para>
/// An account here is an organisation (or a Server collection) rather than a whole host, because
/// that is the level a token is issued at and the level repositories are listed from. The instance
/// root therefore carries a path: <c>https://dev.azure.com/contoso</c>.
/// </para>
/// <para>
/// A personal access token is sent as HTTP Basic with an empty user name, which is how Azure DevOps
/// documents it.
/// </para>
/// </remarks>
public sealed class AzureDevOpsProvider : IRepositoryHostProvider
{
    /// <summary>The API version this client is written against.</summary>
    public const string ApiVersion = "7.1";

    private readonly IHttpClientFactory _clients;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="clients">Supplies the shared hosting client.</param>
    public AzureDevOpsProvider(IHttpClientFactory clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
    }

    /// <inheritdoc />
    public HostKind Kind => HostKind.AzureDevOps;

    /// <inheritdoc />
    public string DisplayName => "Azure DevOps";

    /// <inheritdoc />
    public Uri DefaultBaseUri { get; } = new("https://dev.azure.com");

    /// <inheritdoc />
    public string TokenScopeHint =>
        "A personal access token with 'Code: Read', and the organisation in the instance URL "
        + "(https://dev.azure.com/your-organisation). No work-item or pull-request scope is ever requested.";

    /// <inheritdoc />
    public bool MatchesRemote(RemoteUrl remote) => WellKnownHosts.Detect(remote) == HostKind.AzureDevOps;

    /// <summary>
    /// Works out where an instance's API lives.
    /// </summary>
    /// <param name="baseUri">The instance's root, organisation or collection path included.</param>
    /// <returns>The API base, with a trailing slash.</returns>
    public static Uri ApiBase(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        string root = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

        return new Uri(root + "/_apis/");
    }

    /// <inheritdoc />
    public async Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(token);

        // connectionData is the one identity call that works the same on the cloud and on Server,
        // and it needs no scope beyond the one the token already has.
        using HttpResponseMessage response = await SendAsync(
            new Uri(ApiBase(account.BaseUri), "connectionData?api-version=" + ApiVersion),
            token,
            cancellationToken).ConfigureAwait(false);

        using JsonDocument document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("authenticatedUser", out JsonElement user))
        {
            return new HostIdentity(account.UserName, account.DisplayName);
        }

        string display = JsonHelp.Text(user, "providerDisplayName") ?? account.UserName;
        string login = AccountName(user) ?? display;

        return new HostIdentity(login, display);
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
            new Uri(ApiBase(account.BaseUri), "git/repositories?api-version=" + ApiVersion),
            token,
            cancellationToken).ConfigureAwait(false);

        using JsonDocument document = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        List<HostRepository> repositories = [];

        if (document.RootElement.TryGetProperty("value", out JsonElement values)
            && values.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in values.EnumerateArray())
            {
                HostRepository repository = Map(element);

                if (Matches(repository, query))
                {
                    repositories.Add(repository);
                }
            }
        }

        // One call returns every repository in the organisation, so there is never a second page.
        return new HostRepositoryPage(repositories);
    }

    /// <inheritdoc />
    public string? BuildCommitUrl(RemoteUrl remote, string sha)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(sha)
            ? $"{root}/commit/{Uri.EscapeDataString(sha)}"
            : null;

    /// <inheritdoc />
    public string? BuildBranchUrl(RemoteUrl remote, string branch)
        => Web(remote) is { } root && !string.IsNullOrWhiteSpace(branch)
            ? $"{root}?version=GB{Uri.EscapeDataString(branch)}"
            : null;

    /// <inheritdoc />
    public string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null)
    {
        if (Web(remote) is not { } root || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // GC is a commit and GB is a branch; Azure DevOps needs to be told which it was given.
        string version = LooksLikeSha(reference)
            ? "GC" + Uri.EscapeDataString(reference)
            : "GB" + Uri.EscapeDataString(reference);

        string address = $"{root}?path={Uri.EscapeDataString("/" + path.TrimStart('/'))}&version={version}";

        return line is { } number and > 0
            ? address + "&line=" + number.ToString(CultureInfo.InvariantCulture)
            : address;
    }

    /// <summary>
    /// Answers whether a reference looks like a commit rather than a branch.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns><see langword="true"/> when it is all hex and long enough to be a hash.</returns>
    public static bool LooksLikeSha(string reference)
    {
        if (string.IsNullOrEmpty(reference) || reference.Length < 7 || reference.Length > 40)
        {
            return false;
        }

        foreach (char character in reference)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the browser root of a repository from its remote address.
    /// </summary>
    /// <param name="remote">The remote address.</param>
    /// <returns>The root, or <see langword="null"/> when the address names no repository.</returns>
    /// <remarks>
    /// Azure DevOps has four address shapes for one repository, and two of them are SSH endpoints
    /// on hosts that serve no web pages at all, so each is mapped back to the browser address.
    /// </remarks>
    private static string? Web(RemoteUrl remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        IReadOnlyList<string> segments = remote.Segments;

        // git@ssh.dev.azure.com:v3/org/project/repo
        if (remote.IsHost("ssh." + WellKnownHosts.AzureDevOps) && segments.Count >= 4)
        {
            return $"https://{WellKnownHosts.AzureDevOps}/{JsonHelp.EscapePath(segments[1])}"
                + $"/{JsonHelp.EscapePath(segments[2])}/_git/{JsonHelp.EscapePath(segments[3])}";
        }

        // git@vs-ssh.visualstudio.com:v3/org/project/repo
        if (remote.IsHost("vs-ssh." + WellKnownHosts.AzureDevOpsLegacy) && segments.Count >= 4)
        {
            return $"https://{JsonHelp.EscapePath(segments[1])}.{WellKnownHosts.AzureDevOpsLegacy}"
                + $"/{JsonHelp.EscapePath(segments[2])}/_git/{JsonHelp.EscapePath(segments[3])}";
        }

        // Anything else already carries the web path, _git included.
        foreach (string segment in segments)
        {
            if (string.Equals(segment, "_git", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://{remote.Host}/{JsonHelp.EscapePath(remote.Path)}";
            }
        }

        return null;
    }

    private static bool Matches(HostRepository repository, HostRepositoryQuery query)
    {
        if (query.Visibility == HostVisibility.Public && repository.IsPrivate)
        {
            return false;
        }

        if (query.Visibility == HostVisibility.Private && !repository.IsPrivate)
        {
            return false;
        }

        return query.Search is not { Length: > 0 } search
            || repository.FullName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static HostRepository Map(JsonElement element)
    {
        string name = JsonHelp.Text(element, "name") ?? string.Empty;
        string project = element.TryGetProperty("project", out JsonElement projectElement)
            ? JsonHelp.Text(projectElement, "name") ?? string.Empty
            : string.Empty;

        string visibility = element.TryGetProperty("project", out JsonElement visibilityElement)
            ? JsonHelp.Text(visibilityElement, "visibility") ?? "private"
            : "private";

        string reference = JsonHelp.Text(element, "defaultBranch") ?? string.Empty;

        return new HostRepository(
            project.Length > 0 ? $"{project}/{name}" : name,
            name,
            project.Length > 0 ? $"In the {project} project" : null,
            reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference["refs/heads/".Length..] : "main",
            JsonHelp.Text(element, "remoteUrl") ?? string.Empty,
            JsonHelp.Text(element, "sshUrl"),
            JsonHelp.Text(element, "webUrl") ?? JsonHelp.Text(element, "remoteUrl") ?? string.Empty,
            !visibility.Equals("public", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Digs the account name out of the identity's property bag, which is where Azure DevOps keeps
    /// it rather than beside the display name.
    /// </summary>
    private static string? AccountName(JsonElement user)
    {
        if (!user.TryGetProperty("properties", out JsonElement properties)
            || !properties.TryGetProperty("Account", out JsonElement account))
        {
            return null;
        }

        return JsonHelp.Text(account, "$value");
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri address,
        SecretString token,
        CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(HostHttp.ClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Basic with an empty user name and the token as the password, which is what Azure DevOps
        // documents for a personal access token.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token.Reveal())));

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw HostResponse.Classify(response, "Azure DevOps");
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
            // Azure DevOps answers a rejected token with a sign-in page rather than a 401, so
            // "this is not JSON" is where a wrong token usually lands.
            throw new HostAuthenticationException(
                "Azure DevOps answered with a sign-in page rather than data. The token is probably wrong, "
                + "expired, or lacks the 'Code: Read' scope.",
                exception);
        }
    }
}
