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

        [Authorize(Roles = "Manager,Admin")]
        [HttpGet]
        public List<InventoryModel> Get()
        {
            return _inventoryData.GetInventory();
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public void Post(InventoryModel item)
        {
            _inventoryData.SaveInventoryData(item);
        }
    }
}
