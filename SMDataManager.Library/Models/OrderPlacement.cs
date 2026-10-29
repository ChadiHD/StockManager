using System.Collections.Generic;
using SMDataManager.Library.Tax;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A cart on its way to becoming an order (T12), priced per line and taxed on where it goes.
    /// </summary>
    /// <remarks>
    /// Prices are the caller's for <see cref="QuoteRequest"/>'s reason. The account is absent for
    /// its reason too: <c>spOrder_PlaceFromBasket</c> derives it from the contact, or writes a
    /// guest's.
    /// </remarks>
    public class OrderPlacement
    {
        public int SiteId { get; set; }
        public int BasketId { get; set; }

        /// <summary>The signed-in contact, or null for a guest.</summary>
        public int? ContactId { get; set; }

        /// <summary>A guest's name and address. Ignored for a signed-in contact but the name.</summary>
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }

        public OrderAddressModel Delivery { get; set; }

        public List<QuoteRequestLine> Lines { get; set; } = new List<QuoteRequestLine>();

        public TaxAssessment Tax { get; set; }

        /// <summary>A guest's way back to the order. Only its hash is stored.</summary>
        public string GuestToken { get; set; }
    }

    /// <summary>Where an order goes, as <c>dbo.PurchaseAddress</c> holds it.</summary>
    public class OrderAddressModel
    {
        public string Name { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string PostCode { get; set; }

        /// <summary>ISO 3166-1 alpha-2.</summary>
        public string Country { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>
    /// An order placed, or the SKUs the store is short of. Short writes nothing.
    /// </summary>
    public class OrderPlacementResult
    {
        public string Reference { get; set; }

        /// <summary>Comma-separated, as the procedure returns them.</summary>
        public string ShortSkus { get; set; }

        public bool Placed => !string.IsNullOrEmpty(Reference);
    }
}
