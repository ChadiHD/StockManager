using SMDesktopUI.Library.Models;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public interface IPurchaseEndpoint
    {
        Task PostPurchase(PurchaseModel purchase);
    }
}