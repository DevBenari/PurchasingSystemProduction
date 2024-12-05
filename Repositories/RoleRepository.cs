using Microsoft.AspNetCore.Identity;
using PurchasingSystemProduction.Data;
using PurchasingSystemProduction.Models;

namespace PurchasingSystemProduction.Repositories
{
    public class RoleRepository : IRoleRepository
    {
        private readonly ApplicationDbContext _context;
        public RoleRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public ICollection<IdentityRole> GetRoles()
        {
            return _context.Roles.ToList();
        }
    }
}
