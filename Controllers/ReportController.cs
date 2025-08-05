using Microsoft.AspNetCore.Mvc;
using StimulsoftReport.Models;
using StimulsoftReport.Services;
using System.Threading.Tasks;

namespace StimulsoftReport.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly ReportService _reportService;

        public ReportController(ReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpPost]
        public async Task<IActionResult> GenerateReport([FromBody] ReportRequest request)
        {
            var (success, message, pdfPath) = await _reportService.GenerateReportAsync(
                request.ReportName,
                request.JsonFilePath,
                request.SqlParams
            );

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message, pdfPath });
        }
    }
}