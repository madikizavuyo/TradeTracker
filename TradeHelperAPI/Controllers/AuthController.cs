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

        public AuthController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var isValidPassword = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isValidPassword)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            // Get user roles
            var roles = await _userManager.GetRolesAsync(user);

            // Generate JWT token
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
            var jwtKey = _configuration["Jwt:Key"] ?? "YourSuperSecretKeyForJWTTokenGenerationThatIsAtLeast32CharactersLong!";
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
            return Ok(new { 
                authenticated = true, 
                email = User.Identity?.Name,
                claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }

        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdmin()
        {
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
                    return Ok(new { message = "Admin user created successfully", email = adminEmail, password = adminPassword });
                }
                return BadRequest(new { errors = result.Errors });
            }
            else
            {
                // Reset password
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, adminPassword);
                if (resetResult.Succeeded)
                {
                    if (!await _userManager.IsInRoleAsync(user, "Admin"))
                    {
                        await _userManager.AddToRoleAsync(user, "Admin");
                    }
                    return Ok(new { message = "Admin password reset successfully", email = adminEmail, password = adminPassword });
                }
                return BadRequest(new { errors = resetResult.Errors });
            }
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
    }
}

