using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SMDesktopUI.ViewModels;

namespace SMDesktopUI.Views
{
    /// <summary>
    /// Interaction logic for SalesView.xaml
    /// </summary>
    public partial class SalesView : UserControl
    {
        public SalesView()
        {
            InitializeComponent();
            Loaded += (_, _) => FocusScanner();
        }

        private void ScannerInput_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            if (DataContext is SalesViewModel viewModel)
            {
                viewModel.CommitScan();
            }
            Dispatcher.BeginInvoke(FocusScanner);
        }

        private void FocusScanner()
        {
            ScanInput.Focus();
            ScanInput.SelectAll();
        }
    }
}
