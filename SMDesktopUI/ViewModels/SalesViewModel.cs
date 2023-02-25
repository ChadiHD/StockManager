using Caliburn.Micro;
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
        private BindingList<string> _products;

		public BindingList<string> Products
		{
			get { return _products; }
            set
            {
                _products = value;
                NotifyOfPropertyChange(() => Products);
            }
        }

        private BindingList<string> _cart;

        public BindingList<string> Cart
        {
            get { return _cart; }
            set
            {
                _cart = value;
                NotifyOfPropertyChange(() => Cart);
            }
        }

        private string _itemQuantity;

        public string ItemQuantity
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
