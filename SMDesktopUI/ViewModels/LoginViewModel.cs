using Caliburn.Micro;
using SMDesktopUI.Helpers;
using SMDesktopUI.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SMDesktopUI.Library.Api;
using SMDesktopUI.EventModels;

namespace SMDesktopUI.ViewModels
{
	public class LoginViewModel : Screen
	{
		private string _userName = string.Empty;
		private string _password = string.Empty;
		private readonly IAPIHelper _apiHelper;
		private readonly IEventAggregator _events;

		// Dependency Injection
        public LoginViewModel(IAPIHelper apiHelper, IEventAggregator events)
        {
            _apiHelper = apiHelper;
			_events = events;
        }

        public string UserName
		{
			get { return _userName; }
			set
			{
				_userName = value;
				NotifyOfPropertyChange(() => UserName);
            }
		}
		public string Password
		{
			get { return _password; }
			set
			{
				_password = value;
				NotifyOfPropertyChange(() => Password);
            }
		}

		public bool IsErrorVisible
        {
			get 
			{
				bool output = false;
				if (ErrorMessage?.Length > 0)
				{
					output = true;
				}
				return output;
			}
		}

		private string _errorMessage;

		public string ErrorMessage
		{
			get { return _errorMessage; }
			set 
			{
                _errorMessage = value;
                NotifyOfPropertyChange(() => IsErrorVisible);
                NotifyOfPropertyChange(() => ErrorMessage);
			}
		}

		// The Sign in button is always enabled; input is validated here on click and any
		// problem is surfaced through ErrorMessage instead of silently disabling the button.
		public async Task LogIn()
		{
			ErrorMessage = string.Empty;

			string validationError = ValidateForm();
			if (validationError is not null)
			{
				ErrorMessage = validationError;
				return;
			}

			try
			{
				var result = await _apiHelper.Authenticate(UserName, Password);

				// Get more information about the user
				await _apiHelper.GetLoggedInUserInfo(result.Access_Token);

				// Publish the UI on an empty class to differentiate it from other events
				await _events.PublishOnUIThreadAsync(new LogOnEvent(), new CancellationToken());
			}
			catch (Exception ex)
			{
				// Authenticate / GetLoggedInUserInfo throw a plain Exception on a bad login or an
				// unreachable API; show it rather than letting it crash the app.
				ErrorMessage = ex.Message;
			}
		}

		private string ValidateForm()
		{
			if (string.IsNullOrWhiteSpace(UserName))
			{
				return "Enter your email address.";
			}

			if (!new EmailAddressAttribute().IsValid(UserName))
			{
				return "Enter a valid email address.";
			}

			if (string.IsNullOrWhiteSpace(Password))
			{
				return "Enter your password.";
			}

			return null;
		}
	}
}
