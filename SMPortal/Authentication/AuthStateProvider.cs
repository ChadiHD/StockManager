using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using SMDesktopUI.Library.Api;
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
		private readonly IAPIHelper _apiHelper;
		private readonly AuthenticationState _anonymous;

        public AuthStateProvider(HttpClient httpClient,
                                 ILocalStorageService localStorge,
                                 IConfiguration config,
                                 IAPIHelper apiHelper)
        {
            _httpClient = httpClient;
            _localStorge = localStorge;
            _config = config;
			_apiHelper = apiHelper;
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

            bool isAuthenticated = await MarkUserAsAuthenticated(token);

            if (isAuthenticated == false)
            {
				return _anonymous;
			}

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);

            return new AuthenticationState(
                new ClaimsPrincipal(
                    new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token),
                            "jwtAuthType")));
        }

        public async Task<bool> MarkUserAsAuthenticated(string token)
        {
            bool isAuthenticatedOutput = false;
            Task<AuthenticationState> authState;
            try
            {
                await _apiHelper.GetLoggedInUserInfo(token);
				var authenticatedUser = new ClaimsPrincipal(
				new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token),
					"jwtAuthType"));
				authState = Task.FromResult(new AuthenticationState(authenticatedUser));
				NotifyAuthenticationStateChanged(authState);
                isAuthenticatedOutput = true;

			}
            catch (Exception ex)
            {
                Console.WriteLine(ex);
				await MarkUserAsLoggedOut();
                isAuthenticatedOutput = false;
			}

            return isAuthenticatedOutput;
		}

        public async Task MarkUserAsLoggedOut()
        {
			// Grab the token information
			string authTokenStorageKey = _config["authTokenStorageKey"];
			await _localStorge.RemoveItemAsync(authTokenStorageKey);
			var authState = Task.FromResult(_anonymous);
            _apiHelper.LogOff();
			_httpClient.DefaultRequestHeaders.Authorization = null;
			NotifyAuthenticationStateChanged(authState);
        }
    }
}
