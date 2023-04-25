using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace SMDesktopUI.Library.Models
{
    public class CreateUserModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        [EmailAddress]
        public string EmailAddress { get; set; }
        public string Password { get; set; }
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match")]
        public string ConfirmPassword { get; set; }
    }
}
