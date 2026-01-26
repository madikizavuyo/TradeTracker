using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;
using TradeHelper.Models;
using System.Security.Claims;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MLTradingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<MLTradingController> _logger;

        public MLTradingController(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<MLTradingController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Return ML import history (can reuse BrokerImportHistory or create separate model)
            var history = await _context.BrokerImportHistories
                .Where(h => h.UserId == userId && h.BrokerName.Contains("ML"))
                .OrderByDescending(h => h.ImportedAt)
                .Take(10)
                .Select(h => new
                {
                    h.Id,
                    h.OriginalFileName,
                    h.TradesImported,
                    h.TradesSkipped,
                    h.Status,
                    h.ImportedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB limit
        [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000, ValueLengthLimit = int.MaxValue)]
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile? file,
            [FromForm] string? currency = null,
            [FromForm] string? selectedStrategyId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (file == null)
            {
                return BadRequest(new { message = "No file provided" });
            }

            // Validate file size (100MB limit)
            if (file.Length > 100_000_000)
            {
                return BadRequest(new
                {
                    message = $"File size ({file.Length / 1024.0 / 1024.0:F2} MB) exceeds the maximum allowed size of 100 MB"
                });
            }

            // Create import history record
            var importHistory = new BrokerImportHistory
            {
                UserId = userId,
                BrokerName = "ML Trading (AI Processing)",
                OriginalFileName = file.FileName,
                Status = "Processing",
                ImportedAt = DateTime.UtcNow
            };

            _context.BrokerImportHistories.Add(importHistory);
            await _context.SaveChangesAsync();

            try
            {
                // TODO: Implement AI-based PDF/image processing
                // For now, return a message indicating the feature is being developed
                importHistory.Status = "Failed";
                importHistory.CompletedAt = DateTime.UtcNow;
                importHistory.ImportNotes = "ML Trading AI processing is not yet implemented. This feature will use AI to extract trade data from PDF files and images.";
                await _context.SaveChangesAsync();

                return StatusCode(501, new
                {
                    message = "ML Trading AI processing is not yet implemented. This feature will use AI to extract trade data from PDF files and images. Please use CSV/Excel files in the regular Import page for now.",
                    receivedFile = file.FileName,
                    fileSize = file.Length,
                    fileSizeMB = (file.Length / 1024.0 / 1024.0).ToString("F2")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ML Trading file {FileName}", file.FileName);

                importHistory.Status = "Failed";
                importHistory.CompletedAt = DateTime.UtcNow;
                importHistory.ImportNotes = $"Error: {ex.Message}";
                await _context.SaveChangesAsync();

                return StatusCode(500, new
                {
                    message = "An error occurred while processing the file",
                    error = ex.Message
                });
            }
        }

        [HttpGet("strategies")]
        public async Task<IActionResult> GetStrategies()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Return strategies (same as Strategies controller)
            var strategies = await _context.Strategies
                .Where(s => s.UserId == userId && s.IsActive)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Description,
                    s.IsActive
                })
                .ToListAsync();

            return Ok(strategies);
        }
    }
}


