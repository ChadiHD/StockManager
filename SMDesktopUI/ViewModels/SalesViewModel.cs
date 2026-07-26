using AutoMapper;
using Caliburn.Micro;
using Microsoft.Extensions.Configuration;
using SMDesktopUI.Library.Api;
using SMDesktopUI.Library.Models;
using SMDesktopUI.Models;
using SMDesktopUI.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SMDesktopUI.ViewModels
{
    public class SalesViewModel : Screen
    {
        readonly IProductEndpoint _productEndpoint;
        readonly IPurchaseEndpoint _purchaseEndpoint;
		private readonly IConfiguration _config;
        readonly IMapper _mapper;
        private readonly StatusInfoViewModel _status;
        private readonly IWindowManager _window;
        private readonly ILabelPrintService _labelPrintService;

        public SalesViewModel
            (IProductEndpoint productEndpoint, 
            IPurchaseEndpoint purchaseEndpoint, 
            IConfiguration config,
            IMapper mapper,
            StatusInfoViewModel status,
            IWindowManager window,
            ILabelPrintService labelPrintService)
        {
            _productEndpoint = productEndpoint;
            _purchaseEndpoint = purchaseEndpoint;
			_config = config;
            _mapper = mapper;
            _status = status;
            _window = window;
            _labelPrintService = labelPrintService;
        }

        protected override async void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            try
            {
                await LoadProducts();
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
                    await _window.ShowDialogAsync(_status, null, settings); 
                }
                else
                {
                    _status.UpdateMessage("Expection Error", ex.Message);
                    await _window.ShowDialogAsync(_status, null, settings);
                }
                await TryCloseAsync();
            }
        }

        // A constructor doesn't return Tasks therefore this function is created
        // To return the values from the constructor
        private async Task LoadProducts()
        {
            var productList = await _productEndpoint.GetAll();
            var products = _mapper.Map<List<ProductDisplayModel>>(productList);
            Products = new BindingList<ProductDisplayModel>(products);
        }

        private BindingList<ProductDisplayModel> _products;

		public BindingList<ProductDisplayModel> Products
		{
			get { return _products; }
            set
            {
                _products = value;
                NotifyOfPropertyChange(() => Products);
            }
        }

        private ProductDisplayModel _selectedProduct;

        public ProductDisplayModel SelectedProduct
        {
            get { return _selectedProduct; }
            set 
            { 
                _selectedProduct = value;
                NotifyOfPropertyChange(() => SelectedProduct);
                NotifyOfPropertyChange(() => CanAddToCart);
                QuantityMessage = string.Empty;
            }
        }

        private async Task ResetSalesViewModel()
        {
            Cart = new BindingList<CartItemDisplayModel>();
            SelectedCartItem = null;
            SelectedProduct = null;
            await LoadProducts();

            NotifyOfPropertyChange(() => SubTotal);
            NotifyOfPropertyChange(() => VAT);
            NotifyOfPropertyChange(() => FinalPrice);
            NotifyOfPropertyChange(() => CanCheckOut);
        }

        private CartItemDisplayModel _selectedCartItem;

        public CartItemDisplayModel SelectedCartItem
        {
            get { return _selectedCartItem; }
            set
            {
                _selectedCartItem = value;
                NotifyOfPropertyChange(() => SelectedCartItem);
                NotifyOfPropertyChange(() => CanRemoveFromCart);
                NotifyOfPropertyChange(() => CanPrintSelectedLabel);
                LabelPreview = value?.LabelContent ?? LastCompletedLabelContent;
            }
        }


        private BindingList<CartItemDisplayModel> _cart = new();

        public BindingList<CartItemDisplayModel> Cart
        {
            get { return _cart; }
            set
            {
                _cart = value;
                NotifyOfPropertyChange(() => Cart);
            }
        }

        private int _itemQuantity = 1;

        public int ItemQuantity
        {
            get { return _itemQuantity; }
            set
            {
                _itemQuantity = value;
                NotifyOfPropertyChange(() => ItemQuantity);
                NotifyOfPropertyChange(() => CanAddToCart);
                QuantityMessage = value <= 0 ? "Enter a quantity of one or more." :
                    SelectedProduct != null && value > SelectedProduct.QuantityInStock
                        ? $"Only {SelectedProduct.QuantityInStock} units are available."
                        : string.Empty;
            }
        }

        private string _quantityMessage = string.Empty;
            public string QuantityMessage
            {
                get => _quantityMessage;
                private set
                {
                    _quantityMessage = value;
                    NotifyOfPropertyChange(() => QuantityMessage);
                    NotifyOfPropertyChange(() => HasQuantityMessage);
                }
            }

            public bool HasQuantityMessage => !string.IsNullOrWhiteSpace(QuantityMessage);

            private string _scanInput = string.Empty;
            public string ScanInput
            {
                get => _scanInput;
                set
                {
                    _scanInput = value;
                    NotifyOfPropertyChange(() => ScanInput);
                }
            }

            private string _scanFeedback = "Ready for a barcode, SKU, product ID, or exact product name.";
            public string ScanFeedback
            {
                get => _scanFeedback;
                private set
                {
                    _scanFeedback = value;
                    NotifyOfPropertyChange(() => ScanFeedback);
                }
            }

            private string _labelPreview = "Select a cart item to preview its label.";
            public string LabelPreview
            {
                get => _labelPreview;
                private set
                {
                    _labelPreview = value;
                    NotifyOfPropertyChange(() => LabelPreview);
                    NotifyOfPropertyChange(() => CanPrintLastSaleLabels);
                }
            }

            private string LastCompletedLabelContent { get; set; } = string.Empty;

            private string _printFeedback = string.Empty;
            public string PrintFeedback
            {
                get => _printFeedback;
                private set
                {
                    _printFeedback = value;
                    NotifyOfPropertyChange(() => PrintFeedback);
                    NotifyOfPropertyChange(() => HasPrintFeedback);
                }
            }

            public bool HasPrintFeedback => !string.IsNullOrWhiteSpace(PrintFeedback);

            public void CommitScan()
            {
                var value = ScanInput?.Trim();
                ScanInput = string.Empty;

                if (string.IsNullOrWhiteSpace(value))
                {
                    ScanFeedback = "Not found — enter or scan a product identifier.";
                    return;
                }

                var match = Products?.FirstOrDefault(product =>
                    product.Id.ToString() == value ||
                    string.Equals(product.ProductName, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(product.Description, value, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    SelectedProduct = null;
                    ScanFeedback = $"Not found — “{value}” does not match an available product.";
                    return;
                }

                SelectedProduct = match;
                ItemQuantity = 1;
                if (!CanAddToCart)
                {
                    ScanFeedback = $"Found {match.ProductName}, but it is out of stock.";
                    return;
                }

                AddToCart();
                ScanFeedback = $"Added 1 × {match.ProductName}. Ready for the next scan.";
            }

        public string SubTotal
        {
            get
            {
                return CalculateSubTotal().ToString("C");
            }
        }

        private decimal CalculateSubTotal()
        {
            decimal subTotal = 0;
            foreach (var item in Cart)
            {
                subTotal += (item.Product.RetailPrice * item.QuantityInCart);
            }

            return subTotal;
        }

        private decimal CalculateVAT()
        {
            decimal taxAmount = 0;
            decimal taxRate = _config.GetValue<decimal>("taxRate")/100;

            taxAmount = Cart.Where(x => x.Product.IsTaxable)
                .Sum(x => x.Product.RetailPrice * x.QuantityInCart * taxRate);

            return taxAmount;
        }

        public string VAT
        {
            get
            {
                return CalculateVAT().ToString("C");
            }
        }

        public string FinalPrice
        {
            get
            {
                decimal finalPrice = CalculateSubTotal() + CalculateVAT();
                return finalPrice.ToString("C");
            }
        }

        public bool CanAddToCart
        {
            get
            {
                bool output = false;
                
                // Validate if something is selected 
                // Validate if there is an item in cart
                if (ItemQuantity > 0 && SelectedProduct?.QuantityInStock >= ItemQuantity)
                {
                    output = true;
                }

                return output;
            }
        }

        public void AddToCart()
        {
            if (!CanAddToCart || SelectedProduct == null)
            {
                QuantityMessage = SelectedProduct == null
                    ? "Select a product before adding it to the cart."
                    : ItemQuantity <= 0
                        ? "Enter a quantity of one or more."
                        : $"Only {SelectedProduct.QuantityInStock} units are available.";
                return;
            }

            CartItemDisplayModel existingItem = Cart.FirstOrDefault(x => x.Product == SelectedProduct);
            if (existingItem != null)
            {
                existingItem.QuantityInCart += ItemQuantity;
                if (ReferenceEquals(SelectedCartItem, existingItem))
                {
                    LabelPreview = existingItem.LabelContent;
                }
            }
            else
            {
                CartItemDisplayModel item = new()
                {
                    Product = SelectedProduct,
                    QuantityInCart = ItemQuantity
                };
                Cart.Add(item);
            }
            
            SelectedProduct.QuantityInStock -= ItemQuantity;
            ItemQuantity = 1;
            QuantityMessage = string.Empty;
            NotifyOfPropertyChange(() => SubTotal);
            NotifyOfPropertyChange(() => VAT);
            NotifyOfPropertyChange(() => FinalPrice);
            NotifyOfPropertyChange(() => CanCheckOut);
            NotifyOfPropertyChange(() => CanPrintSelectedLabel);
        }

        public bool CanRemoveFromCart
        {
            get
            {
                bool output = false;

                // Validate if something is selected
                if (SelectedCartItem != null && SelectedCartItem?.QuantityInCart > 0)
                {
                    output = true;
                }

                return output;
            }
        }

        public void RemoveFromCart()
        {
            if (!CanRemoveFromCart || SelectedCartItem?.Product == null)
            {
                return;
            }

            var item = SelectedCartItem;
            item.Product.QuantityInStock += 1;

            if (item.QuantityInCart > 1)
            {
                item.QuantityInCart -= 1;
            }
            else
            {
                Cart.Remove(item);
                SelectedCartItem = null;
            }
            NotifyOfPropertyChange(() => SubTotal);
            NotifyOfPropertyChange(() => VAT);
            NotifyOfPropertyChange(() => FinalPrice);
            NotifyOfPropertyChange(() => CanCheckOut);
            NotifyOfPropertyChange(() => CanAddToCart);
        }

        public bool CanCheckOut
        {
            get
            {
                bool output = false;

                // Validate if there is an item in cart
                if (Cart.Count > 0)
                {
                    output = true;
                }

                return output;
            }
        }

        public async Task CheckOut()
        {
            if (!CanCheckOut)
            {
                return;
            }

            // Create a SaleModel that is connected to the API
            PurchaseModel sale = new();

            foreach (var item in Cart)
            {
                sale.PurchaseDetails.Add(new PurchaseDetailModel
                {
                    ProductId = item.Product.Id,
                    Quantity = item.QuantityInCart
                });
            }
            
            var completedLabelContent = string.Join(
                "\n\n————————————\n\n",
                Cart.Select(item => item.LabelContent));

            await _purchaseEndpoint.PostPurchase(sale);

            await ResetSalesViewModel();
            LastCompletedLabelContent = completedLabelContent;
            LabelPreview = completedLabelContent;
            PrintFeedback = "Checkout complete. Review the label preview, then choose Print sale labels.";
            NotifyOfPropertyChange(() => CanPrintLastSaleLabels);
        }

        public bool CanPrintSelectedLabel => SelectedCartItem?.QuantityInCart > 0;

        public void PrintSelectedLabel()
        {
            if (!CanPrintSelectedLabel)
            {
                PrintFeedback = "Select a valid cart item before printing a label.";
                return;
            }

            LabelPreview = SelectedCartItem.LabelContent;
            var result = _labelPrintService.Print(
                $"StockManager label - {SelectedCartItem.Product.ProductName}",
                LabelPreview);
            PrintFeedback = result.Message;
        }

        public bool CanPrintLastSaleLabels => !string.IsNullOrWhiteSpace(LastCompletedLabelContent);

        public void PrintLastSaleLabels()
        {
            if (!CanPrintLastSaleLabels)
            {
                PrintFeedback = "Complete a checkout before printing sale labels.";
                return;
            }

            LabelPreview = LastCompletedLabelContent;
            var result = _labelPrintService.Print("StockManager completed sale labels", LastCompletedLabelContent);
            PrintFeedback = result.Message;
        }
    }
}
