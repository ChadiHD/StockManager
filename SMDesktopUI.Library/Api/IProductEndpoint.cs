using SMDesktopUI.Library.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public interface IProductEndpoint
    {
        Task<List<ProductModel>> GetAll();
    }
}