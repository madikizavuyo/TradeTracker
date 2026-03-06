using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace TradeHelper.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly TradeHelper.Services.LoginRateLimitService _loginRateLimit;

        public AuthController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, IConfiguration configuration, IWebHostEnvironment env, TradeHelper.Services.LoginRateLimitService loginRateLimit)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _env = env;
            _loginRateLimit = loginRateLimit;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || !ModelState.IsValid)
                return BadRequest(ModelState);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            if (!_loginRateLimit.IsAllowed(ip))
                return StatusCode(429, new { message = "Too many login attempts. Please try again later." });

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                _loginRateLimit.RecordAttempt(ip);
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var isValidPassword = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isValidPassword)
            {
                _loginRateLimit.RecordAttempt(ip);
                return Unauthorized(new { message = "Invalid email or password." });
            }

            _loginRateLimit.ClearAttempts(ip);
            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            return Ok(new 
            { 
                message = "Login successful", 
                email = user.Email,
                token = token,
                expiresIn = 3600 // 1 hour
            });
        }

        private string GenerateJwtToken(IdentityUser user, IList<string> roles)
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
                throw new InvalidOperationException("Jwt:Key must be configured. See SECRETS.md.");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "TradeHelperAPI";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "TradeHelperClient";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Email ?? user.UserName ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddHours(1);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            // JWT tokens are stateless, so logout is handled client-side by removing the token
            return Ok(new { message = "Logout successful" });
        }

        [HttpGet("check")]
        [Authorize]
        public IActionResult CheckAuth()
        {
            var roles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();
            return Ok(new { 
                authenticated = true, 
                email = User.Identity?.Name,
                roles
            });
        }

        [HttpPost("refresh")]
        [Authorize]
        public async Task<IActionResult> Refresh()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            return Ok(new { token, expiresIn = 3600 });
        }

        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin([FromQuery] string? setupToken = null)
        {
            if (!_env.IsDevelopment())
            {
                var requiredToken = _configuration["AdminSetupToken"] ?? Environment.GetEnvironmentVariable("ADMIN_SETUP_TOKEN");
                if (string.IsNullOrEmpty(requiredToken) || setupToken != requiredToken)
                    return NotFound();
            }
            var adminEmail = "admin@tradehelper.ai";
            var adminPassword = "Admin@1234";
            var user = await _userManager.FindByEmailAsync(adminEmail);
            if (user == null)
            {
                user = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var result = await _userManager.CreateAsync(user, adminPassword);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "Admin");
                    return Ok(new { message = "Admin user created successfully", email = adminEmail });
                }
                return BadRequest(new { errors = result.Errors });
            }
            else
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, adminPassword);
                if (resetResult.Succeeded)
                {
                    if (!await _userManager.IsInRoleAsync(user, "Admin"))
                        await _userManager.AddToRoleAsync(user, "Admin");
                    return Ok(new { message = "Admin password reset successfully", email = adminEmail });
                }
                return BadRequest(new { errors = resetResult.Errors });
            }
        }
    }

    public class LoginRequest
    {
        [Required(ErrorMessage = "Email is required.")]
        public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
    }
}

