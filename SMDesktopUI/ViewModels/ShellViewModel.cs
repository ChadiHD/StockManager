using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using SMDesktopUI.EventModels;
using SMDesktopUI.Library.Api;
using SMDesktopUI.Library.Models;

namespace SMDesktopUI.ViewModels
{
    public class ShellViewModel : Conductor<object>, IHandle<LogOnEvent>
    {
        private readonly IEventAggregator _events;
        private readonly ILoggedInUserModel _user;
        private readonly IAPIHelper _apiHelper;

        // Contructor dependency injection
        // all Subscribers are notified with all events in this form
        public ShellViewModel(IEventAggregator events,
                              ILoggedInUserModel user,
                              IAPIHelper apiHelper)
        {
            _events = events;
            _user = user;
            _apiHelper = apiHelper;

            _events.SubscribeOnPublishedThread(this);

            // Create a new instance of LoginView everytime it's called
            ActivateItemAsync(IoC.Get<LoginViewModel>(), new CancellationToken());
        }

        // Hide the logout button on Login page
        public bool IsLoggedIn
        {
            get
            {
                bool output = false;

                if (string.IsNullOrWhiteSpace(_user.Token) == false)
                {
                    output = true;
                }

                return output;
            }
        }

        public void ExitApplication()
        {
            TryCloseAsync();
        }

        public async Task UserManagement()
        {
            await ActivateItemAsync(IoC.Get<UserDisplayViewModel>(), new CancellationToken());
        }

        public async Task LogOut()
        {
            _user.ResetUserModel();
            // Clears the Token for security breach
            _apiHelper.LogOff();
            await ActivateItemAsync(IoC.Get<LoginViewModel>(), new CancellationToken());
            NotifyOfPropertyChange(() => IsLoggedIn);
        }

        public async Task HandleAsync(LogOnEvent message, CancellationToken cancellationToken)
        {
            await ActivateItemAsync(IoC.Get<SalesViewModel>(), cancellationToken);
            NotifyOfPropertyChange(() => IsLoggedIn);
        }
    }
}
