using SMDesktopUI.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public class PurchaseEndpoint : IPurchaseEndpoint
    {
        private IAPIHelper _apiHelper;
        public PurchaseEndpoint(IAPIHelper apiHelper)
        {
            _apiHelper = apiHelper;
        }

        public async Task PostPurchase(PurchaseModel purchase)
        {
            using HttpResponseMessage response = await _apiHelper.ApiClient.PostAsJsonAsync("/api/Purchase", purchase);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(response.ReasonPhrase);
            }
        }

        public async Task<List<PurchaseReportModel>> GetPurchaseReport()
        {
            using HttpResponseMessage response =
                await _apiHelper.ApiClient.GetAsync("/api/Purchase/GetPurchaseReport");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(response.ReasonPhrase);
            }

            return await response.Content.ReadFromJsonAsync<List<PurchaseReportModel>>() ?? new();
        }
    }
}
