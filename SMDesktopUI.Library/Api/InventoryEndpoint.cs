using SMDesktopUI.Library.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public class InventoryEndpoint : IInventoryEndpoint
    {
        private readonly IAPIHelper _apiHelper;

        public InventoryEndpoint(IAPIHelper apiHelper)
        {
            _apiHelper = apiHelper;
        }

        public async Task<List<InventoryModel>> GetAll()
        {
            using HttpResponseMessage response = await _apiHelper.ApiClient.GetAsync("/api/Inventory");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(response.ReasonPhrase);
            }

            return await response.Content.ReadFromJsonAsync<List<InventoryModel>>() ?? new();
        }

        public async Task Save(InventoryModel item)
        {
            using HttpResponseMessage response = await _apiHelper.ApiClient.PostAsJsonAsync("/api/Inventory", item);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(response.ReasonPhrase);
            }
        }
    }
}
