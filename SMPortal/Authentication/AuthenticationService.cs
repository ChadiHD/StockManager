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

        public AuthenticationService(HttpClient client,
                                     AuthenticationStateProvider authStateProvider,
                                     ILocalStorageService localStorage)
        {
            _client = client;
            _authStateProvider = authStateProvider;
            _localStorage = localStorage;
        }

        public async Task<AuthenticatedUserModel> Login(AuthenticationUserModel userForAuthentication)
        {
            var data = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", userForAuthentication.Email),
                new KeyValuePair<string, string>("password", userForAuthentication.Password)
            });

            var authResult = await _client.PostAsync("https://localhost:5001/token", data);
            var authContent = await authResult.Content.ReadAsStringAsync();

            if (authResult.IsSuccessStatusCode == false)
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<AuthenticatedUserModel>
                (
                authContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

            await _localStorage.SetItemAsync("authToken", result.Access_Token);

            ((AuthStateProvider)_authStateProvider).MarkUserAsAuthenticated(result.Access_Token);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Access_Token);

            return result;
        }

        public async Task Logut()
        {
            await _localStorage.RemoveItemAsync("authToken");

            ((AuthStateProvider)_authStateProvider).MarkUserAsLoggedOut();

            _client.DefaultRequestHeaders.Authorization = null;
        }
    }
}
