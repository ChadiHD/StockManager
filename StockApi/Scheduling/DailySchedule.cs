namespace StockApi.Scheduling
{
    /// <summary>
    /// "Once a day at this time, UTC", for the background jobs that run on a clock.
    /// </summary>
    /// <remarks>
    /// Extracted from the feed scheduler when the quote-expiry sweep became the second job to
    /// need it. Two copies of how a time of day is read and when the next run falls is how one
    /// of them comes to sync at midnight for ever while the other does as it is told.
    ///
    /// A time of day and nothing cleverer. Cron would buy "twice a day" and "weekdays only" at
    /// the cost of a dependency and a syntax to get wrong, and nothing has asked for either.
    /// </remarks>
    public static class DailySchedule
    {
        /// <summary>
        /// Reads a time of day from <paramref name="key"/>, or <paramref name="fallback"/> when
        /// it is unset. False, with an Error logged, when it is set to something unreadable.
        /// </summary>
        /// <remarks>
        /// Refused rather than defaulted when unreadable: a misread time would run at the
        /// fallback for ever while the configuration plainly said otherwise, and nothing would
        /// explain the difference.
        /// </remarks>
        public static bool TryRead(
            IConfiguration config, string key, string fallback, ILogger logger, out TimeSpan time)
        {
            var configured = config.GetValue<string?>(key);

            if (string.IsNullOrWhiteSpace(configured))
            {
                time = TimeSpan.Parse(fallback);
                return true;
            }

            if (TimeSpan.TryParse(configured, out time)
                && time >= TimeSpan.Zero
                && time < TimeSpan.FromDays(1))
            {
                return true;
            }

            logger.LogError(
                "{Key} is '{Configured}', which is not a time of day between 00:00 and 23:59. " +
                "Nothing will run on that schedule until it is corrected.", key, configured);

            time = default;
            return false;
        }

        /// <summary>How long from <paramref name="nowUtc"/> until the next run.</summary>
        /// <remarks>
        /// Today's slot if it has not passed, otherwise tomorrow's. A host that starts after the
        /// hour therefore waits a day rather than running immediately, which is the right way
        /// round: a restart loop would otherwise repeat the job on every restart, and a missed
        /// day is something each job's own design has to tolerate anyway.
        /// </remarks>
        public static TimeSpan UntilNext(TimeSpan time, DateTime nowUtc)
        {
            var next = nowUtc.Date + time;

            if (next <= nowUtc)
            {
                next = next.AddDays(1);
            }

            return next - nowUtc;
        }
    }
}
