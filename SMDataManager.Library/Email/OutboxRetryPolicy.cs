using System;

namespace SMDataManager.Library.Email
{
    /// <summary>
    /// When to try a failed message again, and when to stop.
    /// </summary>
    /// <remarks>
    /// Doubling from a minute, capped at an hour, eight attempts: 1, 2, 4, 8, 16, 32 and 60
    /// minutes between them, a little over two hours in all. Long enough to ride out a mail
    /// relay being restarted or a provider's brief outage, short enough that an operator hears
    /// about a dead address the same morning rather than the next day.
    ///
    /// Then the message is dead-lettered and the operator is told, because a permanently bad
    /// address retried for ever is a row that keeps its place at the front of the queue and
    /// says nothing.
    /// </remarks>
    public static class OutboxRetryPolicy
    {
        public const int MaxAttempts = 8;

        public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

        /// <summary>
        /// The wait before the next attempt, or null when <paramref name="attempts"/> has used
        /// them all.
        /// </summary>
        /// <param name="attempts">Attempts made so far, counting the one that just failed.</param>
        public static TimeSpan? After(int attempts)
        {
            if (attempts >= MaxAttempts)
            {
                return null;
            }

            var wait = TimeSpan.FromMinutes(Math.Pow(2, Math.Max(attempts, 1) - 1));

            return wait < Longest ? wait : Longest;
        }
    }
}
