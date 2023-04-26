using Microsoft.Extensions.Configuration;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using PurchaseModel = SMDataManager.Library.Models.PurchaseModel;

namespace SMDataManager.Library.DataAccess
{
    public class PurchaseData : IPurchaseData
    {
        private readonly IProductData _productData;
        private readonly ISqlDataAccess _sqlDataAccess;
		private readonly IConfiguration _config;

		public PurchaseData(IProductData productData, ISqlDataAccess sqlDataAccess, IConfiguration config)
        {
            _productData = productData;
            _sqlDataAccess = sqlDataAccess;
			_config = config;
		}
        public void SavePurchases(PurchaseModel purchaseInfo, string staffId)
        {
            // Deposit the PurchaseDetailModel that are going to be saved in the database
            List<PurchaseDetailDBModel> details = new List<PurchaseDetailDBModel>();
            var taxRate = _config.GetValue<decimal>("taxRate") / 100;

            foreach (var item in purchaseInfo.PurchaseDetails)
            {
                var detail = new PurchaseDetailDBModel
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                };

                // Get the information about this product
                var productInfo = _productData.GetProductById(detail.ProductId);
                if (productInfo == null)
                {
                    throw new Exception($"The Product ID of {item.ProductId} could not be found in the database");
                }
                detail.PurchasePrice = (productInfo.RetailPrice * detail.Quantity);

                if (productInfo.IsTaxable)
                {
                    detail.VAT = (detail.PurchasePrice * taxRate);
                }

                details.Add(detail);
            }

            // Create a PurchaseModel
            PurchaseDBModel purchase = new PurchaseDBModel
            {
                SubTotal = details.Sum(x => x.PurchasePrice),
                VAT = details.Sum(x => x.VAT),
                StaffId = staffId
            };

            purchase.FinalPrice = purchase.SubTotal + purchase.VAT;

            try
            {
                _sqlDataAccess.StartTransaction("SMDataBase");

                // Save the PurchaseModel
                _sqlDataAccess.SaveDataInTransaction("dbo.spPurchase_Insert", purchase);

                // Get ID from PurchaseDBModel
                int purchaseId = _sqlDataAccess.LoadDataInTransaction<int, dynamic>("dbo.spPurchase_LookUp", new { purchase.StaffId, purchase.PurchaseDate }).FirstOrDefault();

                // Finish completing the PurchaseDetailsModel
                foreach (var item in details)
                {
                    item.PurchaseId = purchaseId;
                    // Save the PurchaseDetailsModel
                    _sqlDataAccess.SaveDataInTransaction("dbo.spPurchaseDetails_Insert", item);
                }

                _sqlDataAccess.CommitTransaction();
            }
            catch
            {
                _sqlDataAccess.RollbackTransaction();
                throw;
            }
        }

        public List<PurchaseReportModel> GetPurchaseReport()
        {
            var output = _sqlDataAccess.LoadData<PurchaseReportModel, dynamic>("spPurchase_PurchaseReport", new { }, "SMDatabase");

            return output;
        }
    }
}
