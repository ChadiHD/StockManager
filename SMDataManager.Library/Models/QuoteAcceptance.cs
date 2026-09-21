namespace SMDataManager.Library.Models
{
    /// <summary>
    /// Who is turning a quote into an order, and the customer's own reference for it.
    /// </summary>
    /// <remarks>
    /// A parameter object rather than two more arguments, because the rule it carries cannot be
    /// stated in a parameter list: an order records exactly one placer. <c>dbo.Purchase</c> has
    /// a column for each — <c>StaffId</c> into <c>dbo.[User]</c>, which holds staff, and
    /// <c>PlacedByContactId</c> into <c>dbo.Contact</c>, which holds customers' employees —
    /// and <c>CK_Purchase_Placer</c> refuses a row with both or neither.
    ///
    /// Two factory methods and no public constructor, so "both" and "neither" are not states a
    /// caller can reach. That matters here more than it usually would: <c>StaffId</c> is a
    /// foreign key and an id-shaped string that names nobody satisfies the compiler and fails
    /// in the database, after the caller has been told it succeeded. That has now happened
    /// three times in this codebase — <c>Account.ApprovedBy</c> in T3, a test fixture in T4,
    /// and this path would have been the third.
    /// </remarks>
    public sealed class QuoteAcceptance
    {
        private QuoteAcceptance(string staffId, int? placedByContactId, string poNumber)
        {
            StaffId = staffId;
            PlacedByContactId = placedByContactId;
            PoNumber = poNumber;
        }

        /// <summary>A <c>dbo.[User].UserId</c>, or null when a customer accepted.</summary>
        public string StaffId { get; }

        /// <summary>A <c>dbo.Contact.Id</c>, or null when staff converted.</summary>
        public int? PlacedByContactId { get; }

        /// <summary>The customer's purchase-order number, or null.</summary>
        public string PoNumber { get; }

        /// <summary>An admin converting a quote from the portal.</summary>
        public static QuoteAcceptance ByStaff(string staffId, string poNumber = null) =>
            new QuoteAcceptance(staffId, null, poNumber);

        /// <summary>A customer accepting their own quote from the storefront.</summary>
        public static QuoteAcceptance ByCustomer(int contactId, string poNumber = null) =>
            new QuoteAcceptance(null, contactId, poNumber);
    }

    /// <summary>
    /// The outcome of converting a quote: the order, or why no order was created.
    /// </summary>
    /// <remarks>
    /// Shaped like <see cref="DistributorFeedResult"/>'s <c>AlreadyRunning</c>, and for the
    /// same reason. A quote somebody else decided while this screen was open is not a fault in
    /// the request — under T5 it is the ordinary race between an admin's Convert and a
    /// customer's Accept — so it has to be distinguishable from a failure all the way out to a
    /// 409 and a message that says what actually happened.
    /// </remarks>
    public sealed class QuoteAcceptanceResult
    {
        private QuoteAcceptanceResult(OrderModel order, bool noLongerAwaitingAcceptance)
        {
            Order = order;
            NoLongerAwaitingAcceptance = noLongerAwaitingAcceptance;
        }

        /// <summary>The order that now exists, or null.</summary>
        public OrderModel Order { get; }

        /// <summary>
        /// The quote had already been accepted or rejected, so this call created nothing.
        /// </summary>
        public bool NoLongerAwaitingAcceptance { get; }

        public bool Succeeded => Order != null;

        public static QuoteAcceptanceResult Converted(OrderModel order) =>
            new QuoteAcceptanceResult(order, false);

        public static QuoteAcceptanceResult AlreadyDecided() =>
            new QuoteAcceptanceResult(null, true);
    }
}
