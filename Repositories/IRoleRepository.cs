using Microsoft.AspNetCore.Identity;

namespace PurchasingSystemProduction.Repositories
{
    public interface IRoleRepository
    {
        ICollection<IdentityRole> GetRoles();
    }
}
