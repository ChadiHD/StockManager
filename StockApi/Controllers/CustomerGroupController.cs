using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace StockApi.Controllers
{
    // Customer groups drive the discount applied to an account's pricing.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class CustomerGroupController : ControllerBase
    {
        private readonly ICustomerGroupData _groupData;

        public CustomerGroupController(ICustomerGroupData groupData)
        {
            _groupData = groupData;
        }

        [HttpGet]
        public List<CustomerGroupModel> GetAll()
        {
            return _groupData.GetGroups();
        }

        [HttpGet("{slug}")]
        public ActionResult<CustomerGroupModel> GetBySlug(string slug)
        {
            var group = _groupData.GetGroupBySlug(slug);

            return group is null ? NotFound() : group;
        }

        [HttpPost]
        public ActionResult<CustomerGroupModel> Create(CustomerGroupModel group)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                return BadRequest("Name is required.");
            }

            return _groupData.CreateGroup(group);
        }

        public record GroupChangeModel(int Discount, string Terms, string Note);

        [HttpPut("{slug}")]
        public IActionResult Update(string slug, GroupChangeModel change)
        {
            if (_groupData.GetGroupBySlug(slug) is null)
            {
                return NotFound();
            }

            _groupData.UpdateGroup(slug, change.Discount, change.Terms, change.Note);

            return NoContent();
        }
    }
}
