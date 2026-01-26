// StartupTasks.cs – seed default roles and admin user
using Microsoft.AspNetCore.Identity;

namespace TradeHelper.Data
{
    public static class StartupTasks
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

            string[] roles = ["Admin", "User"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var adminEmail = "admin@tradehelper.ai";
            var adminPassword = "Admin@1234";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            
            if (adminUser == null)
            {
                adminUser = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }
            else
            {
                // Reset password if user exists (in case it was changed)
                var token = await userManager.GeneratePasswordResetTokenAsync(adminUser);
                await userManager.ResetPasswordAsync(adminUser, token, adminPassword);
                
                // Ensure user is in Admin role
                if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // Create normal user
            var userEmail = "user@tradehelper.ai";
            var userPassword = "User@1234";
            var normalUser = await userManager.FindByEmailAsync(userEmail);
            
            if (normalUser == null)
            {
                normalUser = new IdentityUser { UserName = userEmail, Email = userEmail, EmailConfirmed = true };
                var result = await userManager.CreateAsync(normalUser, userPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(normalUser, "User");
                }
            }
            else
            {
                // Reset password if user exists (in case it was changed)
                var token = await userManager.GeneratePasswordResetTokenAsync(normalUser);
                await userManager.ResetPasswordAsync(normalUser, token, userPassword);
                
                // Ensure user is in User role
                if (!await userManager.IsInRoleAsync(normalUser, "User"))
                {
                    await userManager.AddToRoleAsync(normalUser, "User");
                }
            }
        }
    }
}