using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Shipping;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// The acting store's delivery rates (T13), at <c>/admin/shipping</c>. Scoped like every store's
    /// own rows: another store's rate is not found.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ShippingRateController : ControllerBase
    {
        private readonly IShippingRateData _rates;
        private readonly IAdminSiteContext _site;

        public ShippingRateController(IShippingRateData rates, IAdminSiteContext site)
        {
            _rates = rates;
            _site = site;
        }

        [HttpGet]
        public List<ShippingRateModel> Get() => _rates.GetBySite(_site.SiteId);

        /// <summary>Adds a rate when its id is zero, or changes one of the acting store's.</summary>
        [HttpPut]
        public IActionResult Save(ShippingRateModel rate)
        {
            var refusal = WhyRefused(rate);
            if (refusal is not null) return BadRequest(refusal);

            rate.Name = rate.Name.Trim();
            rate.Countries = ShippingQuote.Normalise(rate.Countries);

            return _rates.Save(rate, _site.SiteId) ? NoContent() : NotFound();
        }

        /// <summary>
        /// What the table would refuse, said first: a CHECK violated inside a procedure reaches the
        /// caller as a 500.
        /// </summary>
        public static string? WhyRefused(ShippingRateModel rate)
        {
            if (string.IsNullOrWhiteSpace(rate.Name) || rate.Name.Trim().Length > 100)
            {
                return "A delivery rate needs a name of up to 100 characters.";
            }

            if (rate.Price < 0 || rate.FreeOver < 0)
            {
                return "A price and a free-over amount cannot be below zero.";
            }

            var codes = ShippingQuote.Codes(rate.Countries).ToList();

            if (codes.Any(code => code.Length != 2 || !code.All(char.IsAsciiLetter)))
            {
                return "Countries are two-letter codes separated by commas, such as BR, PT. Leave it empty for every country.";
            }

            return ShippingQuote.Normalise(rate.Countries)?.Length > 400
                ? "That is more countries than a rate can hold. Leave it empty for every country."
                : null;
        }
    }
}
