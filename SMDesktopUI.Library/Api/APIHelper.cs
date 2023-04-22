using Microsoft.Extensions.Configuration;
using SMDesktopUI.Library.Helpers;
using SMDesktopUI.Library.Models;
using SMDesktopUI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Api
{
    public class APIHelper : IAPIHelper
    {
        // HttpClient is instantiated once for as long as runtime
        private HttpClient _apiClient;
        private ILoggedInUserModel _loggedInUser;
        private readonly IConfiguration _config;

        public APIHelper(ILoggedInUserModel loggedInUser, IConfiguration config)
        {
            _loggedInUser = loggedInUser;
            _config = config;
            InitializeClient();
        }

        public HttpClient ApiClient
        {
            get { return _apiClient; }
        }

        private void InitializeClient()
        {
            // App.config for api URL location
            string api = ConfigurationManager.AppSettings["api"];

            _apiClient = new HttpClient();
            _apiClient.BaseAddress = new Uri(api);
            _apiClient.DefaultRequestHeaders.Accept.Clear();
            _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<AuthenticatedUser> Authenticate(string username, string password)
        {
            var data = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            });
            using (HttpResponseMessage response = await _apiClient.PostAsync("/Token", data))
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsAsync<AuthenticatedUser>();
                    return result;
                }
                else
                {
                    throw new Exception(response.ReasonPhrase);
                }
            }
        }

        public void LogOff()
        {
            _apiClient.DefaultRequestHeaders.Clear();
        }

        // Creates calls that gets information about users
        public async Task GetLoggedInUserInfo(string token)
        {
            _apiClient.DefaultRequestHeaders.Clear();
            _apiClient.DefaultRequestHeaders.Accept.Clear();
            _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _apiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            using (HttpResponseMessage response = await _apiClient.GetAsync("/api/User"))
            {
                if (response.IsSuccessStatusCode )
                {
                    List<System.Net.Http.Formatting.MediaTypeFormatter> formatters = new List<System.Net.Http.Formatting.MediaTypeFormatter>
                    {
                        new System.Net.Http.Formatting.JsonMediaTypeFormatter(),
                        new VndApiJsonMediaTypeFormatter()
                    };
                    // Map the data between the interface and database
                    var result = await response.Content.ReadAsAsync<LoggedInUserModel>(formatters);
                    _loggedInUser.CreatedDate = result.CreatedDate;
                    _loggedInUser.EmailAddress = result.EmailAddress;
                    _loggedInUser.FirstName = result.FirstName;
                    _loggedInUser.LastName = result.LastName;
                    _loggedInUser.Id = result.Id;
                    _loggedInUser.Token = token;

                }
                else
                { 
                    throw new Exception(response.ReasonPhrase);
                }
            }
        }
    }
}
