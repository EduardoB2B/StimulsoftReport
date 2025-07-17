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

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] ReportRequest? request)
        {
            if (string.IsNullOrWhiteSpace(request?.ReportName) || string.IsNullOrWhiteSpace(request?.JsonFilePath))
                return BadRequest("Debe proporcionar el nombre del reporte y la ruta del archivo JSON.");

            var (success, message, pdfPath) = await _reportService.GenerateReportAsync(request.ReportName!, request.JsonFilePath!);

            if (!success)
                return BadRequest(new { success = false, message });

            return Ok(new { success = true, message, pdfPath });
        }
    }
}