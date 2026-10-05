using System;

namespace SMDataManager.Library.DataAccess
{
    public interface IHousekeepingData
    {
        /// <summary>
        /// Deletes anonymous baskets untouched since the first cutoff and sent mail older than
        /// the second. Dead letters, in-flight mail and signed-in customers' baskets are kept.
        /// </summary>
        HousekeepingResult Sweep(DateTime abandonedBasketsBeforeUtc, DateTime sentMailBeforeUtc);
    }

    public sealed record HousekeepingResult(int AbandonedBaskets, int SentMail);
}
