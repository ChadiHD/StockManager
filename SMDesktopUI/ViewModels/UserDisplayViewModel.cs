using AutoMapper;
using Caliburn.Micro;
using SMDesktopUI.Library.Api;
using SMDesktopUI.Library.Models;
using SMDesktopUI.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SMDesktopUI.ViewModels
{
    public class UserDisplayViewModel : Screen
    {
        private readonly IUserEndpoint _userEndpoint;
        private readonly StatusInfoViewModel _status;
        private readonly IWindowManager _window;
        private BindingList<UserModel> _users;

        public BindingList<UserModel> Users
        { 
            get
            {
                return _users;
            }
            set
            {
                _users = value;
                NotifyOfPropertyChange(() => Users);
            }
        }

        public UserDisplayViewModel(IUserEndpoint userEndpoint, 
            StatusInfoViewModel status, 
            IWindowManager window)
        {
            _userEndpoint = userEndpoint;
            _status = status;
            _window = window;
        }

        protected override async void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            try
            {
                await LoadUsers();
            }
            catch (Exception ex)
            {
                // Message box template for catching exception and showing to the user
                dynamic settings = new ExpandoObject();
                settings.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                settings.ResizeMode = ResizeMode.NoResize;
                settings.Title = "Error";

                if (ex.Message == "Unauthorized")
                {
                    _status.UpdateMessage("Unauthorised Access", "Access Denied to interact with the sales page");
                    _window.ShowDialog(_status, null, settings);
                }
                else
                {
                    _status.UpdateMessage("Expection Error", ex.Message);
                    _window.ShowDialog(_status, null, settings);
                }
                TryClose();
            }
        }

        private async Task LoadUsers()
        {
            var userList = await _userEndpoint.GetAll();
            Users = new BindingList<UserModel>(userList);
        }
    }
}
