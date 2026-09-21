using System.Collections.Generic;
using System.Linq;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class BasketData : IBasketData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public BasketData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public BasketModel FindBasket(int siteId, string token, int? contactId)
        {
            return _sqlDataAccess.LoadData<BasketModel, dynamic>("dbo.spBasket_Find", new
            {
                SiteId = siteId,
                Token = token,
                ContactId = contactId
            }, "SMDatabase").FirstOrDefault();
        }

        public BasketModel EnsureBasket(int siteId, string token, int? contactId)
        {
            return _sqlDataAccess.LoadData<BasketModel, dynamic>("dbo.spBasket_Ensure", new
            {
                SiteId = siteId,
                Token = token,
                ContactId = contactId
            }, "SMDatabase").FirstOrDefault();
        }

        public List<BasketLineModel> GetLines(int basketId, int siteId, int? customerGroupId)
        {
            return _sqlDataAccess.LoadData<BasketLineModel, dynamic>("dbo.spBasket_GetLines", new
            {
                BasketId = basketId,
                SiteId = siteId,
                CustomerGroupId = customerGroupId
            }, "SMDatabase");
        }

        public void AddLine(int basketId, int siteId, int productId, int quantity, int? customerGroupId)
        {
            _sqlDataAccess.SaveData("dbo.spBasket_AddLine", new
            {
                BasketId = basketId,
                SiteId = siteId,
                ProductId = productId,
                Quantity = quantity,
                CustomerGroupId = customerGroupId
            }, "SMDatabase");
        }

        public bool SetQuantity(int basketId, int siteId, int productId, int quantity)
        {
            // The procedure reports its row count, so "nothing to change" is distinguishable
            // from "changed". An output parameter could not carry it — SaveData passes an
            // anonymous object and Dapper cannot write back through one.
            return _sqlDataAccess.LoadData<int, dynamic>("dbo.spBasket_SetQuantity", new
            {
                BasketId = basketId,
                SiteId = siteId,
                ProductId = productId,
                Quantity = quantity
            }, "SMDatabase").FirstOrDefault() > 0;
        }

        public int? ClaimBasket(int siteId, string token, int contactId)
        {
            return _sqlDataAccess.LoadData<int?, dynamic>("dbo.spBasket_Claim", new
            {
                SiteId = siteId,
                Token = token,
                ContactId = contactId
            }, "SMDatabase").FirstOrDefault();
        }
    }
}
