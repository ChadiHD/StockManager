using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A basket in progress: what a customer has picked out before it becomes a quote request.
    /// </summary>
    /// <remarks>
    /// Found by <see cref="ContactId"/> when somebody is signed in and by <see cref="Token"/>
    /// when nobody is. See dbo.Basket for why this is not a Quote with a draft status.
    /// </remarks>
    public class BasketModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }

        /// <summary>The signed-in contact this basket belongs to, or null while anonymous.</summary>
        public int? ContactId { get; set; }

        /// <summary>
        /// The bearer credential for an anonymous basket, held in an HttpOnly cookie.
        /// </summary>
        /// <remarks>
        /// Must not reach a response body, a log or a rendered page: whoever holds it holds the
        /// basket. Only <c>BasketService</c> writes it, and only into the cookie.
        /// </remarks>
        public string Token { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>
    /// One basket line and the product fields a page needs to price and render it.
    /// </summary>
    /// <remarks>
    /// Carries <see cref="RetailPrice"/> and <see cref="Cost"/> rather than a net price, because
    /// the price a customer is shown comes from <c>PriceResolver</c> and nowhere else. Same
    /// division as <c>CatalogItemModel</c>, and the same warning applies to <see cref="Cost"/> —
    /// it is a buy price and must not reach a view record.
    /// </remarks>
    public class BasketLineModel
    {
        public int Id { get; set; }
        public int BasketId { get; set; }
        public int ProductId { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal RetailPrice { get; set; }
        public decimal? Cost { get; set; }
        public int QuantityInStock { get; set; }
        public DateTime AddedUtc { get; set; }

        /// <summary>
        /// Whether the product is still visible in this store to this customer group.
        /// </summary>
        /// <remarks>
        /// False for a line whose product was delisted, went stale, lost its category mapping,
        /// or is excluded by the group the customer signed in to. Such lines are returned
        /// rather than dropped: a basket that silently loses rows is one the customer cannot
        /// reason about, and the submit path has to refuse them explicitly.
        /// </remarks>
        public bool Available { get; set; }
    }
}
