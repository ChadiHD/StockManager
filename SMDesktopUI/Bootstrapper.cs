using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Caliburn.Micro;
using SMDesktopUI.Helpers;
using SMDesktopUI.Library.Api;
using SMDesktopUI.Library.Models;
using SMDesktopUI.Models;
using SMDesktopUI.ViewModels;
using SMDesktopUI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace SMDesktopUI
{
    public class Bootstrapper : BootstrapperBase
    {
        // Dependency injection container system build with CaliburnMicro
        private readonly SimpleContainer _container = new();

        public Bootstrapper() 
        {
            Initialize();

            ConventionManager.AddElementConvention<PasswordBox>(
                PasswordBoxHelper.BoundPasswordProperty,
                "Password",
                "PasswordChanged");
        }

        private static IMapper ConfigureAutoMapper()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<ProductModel, ProductDisplayModel>();
                cfg.CreateMap<CartItemModel, CartItemDisplayModel>();
            }, NullLoggerFactory.Instance);

            var output = config.CreateMapper();

            return output;
        }

        private static IConfiguration AddConfiguration()
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

#if DEBUG
            builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
#else
            builder.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true);
#endif
            return builder.Build();
        }

        // The container contains an instance of itself to pass it out when requested
        protected override void Configure()
        {
            // Add the mapper to the container
            _container.Instance(ConfigureAutoMapper());

            _container.Instance(_container)
                .PerRequest<IProductEndpoint, ProductEndpoint>()
                .PerRequest<IPurchaseEndpoint, PurchaseEndpoint>()
                .PerRequest<IUserEndpoint, UserEndpoint>();

            _container
                .Singleton<IWindowManager, WindowManager>()
                .Singleton<IEventAggregator, EventAggregator>()
                .Singleton<ILoggedInUserModel, LoggedInUserModel>()
                .Singleton<IAPIHelper, APIHelper>()
                .Singleton<ILabelPrintService, WindowsLabelPrintService>();

            _container.RegisterInstance(typeof(IConfiguration), "IConfiguration", AddConfiguration());

            // Connect ViewModels to Views (slow performance)
            GetType().Assembly.GetTypes()
                .Where(type => type.IsClass)
                .Where(type => type.Name.EndsWith("ViewModel"))
                .ToList()
                .ForEach(viewModelType => _container.RegisterPerRequest(
                    viewModelType, viewModelType.ToString(), viewModelType));
        }

        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            DisplayRootViewForAsync<ShellViewModel>();
        }

        // Pass a head type and use the container to instantiate the ShellViewModel
        protected override object GetInstance(Type service, string key)
        {
            return _container.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            _container.BuildUp(instance);
        }
    }
}
