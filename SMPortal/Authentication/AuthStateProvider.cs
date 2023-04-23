using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace SMPortal.Authentication
{
    public class AuthStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorge;
        private readonly IConfiguration _config;
        private readonly AuthenticationState _anonymous;

        public AuthStateProvider(HttpClient httpClient,
                                 ILocalStorageService localStorge,
                                 IConfiguration config)
        {
            _httpClient = httpClient;
            _localStorge = localStorge;
            _config = config;
            _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            string authTokenStorageKey = _config["authTokenStorageKey"];
            var token = await _localStorge.GetItemAsync<string>(authTokenStorageKey);

            if (string.IsNullOrWhiteSpace(token))
            {
                return _anonymous;
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);

            return new AuthenticationState(
                new ClaimsPrincipal(
                    new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token),
                            "jwtAuthType")));
        }

        public void MarkUserAsAuthenticated(string token)
        {
            var authenticatedUser = new ClaimsPrincipal(
                new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token),
                    "jwtAuthType"));
            var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
            NotifyAuthenticationStateChanged(authState);
        }

        public void MarkUserAsLoggedOut()
        {
            var authState = Task.FromResult(_anonymous);
            NotifyAuthenticationStateChanged(authState);
        }
    }
}
