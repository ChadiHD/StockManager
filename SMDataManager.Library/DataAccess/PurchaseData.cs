using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using SMDesktopUI.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using PurchaseModel = SMDataManager.Library.Models.PurchaseModel;

namespace SMDataManager.Library.DataAccess
{
    public class PurchaseData
    {
        public void SavePurchases(PurchaseModel purchaseInfo, string staffId)
        {
            // Deposit the PurchaseDetailModel that are going to be saved in the database
            List<PurchaseDetailDBModel> details = new List<PurchaseDetailDBModel>();
            ProductData products = new ProductData();
            var taxRate = ConfigHelper.GetTaxRate() / 100;

            foreach (var item in purchaseInfo.PurchaseDetails)
            {
                var detail = new PurchaseDetailDBModel
                {
                    ProductId= item.ProductId,
                    Quantity= item.Quantity,
                };

                // Get the information about this product
                var productInfo = products.GetProductById(detail.ProductId);
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

            using (SqlDataAccess sql = new SqlDataAccess())
            {
                try
                {
                    sql.StartTransaction("SMDataBase");

                    // Save the PurchaseModel
                    sql.SaveDataInTransaction("dbo.spPurchase_Insert", purchase);

                    // Get ID from PurchaseDBModel
                    int purchaseId = sql.LoadDataInTransaction<int, dynamic>("dbo.spPurchase_LookUp", new { purchase.StaffId, purchase.PurchaseDate }).FirstOrDefault();

                    // Finish completing the PurchaseDetailsModel
                    foreach (var item in details)
                    {
                        item.PurchaseId = purchaseId;
                        // Save the PurchaseDetailsModel
                        sql.SaveDataInTransaction("dbo.spPurchaseDetails_Insert", item);
                    }

                    sql.CommitTransaction();
                }
                catch
                {
                    sql.RollbackTransaction();
                    throw;
                }
            }
        }

        public List<PurchaseReportModel> GetPurchaseReport()
        {
            SqlDataAccess sql = new SqlDataAccess();

            var output = sql.LoadData<PurchaseReportModel, dynamic>("spPurchase_PurchaseReport", new {}, "SMDatabase");

            return output;
        }
    }
}
