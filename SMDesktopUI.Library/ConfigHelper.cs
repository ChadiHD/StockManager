using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDesktopUI.Library
{
    public class ConfigHelper
    {
        // Activate the tax rate specified in App.config
        public static decimal GetTaxRate()
        {
            string rateText = ConfigurationManager.AppSettings["taxRate"];

            bool IsValidVat = Decimal.TryParse(rateText, out decimal output);

            if (IsValidVat == false)
            {
                throw new ConfigurationErrorsException("The tax rate is not");
            }

            return output;
        }
    }
}
