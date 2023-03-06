using SMDesktopUI.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            using (HttpResponseMessage response = await _apiHelper.ApiClient.PostAsJsonAsync("/api/Purchase", purchase))
            {
                if (response.IsSuccessStatusCode)
                {
                    // create a successful call

                }
                else
                {
                    throw new Exception(response.ReasonPhrase);
                }
            }
        }
    }
}
