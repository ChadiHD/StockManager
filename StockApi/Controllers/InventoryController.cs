using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System.Data;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly InventoryData _inventoryData;

        public InventoryController(InventoryData inventoryData)
        {
            _inventoryData = inventoryData;
        }

        // The till's stock has no store, so an admin given only some stores does not reach it;
        // see AllStores.
        [Authorize(Roles = "Manager,Admin", Policy = Security.AllStores.Policy)]
        [HttpGet]
        public List<InventoryModel> Get()
        {
            return _inventoryData.GetInventory();
        }

        [Authorize(Roles = "Admin", Policy = Security.AllStores.Policy)]
        [HttpPost]
        public void Post(InventoryModel item)
        {
            _inventoryData.SaveInventoryData(item);
        }
    }
}
