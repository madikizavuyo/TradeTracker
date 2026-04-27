using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;

namespace TradeHelper.Controllers
{
    /// <summary>
    /// Admin-only endpoints for diagnostics, user management, and error log inspection.
    /// </summary>
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public AdminController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        /// <summary>Lists registered users and their roles.</summary>
        [HttpGet("users")]
        public async Task<IActionResult> ListUsers()
        {
            var users = await _userManager.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            var items = new List<object>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                items.Add(new { u.Id, email = u.Email, roles });
            }

            return Ok(items);
        }

        /// <summary>Creates a new user. Default role is User; optionally grant Admin.</summary>
        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request)
        {
            if (request == null || !ModelState.IsValid)
                return BadRequest(ModelState);

            var email = request.Email.Trim();
            if (await _userManager.FindByEmailAsync(email) != null)
                return Conflict(new { message = "A user with this email already exists." });

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });

            var role = request.GrantAdminRole ? "Admin" : "User";
            var addRole = await _userManager.AddToRoleAsync(user, role);
            if (!addRole.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                return BadRequest(new { errors = addRole.Errors.Select(e => e.Description).ToList() });
            }

            return Ok(new { message = "User created.", email, roles = new[] { role } });
        }

        /// <summary>
        /// Returns error and warning logs from ApplicationLogs for the last 5 days, with pagination.
        /// Used for admin investigation of refresh failures and other issues.
        /// </summary>
        [HttpGet("error-logs")]
        public async Task<IActionResult> GetErrorLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? level = null)
        {
            var cutoff = DateTime.UtcNow.AddDays(-5);
            var query = _context.ApplicationLogs
                .Where(l => l.Timestamp >= cutoff)
                .Where(l => l.Level == "Error" || l.Level == "Warning");

            if (!string.IsNullOrWhiteSpace(level))
            {
                var lvl = level.Trim();
                if (lvl.Equals("Error", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.Level == "Error");
                else if (lvl.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.Level == "Warning");
            }

            var totalCount = await query.CountAsync();
            var effectivePageSize = Math.Min(Math.Max(pageSize, 10), 100);
            var items = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(l => new
                {
                    l.Id,
                    l.Timestamp,
                    l.Level,
                    l.Category,
                    l.Message,
                    l.Exception
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize)
            });
        }
    }

    public class AdminCreateUserRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>If true, assigns the Admin role instead of User.</summary>
        public bool GrantAdminRole { get; set; }
    }
}
