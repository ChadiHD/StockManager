using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// The payment terms a store offers, as the label a buyer recognises and the number of
    /// days a due date is calculated from.
    /// </summary>
    /// <remarks>
    /// <c>Account</c> stores both, and <c>CK_Account_Terms</c> keeps them agreeing. This is
    /// the one place that knows which label means how many days, so a caller sends the pair
    /// and nothing downstream parses "Net 30" back into a number.
    ///
    /// Parsing is what the alternative would have been, and it holds only until somebody
    /// types <c>NET30</c>, <c>net 30</c> or <c>30 days</c> into a column with no constraint
    /// on its text. The pre-deployment backfill does parse, because it has existing rows and
    /// no other source — and it falls back to thirty days rather than zero, because zero
    /// means prepaid and would put a credit account onto immediate payment silently.
    /// </remarks>
    public static class PaymentTerms
    {
        /// <summary>Paid before the goods move. Zero days, and the only label that is.</summary>
        public const string Prepaid = "Prepaid";

        /// <summary>Label to days, in the order the portal offers them.</summary>
        private static readonly IReadOnlyList<(string Label, int Days)> Terms =
            new[]
            {
                (Prepaid, 0),
                ("Net 14", 14),
                ("Net 30", 30),
                ("Net 45", 45),
                ("Net 60", 60),
            };

        public static readonly IReadOnlyList<string> All =
            Terms.Select(term => term.Label).ToArray();

        /// <summary>The credit terms, which is every label except <see cref="Prepaid"/>.</summary>
        public static readonly IReadOnlyList<string> Credit =
            Terms.Where(term => term.Days > 0).Select(term => term.Label).ToArray();

        public static bool IsKnown(string label) =>
            All.Any(known => string.Equals(known, label, StringComparison.Ordinal));

        /// <summary>
        /// Days for a known label, and zero for anything else.
        /// </summary>
        /// <remarks>
        /// Zero for an unknown label is safe here and would not be in the backfill: a caller
        /// reaching this with something unknown is sending a value no screen offers, and
        /// prepaid is the terms that extend no credit. The constraint then refuses the pair
        /// unless the label really was <see cref="Prepaid"/>, so a bad label fails loudly at
        /// the write rather than quietly granting terms.
        /// </remarks>
        public static int DaysFor(string label) =>
            Terms.FirstOrDefault(term =>
                string.Equals(term.Label, label, StringComparison.Ordinal)).Days;

        /// <summary>Whether these terms extend credit at all.</summary>
        public static bool ExtendsCredit(string label) => DaysFor(label) > 0;
    }
}
