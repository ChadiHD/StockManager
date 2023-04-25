using Caliburn.Micro;
using SMDesktopUI.Helpers;
using SMDesktopUI.Models;
using System;
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
		private string _userName = "chadi.hammoud@gmail.com";
		private string _password = "Pwd12345.";
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
                NotifyOfPropertyChange(() => CanLogIn);
            }
		}
		public string Password
		{
			get { return _password; }
			set
			{
				_password = value;
				NotifyOfPropertyChange(() => Password);
                NotifyOfPropertyChange(() => CanLogIn);
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

		public bool CanLogIn
		{
			get
			{
                bool output = false;

                if (UserName?.Length > 0 && Password?.Length > 0)
                {
                    output = true;
                }

                return output;
            }
		}

		// TODO - call the method from IAPIHelper
		public async Task LogIn()
		{
			try
			{
				ErrorMessage = string.Empty;
				var result = await _apiHelper.Authenticate(UserName, Password);

				// Get more information about the user
				await _apiHelper.GetLoggedInUserInfo(result.Access_Token);

				// Publish the UI on an empty class to differentiate it from other events
				await _events.PublishOnUIThreadAsync(new LogOnEvent(), new CancellationToken());
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}
}
