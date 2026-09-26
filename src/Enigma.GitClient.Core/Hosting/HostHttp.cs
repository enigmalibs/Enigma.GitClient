using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diagnostics;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// What every hosting provider's HTTP client agrees on.
/// </summary>
public static class HostHttp
{
    /// <summary>
    /// The name every provider resolves its client under.
    /// </summary>
    public const string ClientName = "enigma-hosting";

    /// <summary>
    /// How long a call may take before it is given up on. Long enough for a slow self-hosted
    /// instance, short enough that a hung request does not look like a hung application.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a retry will wait, however far in the future a <c>Retry-After</c> points.
    /// </summary>
    /// <remarks>
    /// A rate-limited host can name a reset an hour away. Waiting for it behind a dialog is not a
    /// retry, it is a hang; past this the failure is reported and the user decides.
    /// </remarks>
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Builds the product's user agent, which several hosts require and all of them log.
    /// </summary>
    /// <returns>The user-agent value.</returns>
    public static ProductInfoHeaderValue UserAgent()
        => new(ProductInformation.Name.Replace('.', '-'), ProductInformation.GetVersion());
}

/// <summary>
/// Retries a request once when the host says it is temporarily unable to answer.
/// </summary>
/// <remarks>
/// <para>
/// Once, not repeatedly: a second failure is a real one, and a client that keeps retrying a
/// rate-limited host is a client that gets the account throttled harder. A <c>Retry-After</c> is
/// honoured when the host sends one, capped by <see cref="HostHttp.MaximumRetryDelay"/>.
/// </para>
/// <para>
/// 429 is retried here as well as being classified by the providers: a single 429 with a short
/// <c>Retry-After</c> is a hiccup worth absorbing, and one that survives the retry is the real rate
/// limit the user needs to be told about.
/// </para>
/// </remarks>
public sealed class HostRetryHandler : DelegatingHandler
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="delay">
    /// How to wait between the attempts. The default is a real delay; a test passes one that does
    /// not spend the wall-clock time.
    /// </param>
    public HostRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((wait, token) => Task.Delay(wait, token));

    /// <summary>
    /// Gets how many requests this handler has retried, which is what a test asserts on.
    /// </summary>
    public int Retries { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!ShouldRetry(response.StatusCode))
        {
            return response;
        }

        TimeSpan wait = RetryAfter(response) ?? TimeSpan.FromMilliseconds(250);

        if (wait > HostHttp.MaximumRetryDelay)
        {
            // Too far away to wait for: hand the failure back so it can be reported with the time.
            return response;
        }

        response.Dispose();

        await _delay(wait, cancellationToken).ConfigureAwait(false);

        Retries++;

        return await base.SendAsync(await CloneAsync(request, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldRetry(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// Reads a <c>Retry-After</c>, in either of the two forms the header is allowed to take.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>How long to wait, or <see langword="null"/> when the host did not say.</returns>
    public static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        RetryConditionHeaderValue? header = response.Headers.RetryAfter;

        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header.Date is { } date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;

            return until < TimeSpan.Zero ? TimeSpan.Zero : until;
        }

        return null;
    }

    /// <summary>
    /// Copies a request so it can be sent a second time, which a sent request cannot be.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage clone = new(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (KeyValuePair<string, object?> option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ByteArrayContent content = new(body);

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
}

/// <summary>
/// Turns a failed response into the exception that says what the user can do about it.
/// </summary>
/// <remarks>
/// Every provider classifies the same four things — rejected token, rate limit, "not there", and
/// everything else — so the rule lives here rather than three times over, and the wording a user
/// sees does not depend on which host refused them.
/// </remarks>
public static class HostResponse
{
    /// <summary>
    /// Builds the exception for a response that failed.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="hostName">What to call the host in the message.</param>
    /// <param name="rateLimitReset">When the limit lifts, if the host said so in its own header.</param>
    /// <returns>The exception to throw.</returns>
    public static HostException Classify(
        HttpResponseMessage response,
        string hostName,
        DateTimeOffset? rateLimitReset = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        DateTimeOffset? reset = rateLimitReset;

        if (reset is null && HostRetryHandler.RetryAfter(response) is { } after)
        {
            reset = DateTimeOffset.UtcNow + after;
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new HostAuthenticationException(
                $"{hostName} rejected the token. Check that it is still valid and has the scopes this client asks for."),

            HttpStatusCode.Forbidden when reset is not null => new HostRateLimitException(
                $"{hostName} is rate-limiting this account.",
                reset),

            HttpStatusCode.Forbidden => new HostAuthenticationException(
                $"{hostName} refused the request. The token is valid but does not have the scope it needs."),

            HttpStatusCode.TooManyRequests => new HostRateLimitException(
                $"{hostName} is rate-limiting this account.",
                reset),

            HttpStatusCode.NotFound => new HostRequestException(
                $"{hostName} has nothing at that address. Check the instance URL.",
                HttpStatusCode.NotFound),

            _ => new HostRequestException(
                $"{hostName} answered {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} "
                + $"{response.ReasonPhrase ?? response.StatusCode.ToString()}.",
                response.StatusCode),
        };
    }
}
