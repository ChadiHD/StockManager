using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// The four states a portal order can be in, and the only four
    /// <c>CK_Purchase_Status</c> accepts alongside NULL.
    /// </summary>
    /// <remarks>
    /// The same story as <see cref="QuoteStatus"/>, one table over. These had been a comment
    /// on <c>Purchase.Status</c> since the column was written, and <c>spOrder_UpdateStatus</c>
    /// stored whatever string reached it — so a typo produced an order that matched no filter
    /// and no step in the progress bar. Now the constraint refuses it, and a constraint
    /// violation raised inside a procedure reaches the caller as a 500 rather than as an
    /// answer, so the boundary has to refuse it first.
    ///
    /// NULL is not in this set and is not a state: a desktop POS sale has no order status at
    /// all, and every <c>spOrder_*</c> procedure filters it out with
    /// <c>Reference IS NOT NULL</c> before the question arises.
    /// </remarks>
    public static class OrderStatus
    {
        /// <summary>Raised and not yet paid. Where an accepted quote lands.</summary>
        public const string AwaitingPayment = "Awaiting payment";

        /// <summary>Being picked and packed.</summary>
        public const string Processing = "Processing";

        /// <summary>Shipped and invoiced. Terminal.</summary>
        public const string Fulfilled = "Fulfilled";

        /// <summary>Withdrawn before fulfilment. Terminal.</summary>
        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All =
            new[] { AwaitingPayment, Processing, Fulfilled, Cancelled };

        public static bool IsKnown(string status) =>
            All.Any(known => string.Equals(known, status, StringComparison.Ordinal));
    }
}
