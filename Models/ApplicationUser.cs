﻿using Microsoft.AspNetCore.Identity;

namespace PurchasingSystemProduction.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string KodeUser { get; set; }
        public string NamaUser { get; set; }
        public bool IsActive { get; set; }
        public bool IsOnline { get; set; }
    }
}