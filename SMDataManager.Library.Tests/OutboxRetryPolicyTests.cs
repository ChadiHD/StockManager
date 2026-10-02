using FluentAssertions;
using SMDataManager.Library.Email;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The backoff a failed message waits out, and the point it is given up on.
/// </summary>
public class OutboxRetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 32)]
    // Would be 64, and is held to the hour: a longer gap is a message an operator hears about
    // the next day rather than the same morning.
    [InlineData(7, 60)]
    public void WaitsDoubleFromAMinuteAndStopAtAnHour(int attempts, int minutes)
    {
        OutboxRetryPolicy.After(attempts).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData(OutboxRetryPolicy.MaxAttempts)]
    [InlineData(OutboxRetryPolicy.MaxAttempts + 1)]
    public void TheLastAttemptIsNotRetried(int attempts)
    {
        // Null is the dead letter. A bad address retried for ever keeps its place at the front
        // of the queue and tells nobody.
        OutboxRetryPolicy.After(attempts).Should().BeNull();
    }

    [Fact]
    public void TheWholeScheduleIsAboutTwoHours()
    {
        var total = Enumerable.Range(1, OutboxRetryPolicy.MaxAttempts - 1)
            .Select(attempt => OutboxRetryPolicy.After(attempt)!.Value)
            .Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait);

        total.Should().Be(TimeSpan.FromMinutes(123));
    }
}
