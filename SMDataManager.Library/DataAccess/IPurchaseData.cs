using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface IPurchaseData
    {
        List<PurchaseReportModel> GetPurchaseReport();
        void SavePurchases(PurchaseModel purchaseInfo, string staffId);
    }
}