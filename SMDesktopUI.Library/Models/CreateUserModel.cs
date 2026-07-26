using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace SMDesktopUI.Library.Models
{
    public class CreateUserModel
    {
        [Required(ErrorMessage = "First name is required.")]
        public string FirstName { get; set; }
        [Required(ErrorMessage = "Last name is required.")]
        public string LastName { get; set; }
        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string EmailAddress { get; set; }
        [Required(ErrorMessage = "Password is required.")]
        [MinLength(6, ErrorMessage = "Password must contain at least 6 characters.")]
        public string Password { get; set; }
        [Required(ErrorMessage = "Confirm the password.")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match")]
        public string ConfirmPassword { get; set; }
    }
}
