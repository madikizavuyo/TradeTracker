using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TradeHelper.Data;
using TradeHelper.Models;
using TradeHelper.Services;
using System;

namespace TradeHelper.Controllers
{
    [Authorize]
    [Route("api/email")]
    public class EmailController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _context;

        public EmailController(UserManager<IdentityUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [HttpPost("sendcsv")]
        [RequestSizeLimit(10_000_000)] // 10MB limit
        [RequestFormLimits(MultipartBodyLengthLimit = 10_000_000)]
        public async Task<IActionResult> SendCsv(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");
            if (file.Length > 10_000_000)
                return BadRequest("File size exceeds 10 MB limit.");
            if (!FileUploadValidator.IsFileNameSafe(file.FileName))
                return BadRequest("Invalid file name.");
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (ext != ".csv")
                return BadRequest("Only CSV files are allowed.");
            using (var validateStream = file.OpenReadStream())
            {
                if (!await FileUploadValidator.ValidateContentAsync(validateStream, ext))
                    return BadRequest("File content does not match CSV format.");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrEmpty(user.Email))
                return Unauthorized("User email not found.");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var bytes = stream.ToArray();
            stream.Position = 0;

            string summary = await ExtractSummaryFromCsvAsync(stream);

            using var client = new SmtpClient("smtp.example.com")
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential("your-email@example.com", "your-password"),
                EnableSsl = true,
                Port = 587
            };

            var mail = new MailMessage("your-email@example.com", user.Email)
            {
                Subject = "Trade Helper Bias Scores CSV",
                Body = "Attached is your requested Bias Score CSV file.\n\n" + summary,
                IsBodyHtml = false
            };

            mail.Attachments.Add(new Attachment(new MemoryStream(bytes), file.FileName));

            try
            {
                await client.SendMailAsync(mail);
                _context.UserLogs.Add(new UserLog
                {
                    Email = user.Email,
                    Action = $"Sent Bias Score CSV ({file.FileName})",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return Ok($"CSV emailed to {user.Email} successfully!");
            }
            catch
            {
                return StatusCode(500, "Email failed to send.");
            }
        }

        private async Task<string> ExtractSummaryFromCsvAsync(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream);
            double total = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            int count = 0;

            await reader.ReadLineAsync(); // Skip header

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split(',');
                if (parts.Length >= 3 && double.TryParse(parts[2], out double score))
                {
                    total += score;
                    min = Math.Min(min, score);
                    max = Math.Max(max, score);
                    count++;
                }
            }

            if (count == 0) return "No valid scores found.";
            var avg = total / count;
            return $"Summary Stats: Avg = {avg:F2}, Min = {min:F2}, Max = {max:F2}";
        }
    }
}