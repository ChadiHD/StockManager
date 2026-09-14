using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    // Customer groups drive the discount applied to an account's pricing.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class CustomerGroupController : ControllerBase
    {
        private readonly ICustomerGroupData _groupData;
        private readonly IAdminSiteContext _site;

        public CustomerGroupController(ICustomerGroupData groupData, IAdminSiteContext site)
        {
            _groupData = groupData;
            _site = site;
        }

        [HttpGet]
        public List<CustomerGroupModel> GetAll()
        {
            return _groupData.GetGroups(_site.SiteId);
        }

        [HttpGet("{slug}")]
        public ActionResult<CustomerGroupModel> GetBySlug(string slug)
        {
            var group = _groupData.GetGroupBySlug(slug, _site.SiteId);

            return group is null ? NotFound() : group;
        }

        [HttpPost]
        public ActionResult<CustomerGroupModel> Create(CustomerGroupModel group)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                return BadRequest("Name is required.");
            }

            return _groupData.CreateGroup(group, _site.SiteId);
        }

        public record GroupChangeModel(int Discount, string Terms, string Note);

        [HttpPut("{slug}")]
        public IActionResult Update(string slug, GroupChangeModel change)
        {
            if (_groupData.GetGroupBySlug(slug, _site.SiteId) is null)
            {
                return NotFound();
            }

            _groupData.UpdateGroup(slug, change.Discount, change.Terms, change.Note, _site.SiteId);

            return NoContent();
        }
    }
}
