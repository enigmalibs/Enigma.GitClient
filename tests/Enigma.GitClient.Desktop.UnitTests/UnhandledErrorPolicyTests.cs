using System;
using System.Collections.Generic;
using System.Threading;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The safety net's policy (BUG-9DE3): which exceptions are not errors, how a burst becomes one dialog,
/// what is not shown twice, and what the details say — without a window or a platform.
/// </summary>
public sealed class UnhandledErrorPolicyTests
{
    private readonly ManualTimeProvider _clock = new();

    private UnhandledErrorPolicy Policy() => new(_clock);

    /// <summary>An exception with a stack trace, as one that was really thrown has.</summary>
    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    // ---------------------------------------------------------------- one dialog for a burst

    [Fact]
    public void TheFirstError_OpensABatch()
    {
        UnhandledErrorPolicy policy = Policy();
        Exception error = Thrown(new InvalidOperationException("first"));

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(error));
        Assert.Same(error, policy.Pending?.First);
        Assert.Equal(0, policy.Pending?.More);
    }

    [Fact]
    public void ABurstBeforeTheDialogShows_IsOneBatch_CountingTheRest()
    {
        UnhandledErrorPolicy policy = Policy();

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(Thrown(new InvalidOperationException("first"))));
        Assert.Equal(UnhandledErrorDecision.Merge, policy.Offer(Thrown(new InvalidOperationException("second"))));
        Assert.Equal(UnhandledErrorDecision.Merge, policy.Offer(Thrown(new ArgumentException("third"))));

        UnhandledErrorBatch batch = Assert.IsType<UnhandledErrorBatch>(policy.BeginShow());

        Assert.Equal("first", batch.Message);
        Assert.Equal(2, batch.More);
        Assert.Contains("Error 1 of 3", batch.Details, StringComparison.Ordinal);
        Assert.Contains("second", batch.Details, StringComparison.Ordinal);
        Assert.Contains("System.ArgumentException: third", batch.Details, StringComparison.Ordinal);
        Assert.Null(policy.Pending);
    }

    [Fact]
    public void AnErrorWhileTheDialogIsOpen_JoinsIt_AndSaysSo()
    {
        UnhandledErrorPolicy policy = Policy();
        policy.Offer(Thrown(new InvalidOperationException("first")));
        UnhandledErrorBatch batch = policy.BeginShow()!;
        int changes = 0;
        batch.Changed += (_, _) => changes++;

        Assert.Equal(UnhandledErrorDecision.Merge, policy.Offer(Thrown(new InvalidOperationException("later"))));

        Assert.Equal(1, batch.More);
        Assert.Equal(1, changes);
        Assert.Null(policy.Pending);
    }

    [Fact]
    public void AnErrorAfterTheDialogClosed_OpensANewBatch()
    {
        UnhandledErrorPolicy policy = Policy();
        policy.Offer(Thrown(new InvalidOperationException("first")));
        policy.BeginShow();
        policy.EndShow();

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(Thrown(new ArgumentException("another"))));
        Assert.Equal("another", policy.Pending?.Message);
    }

    [Fact]
    public void TheSameExceptionTwice_CountsOnce()
    {
        UnhandledErrorPolicy policy = Policy();
        Exception error = Thrown(new InvalidOperationException("twice"));

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(error));
        Assert.Equal(UnhandledErrorDecision.Ignore, policy.Offer(error));
        Assert.Equal(0, policy.Pending?.More);
    }

    [Fact]
    public void BeginShow_WithNothingPending_IsNothing()
        => Assert.Null(Policy().BeginShow());

    // ---------------------------------------------------------------- repeats

    [Fact]
    public void AnErrorJustClosed_IsNotShownAgain_ForAMinute()
    {
        UnhandledErrorPolicy policy = Policy();

        policy.Offer(Repeated());
        policy.BeginShow();
        policy.EndShow();

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(UnhandledErrorDecision.Suppress, policy.Offer(Repeated()));
        Assert.Null(policy.Pending);

        _clock.Advance(UnhandledErrorPolicy.RepeatWindow);

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(Repeated()));
    }

    [Fact]
    public void AnErrorCountedIntoAClosedDialog_IsNotShownAgainEither()
    {
        UnhandledErrorPolicy policy = Policy();

        policy.Offer(Thrown(new ArgumentException("first")));
        policy.BeginShow();
        policy.Offer(Repeated());
        policy.EndShow();

        Assert.Equal(UnhandledErrorDecision.Suppress, policy.Offer(Repeated()));
    }

    /// <summary>The same failure, thrown again from the same place: a new instance each time.</summary>
    private static Exception Repeated() => Thrown(new InvalidOperationException("the refresh failed"));

    // ---------------------------------------------------------------- cancellations

    [Fact]
    public void ACancellationWhileClosing_IsIgnored()
    {
        UnhandledErrorPolicy policy = Policy();
        policy.IsShuttingDown = true;

        Assert.Equal(UnhandledErrorDecision.Ignore, policy.Offer(Thrown(new OperationCanceledException())));
        Assert.Equal(UnhandledErrorDecision.Ignore, policy.Offer(new AggregateException(new TaskCanceledExceptionStandIn())));
        Assert.Null(policy.Pending);
    }

    [Fact]
    public void ACancellationSomeoneAskedFor_IsIgnored()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.Equal(
            UnhandledErrorDecision.Ignore,
            Policy().Offer(Thrown(new OperationCanceledException(source.Token))));
    }

    [Fact]
    public void ACancellationNobodyAskedFor_IsAnError()
    {
        // A timeout: cancelled, but not by a token anyone cancelled.
        UnhandledErrorPolicy policy = Policy();

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(Thrown(new TimeoutStandIn())));
    }

    [Fact]
    public void AnAggregateWithOneRealError_IsAnError()
    {
        UnhandledErrorPolicy policy = Policy();
        policy.IsShuttingDown = true;

        AggregateException mixed = new(new OperationCanceledException(), new InvalidOperationException("real"));

        Assert.Equal(UnhandledErrorDecision.Queue, policy.Offer(mixed));
    }

    private sealed class TaskCanceledExceptionStandIn : OperationCanceledException;

    private sealed class TimeoutStandIn() : OperationCanceledException("The request was canceled due to the configured timeout.");

    // ---------------------------------------------------------------- what the dialog says

    [Fact]
    public void TheMessage_IsTheOneInsideTheWrappers()
    {
        AggregateException wrapped = new(new System.Reflection.TargetInvocationException(new InvalidOperationException("what happened")));

        Assert.Equal("what happened", UnhandledErrorPolicy.Summarise(wrapped));
    }

    [Fact]
    public void TheDetails_HaveTheType_TheMessage_TheInnerExceptions_AndTheStack()
    {
        Exception error = Thrown(new InvalidOperationException("outer", Thrown(new ArgumentException("inner"))));

        string details = UnhandledErrorPolicy.BuildDetails(error);

        Assert.StartsWith("System.InvalidOperationException: outer", details, StringComparison.Ordinal);
        Assert.Contains("--- Inner exception ---", details, StringComparison.Ordinal);
        Assert.Contains("System.ArgumentException: inner", details, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), details, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetails_ListEveryExceptionOfAnAggregate()
    {
        AggregateException error = new(new InvalidOperationException("one"), new ArgumentException("two"));

        string details = UnhandledErrorPolicy.BuildDetails(error);

        Assert.Contains("--- Inner exception 1 of 2 ---", details, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: one", details, StringComparison.Ordinal);
        Assert.Contains("--- Inner exception 2 of 2 ---", details, StringComparison.Ordinal);
        Assert.Contains("System.ArgumentException: two", details, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetails_NeverCarryTheExceptionsData()
    {
        Exception error = Thrown(new InvalidOperationException("failed"));
        error.Data["Authorization"] = "Bearer ghp_dataSecretThatMustNotLeak";
        error.Data["request"] = "https://user:ghp_anotherSecret@github.com/x";

        string details = UnhandledErrorPolicy.BuildDetails(error);

        Assert.DoesNotContain("ghp_dataSecretThatMustNotLeak", details, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_anotherSecret", details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fetch https://ada:ghp_secretTokenValue1234@github.com/x/y.git failed", "ghp_secretTokenValue1234")]
    [InlineData("the host said Authorization: Bearer ghp_headerSecretValue", "ghp_headerSecretValue")]
    [InlineData("GET https://gitlab.example.com/api?private_token=glpat-querySecret", "glpat-querySecret")]
    public void ACredentialInTheMessage_IsTakenOut_OfTheDetailsAndTheMessage(string message, string secret)
    {
        Exception error = Thrown(new InvalidOperationException(message, Thrown(new ArgumentException(message))));
        UnhandledErrorBatch batch = new(error);

        Assert.DoesNotContain(secret, batch.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, batch.Message, StringComparison.Ordinal);
        Assert.Contains("***", batch.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeepChain_StopsSomewhere()
    {
        Exception error = new InvalidOperationException("0");

        for (int depth = 1; depth < 50; depth++)
        {
            error = new InvalidOperationException(depth.ToString(System.Globalization.CultureInfo.InvariantCulture), error);
        }

        string details = UnhandledErrorPolicy.BuildDetails(error);

        List<string> inners = [.. details.Split("--- Inner exception ---")];

        Assert.InRange(inners.Count, 2, 10);
    }
}
