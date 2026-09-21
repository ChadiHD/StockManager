using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// The four states a quote can be in, and the only four <c>CK_Quote_Status</c> accepts.
    /// </summary>
    /// <remarks>
    /// Named here rather than left as literals because the constraint landed after the column
    /// did: until T5 <c>spQuote_UpdateStatus</c> would store any string a caller sent, and a
    /// typo simply produced a quote that matched no filter. Now it is a constraint violation
    /// from inside a procedure, which reaches the caller as a 500 rather than as an answer, so
    /// the boundary has to refuse it first.
    /// </remarks>
    public static class QuoteStatus
    {
        /// <summary>Submitted by the customer, not yet priced by sales.</summary>
        public const string Requested = "Requested";

        /// <summary>Priced and returned to the customer, awaiting their decision.</summary>
        public const string Priced = "Priced";

        /// <summary>Converted into an order. Terminal.</summary>
        public const string Accepted = "Accepted";

        /// <summary>Declined by the customer or withdrawn by sales. Terminal.</summary>
        public const string Rejected = "Rejected";

        public static readonly IReadOnlyList<string> All =
            new[] { Requested, Priced, Accepted, Rejected };

        public static bool IsKnown(string status) =>
            All.Any(known => string.Equals(known, status, StringComparison.Ordinal));
    }
}
