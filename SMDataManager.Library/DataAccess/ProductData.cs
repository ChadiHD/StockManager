using Microsoft.Extensions.Configuration;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.DataAccess
{
    public class ProductData : IProductData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public ProductData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }
        public List<ProductModel> GetProducts()
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetAll", new { }, "SMDatabase");

            return output;
        }

        public ProductModel GetProductById(int productId)
        {
            var output = _sqlDataAccess.LoadData<ProductModel, dynamic>("dbo.spProduct_GetById", new { Id = productId }, "SMDatabase").FirstOrDefault();

            return output;
        }
    }
}
