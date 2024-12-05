using PurchasingSystemProduction.Repositories;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace PurchasingSystemProduction.Areas.MasterData.Models
{
    [Table("AspNetGroupRole", Schema = "dbo")]
    public class GroupRole : UserActivity
    {
        [Key]
        public Guid Id { get; set; }
        public string RoleId { get; set; }
        public string DepartemenId { get; set; }
    }
}
