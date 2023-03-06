using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using SMDesktopUI.Library;
using SMDesktopUI.Library.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
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

            // Save the PurchaseModel
            SqlDataAccess sql = new SqlDataAccess();
            sql.SaveData<PurchaseDBModel>("dbo.spPurchase_Insert", purchase, "SMDatabase");

            // Get ID from PurchaseDBModel
            int purchaseId = sql.LoadData<int, dynamic>("dbo.spPurchase_LookUp", new { purchase.StaffId, purchase.PurchaseDate }, "SMDatabase").FirstOrDefault();

            // Finish completing the PurchaseDetailsModel
            foreach (var item in details)
            {
                item.PurchaseId = purchaseId;
                // Save the PurchaseDetailsModel
                sql.SaveData("dbo.spPurchaseDetails_Insert", item, "SMDatabase");
            }
        }
    }
}
