using PurchasingSystemProduction.Areas.MasterData.ViewModels;
using PurchasingSystemProduction.Models;

namespace PurchasingSystemProduction.Areas.MasterData.Models
{
    public class Dashboard
    {
        public IEnumerable<ApplicationUser> UserOnlines { get; set; }
        public UserActiveViewModel UserActiveViewModels { get; set; }
        public IEnumerable<Product> Products { get; set; }
    }
}
