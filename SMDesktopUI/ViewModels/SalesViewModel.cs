using Caliburn.Micro;
using SMDesktopUI.Library.Api;
using SMDesktopUI.Library.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDesktopUI.ViewModels
{
    public class SalesViewModel : Screen
    {
        IProductEndpoint _productEndpoint;

        public SalesViewModel(IProductEndpoint productEndpoint)
        {
            _productEndpoint = productEndpoint;
        }

        protected override async void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            await LoadProducts(); 
        }

        // A constructor doesn't return Tasks therefore this function is created
        // To return the values from the constructor
        private async Task LoadProducts()
        {
            var productList = await _productEndpoint.GetAll();
            Products = new BindingList<ProductModel>(productList);
        }

        private BindingList<ProductModel> _products;

		public BindingList<ProductModel> Products
		{
			get { return _products; }
            set
            {
                _products = value;
                NotifyOfPropertyChange(() => Products);
            }
        }

        private BindingList<ProductModel> _cart;

        public BindingList<ProductModel> Cart
        {
            get { return _cart; }
            set
            {
                _cart = value;
                NotifyOfPropertyChange(() => Cart);
            }
        }

        private int _itemQuantity;

        public int ItemQuantity
        {
            get { return _itemQuantity; }
            set
            {
                _itemQuantity = value;
                NotifyOfPropertyChange(() => ItemQuantity);
            }
        }

        public string SubTotal
        {
            get
            {
                // TODO - Create the calculations
                return "£0.00";
            }
        }

        public string VAT
        {
            get
            {
                // TODO - Create the calculations
                return "£0.00";
            }
        }

        public string FinalPrice
        {
            get
            {
                // TODO - Create the calculations
                return "£0.00";
            }
        }

        public bool CanAddToCart
        {
            get
            {
                bool output = false;
                
                // Validate if something is selected 

                // Validate of there is an item in cart

                return output;
            }
        }

        public void AddToCart()
        {

        }

        public bool CanRemoveFromCart
        {
            get
            {
                bool output = false;

                // Validate if something is selected 

                return output;
            }
        }

        public void RemoveFromCart()
        {

        }

        public bool CanCheckOut
        {
            get
            {
                bool output = false;

                // Validate if there is an item in cart

                return output;
            }
        }

        public void CheckOut()
        {

        }
    }
}
