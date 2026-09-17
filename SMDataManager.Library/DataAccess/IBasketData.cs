using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Baskets and their lines, scoped to a store. See <see cref="IAccountData"/> for why
    /// siteId is mandatory on every method.
    /// </summary>
    /// <remarks>
    /// The token is a bearer credential rather than an identifier — it is the only thing
    /// standing between an anonymous visitor and somebody else's basket — so every lookup takes
    /// the site as well, and the procedures join the basket rather than trusting a caller to
    /// have checked it. A guessed basket id reads as empty, not as another viewer's list.
    /// </remarks>
    public interface IBasketData
    {
        /// <summary>This viewer's basket, or null. Creates nothing.</summary>
        BasketModel FindBasket(int siteId, string token, int? contactId);

        /// <summary>This viewer's basket, creating one if they have none.</summary>
        BasketModel EnsureBasket(int siteId, string token, int? contactId);

        List<BasketLineModel> GetLines(int basketId, int siteId, int? customerGroupId);

        /// <summary>Adds a product, or raises the quantity of the line already there.</summary>
        void AddLine(int basketId, int siteId, int productId, int quantity, int? customerGroupId);

        /// <summary>Sets a line's quantity. A quantity below one removes the line.</summary>
        bool SetQuantity(int basketId, int siteId, int productId, int quantity);

        bool RemoveLine(int basketId, int siteId, int productId);

        /// <summary>
        /// Attaches the browser's basket to a contact who has just signed in, merging it into
        /// whatever they already had. Returns the surviving basket id, or null when there was
        /// nothing on either side.
        /// </summary>
        int? ClaimBasket(int siteId, string token, int contactId);

        /// <summary>Deletes a page of anonymous baskets nobody has touched. Returns how many.</summary>
        int PurgeAbandoned(int olderThanDays, int take);
    }
}
