using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using SMPortal.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SMPortal.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly HttpClient _client;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly ILocalStorageService _localStorage;
        private readonly IConfiguration _config;
        private readonly string authTokenStorageKey;

        public AuthenticationService(HttpClient client,
                                     AuthenticationStateProvider authStateProvider,
                                     ILocalStorageService localStorage,
                                     IConfiguration config)
        {
            _client = client;
            _authStateProvider = authStateProvider;
            _localStorage = localStorage;
            _config = config;
            authTokenStorageKey = _config["authTokenStorageKey"]
                ?? throw new InvalidOperationException("The authTokenStorageKey configuration value is required.");
        }

        public async Task<AuthenticatedUserModel?> Login(AuthenticationUserModel userForAuthentication)
        {
            var data = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", userForAuthentication.Email ?? string.Empty),
                new KeyValuePair<string, string>("password", userForAuthentication.Password ?? string.Empty)
            });

            string api = (_config["api"] ?? throw new InvalidOperationException("The api configuration value is required."))
                + (_config["tokenEndPoint"] ?? throw new InvalidOperationException("The tokenEndPoint configuration value is required."));
            var authResult = await _client.PostAsync(api, data);
            var authContent = await authResult.Content.ReadAsStringAsync();

            if (authResult.IsSuccessStatusCode == false)
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<AuthenticatedUserModel>(
                authContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (string.IsNullOrWhiteSpace(result?.Access_Token))
            {
                return null;
            }

            await _localStorage.SetItemAsync(authTokenStorageKey, result.Access_Token);

            var isAuthenticated = await ((AuthStateProvider)_authStateProvider).MarkUserAsAuthenticated(result.Access_Token);
            if (!isAuthenticated)
            {
                return null;
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Access_Token);

            return result;
        }

        public async Task Logout()
        {
            await ((AuthStateProvider)_authStateProvider).MarkUserAsLoggedOut();
        }
    }
}
