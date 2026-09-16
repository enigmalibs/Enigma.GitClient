using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// A handler that answers from a script instead of from the network, and records what it was asked.
/// </summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpResponseMessage Fallback { get; set; } = new(HttpStatusCode.OK);

    public void Respond(params HttpStatusCode[] statuses)
    {
        foreach (HttpStatusCode status in statuses)
        {
            HttpStatusCode captured = status;
            _script.Enqueue(_ => new HttpResponseMessage(captured));
        }
    }

    public void Respond(Func<HttpRequestMessage, HttpResponseMessage> build) => _script.Enqueue(build);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(_script.Count > 0 ? _script.Dequeue()(request) : new HttpResponseMessage(HttpStatusCode.OK));
    }
}

/// <summary>
/// The retry the hosting providers share, and the rule that turns a refusal into a sentence.
/// </summary>
public sealed class HostHttpTests
{
    private static (HttpClient Client, ScriptedHandler Inner, HostRetryHandler Retry) Build()
    {
        ScriptedHandler inner = new();
        List<TimeSpan> waits = [];

        HostRetryHandler retry = new((wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        })
        {
            InnerHandler = inner,
        };

        return (new HttpClient(retry), inner, retry);
    }

    private static HttpResponseMessage WithRetryAfter(HttpStatusCode status, TimeSpan after)
    {
        HttpResponseMessage response = new(status);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(after);

        return response;
    }

    [Fact]
    public async Task ASuccessfulRequestIsSentOnce()
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(HttpStatusCode.OK);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(inner.Requests);
        Assert.Equal(0, retry.Retries);
    }

    [Fact]
    public async Task AServerErrorIsRetriedExactlyOnce()
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(1, retry.Retries);
    }

    [Fact]
    public async Task AServerErrorThatSurvivesTheRetryIsHandedBack()
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        // Once, not until it works: a second failure is a real one.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(1, retry.Retries);
    }

    [Fact]
    public async Task ARateLimitWithAShortRetryAfterIsAbsorbed()
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(_ => WithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(1)));
        inner.Respond(HttpStatusCode.OK);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, retry.Retries);
    }

    [Fact]
    public async Task ARateLimitTooFarAwayIsReportedRatherThanWaitedFor()
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(_ => WithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromHours(1)));

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        // Waiting an hour behind a dialog is not a retry, it is a hang.
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(inner.Requests);
        Assert.Equal(0, retry.Retries);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AFailureTheHostMeantIsNotRetried(HttpStatusCode status)
    {
        (HttpClient client, ScriptedHandler inner, HostRetryHandler retry) = Build();

        inner.Respond(status);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://example.test/user"),
            TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Single(inner.Requests);
        Assert.Equal(0, retry.Retries);
    }

    [Fact]
    public async Task TheRetriedRequestCarriesTheSameHeaders()
    {
        (HttpClient client, ScriptedHandler inner, _) = Build();

        inner.Respond(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("https://example.test/user"));
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", "glpat-secret");

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Requests.Count);
        Assert.True(inner.Requests[1].Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values));
        Assert.Equal(["glpat-secret"], values!);
    }

    [Fact]
    public void RetryAfter_ReadsBothFormsOfTheHeader()
    {
        using HttpResponseMessage seconds = WithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), HostRetryHandler.RetryAfter(seconds));

        using HttpResponseMessage date = new(HttpStatusCode.TooManyRequests);
        date.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(60));

        TimeSpan? fromDate = HostRetryHandler.RetryAfter(date);

        Assert.NotNull(fromDate);
        Assert.InRange(fromDate!.Value, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(61));

        using HttpResponseMessage past = new(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-60));

        // A date already gone is "now", not a negative wait.
        Assert.Equal(TimeSpan.Zero, HostRetryHandler.RetryAfter(past));

        using HttpResponseMessage none = new(HttpStatusCode.TooManyRequests);

        Assert.Null(HostRetryHandler.RetryAfter(none));
    }

    // ---------------------------------------------------------------- classification

    [Fact]
    public void ARejectedTokenIsSaidToBeARejectedToken()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Unauthorized);

        HostException exception = HostResponse.Classify(response, "GitHub");

        HostAuthenticationException authentication = Assert.IsType<HostAuthenticationException>(exception);

        Assert.Contains("GitHub", authentication.Message, StringComparison.Ordinal);
        Assert.Contains("scopes", authentication.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AForbiddenWithNoRateLimitIsAMissingScope()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden);

        Assert.IsType<HostAuthenticationException>(HostResponse.Classify(response, "GitLab"));
    }

    [Fact]
    public void AForbiddenWithARateLimitResetIsARateLimit()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        DateTimeOffset reset = DateTimeOffset.UtcNow.AddMinutes(20);

        HostRateLimitException limit = Assert.IsType<HostRateLimitException>(
            HostResponse.Classify(response, "GitHub", reset));

        Assert.Equal(reset, limit.ResetsAt);
        Assert.NotNull(limit.RetryAfter);
    }

    [Fact]
    public void ATooManyRequestsTakesItsResetFromRetryAfter()
    {
        using HttpResponseMessage response = WithRetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(5));

        HostRateLimitException limit = Assert.IsType<HostRateLimitException>(
            HostResponse.Classify(response, "Azure DevOps"));

        Assert.NotNull(limit.ResetsAt);
        Assert.InRange(limit.RetryAfter!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ANotFoundPointsAtTheInstanceUrl()
    {
        using HttpResponseMessage response = new(HttpStatusCode.NotFound);

        HostRequestException request = Assert.IsType<HostRequestException>(HostResponse.Classify(response, "GitLab"));

        Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
        Assert.Contains("instance URL", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnythingElseCarriesTheStatusItCameWith()
    {
        using HttpResponseMessage response = new(HttpStatusCode.BadGateway) { ReasonPhrase = "Bad Gateway" };

        HostRequestException request = Assert.IsType<HostRequestException>(HostResponse.Classify(response, "GitHub"));

        Assert.Equal(HttpStatusCode.BadGateway, request.StatusCode);
        Assert.Equal("502", request.StatusText);
        Assert.Contains("502", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUserAgentNamesTheProductAndItsVersion()
    {
        ProductInfoHeaderValue agent = HostHttp.UserAgent();

        Assert.Equal("Enigma-GitClient", agent.Product!.Name);
        Assert.False(string.IsNullOrWhiteSpace(agent.Product.Version));
    }
}
