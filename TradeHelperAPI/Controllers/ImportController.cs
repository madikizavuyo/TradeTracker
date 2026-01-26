using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Services;
using TradeHelper.Data;
using TradeHelper.Models;
using System.Security.Claims;
using System.Linq;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ImportController : ControllerBase
    {
        private readonly ImportService _importService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ImportController> _logger;

        public ImportController(
            ImportService importService,
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ILogger<ImportController> logger)
        {
            _importService = importService;
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("history")]
        public async Task<IActionResult> History()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var history = await _context.BrokerImportHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.ImportedAt)
                .Take(10)
                .Select(h => new
                {
                    h.Id,
                    h.BrokerName,
                    h.OriginalFileName,
                    h.TradesImported,
                    h.TradesSkipped,
                    h.TradesFailed,
                    h.ImportNotes,
                    h.Status,
                    h.ImportedAt,
                    h.CompletedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB limit
        [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000, ValueLengthLimit = int.MaxValue)]
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile? file, 
            [FromForm] string? brokerName = null, 
            [FromForm] string? currency = null, 
            [FromForm] string? strategyId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Upload attempt without valid user ID");
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Upload request received. File: {FileName}, Size: {Size}, Broker: {Broker}, Currency: {Currency}", 
                file?.FileName, file?.Length, brokerName, currency);

            if (file == null)
            {
                _logger.LogWarning("Upload request without file");
                return BadRequest(new { message = "No file provided. Please select a file to upload." });
            }

            // Validate file size (100MB limit)
            if (file.Length > 100_000_000)
            {
                return BadRequest(new { 
                    message = $"File size ({file.Length / 1024.0 / 1024.0:F2} MB) exceeds the maximum allowed size of 100 MB" 
                });
            }

            // Validate file extension
            var allowedExtensions = new[] { ".csv", ".xlsx", ".xls", ".pdf" };
            var fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension) || !allowedExtensions.Contains(fileExtension))
            {
                return BadRequest(new { 
                    message = $"File type '{fileExtension}' is not supported. Allowed types: CSV, Excel (.xlsx, .xls), PDF" 
                });
            }

            // Create import history record
            var importHistory = new BrokerImportHistory
            {
                UserId = userId,
                BrokerName = brokerName ?? "Unknown",
                OriginalFileName = file.FileName,
                Status = "Processing",
                ImportedAt = DateTime.UtcNow
            };

            _context.BrokerImportHistories.Add(importHistory);
            await _context.SaveChangesAsync();

            try
            {
                int? strategyIdInt = null;
                if (!string.IsNullOrEmpty(strategyId) && int.TryParse(strategyId, out var sid))
                {
                    strategyIdInt = sid;
                }

                // Process the file
                ImportResult result;
                using (var stream = file.OpenReadStream())
                {
                    result = await _importService.ImportTradesFromFileAsync(
                        stream,
                        file.FileName,
                        userId,
                        brokerName,
                        currency,
                        strategyIdInt);
                }

                // Update import history
                importHistory.TradesImported = result.TradesImported;
                importHistory.TradesSkipped = result.TradesSkipped;
                importHistory.TradesFailed = result.TradesFailed;
                importHistory.Status = result.TradesFailed > 0 && result.TradesImported == 0 
                    ? "Failed" 
                    : result.TradesFailed > 0 
                        ? "PartiallyCompleted" 
                        : "Completed";
                importHistory.CompletedAt = DateTime.UtcNow;
                importHistory.ImportNotes = string.Join("; ", result.Errors);

                await _context.SaveChangesAsync();

                if (result.Errors.Any() && result.TradesImported == 0)
                {
                    var errorMessage = result.Errors.Count > 0 
                        ? string.Join(" ", result.Errors) 
                        : "Import completed with errors but no trades were imported. Please check your file format.";
                    
                    return BadRequest(new
                    {
                        message = errorMessage,
                        tradesImported = result.TradesImported,
                        tradesSkipped = result.TradesSkipped,
                        tradesFailed = result.TradesFailed,
                        errors = result.Errors
                    });
                }

                return Ok(new
                {
                    message = $"Import completed successfully! {result.TradesImported} trades imported, {result.TradesSkipped} skipped, {result.TradesFailed} failed.",
                    tradesImported = result.TradesImported,
                    tradesSkipped = result.TradesSkipped,
                    tradesFailed = result.TradesFailed,
                    errors = result.Errors
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing import file {FileName}", file.FileName);
                
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
    }
}

