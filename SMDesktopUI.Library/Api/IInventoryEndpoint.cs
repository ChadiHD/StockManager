using SMDesktopUI.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public interface IInventoryEndpoint
    {
        Task<List<InventoryModel>> GetAll();
        Task Save(InventoryModel item);
    }
}
