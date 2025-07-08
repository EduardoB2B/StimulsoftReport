using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using StimulsoftReport.Models;
using StimulsoftReport.Services;
using System.Collections.Generic;

namespace StimulsoftReport.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>
        /// Genera un reporte usando filtros para extraer datos de SQL (GET).
        /// Ejemplo de uso: /api/report/generate?reportName=empresasreport&filtros={"fechaInicio":"2024-01-01"}
        /// </summary>
        [HttpGet("generate")]
        public async Task<IActionResult> GenerateFromFilters([FromQuery] string reportName, [FromQuery] string? filtros = null)
        {
            try
            {
                Console.WriteLine("Generando reporte desde filtros SQL...");
                Console.WriteLine($"ReportName: {reportName}");

                if (string.IsNullOrEmpty(reportName))
                    return BadRequest("Debe especificar el nombre del reporte.");

                // Deserializar filtros si vienen como JSON en query string
                var filtrosDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(filtros))
                {
                    try
                    {
                        filtrosDict = JsonSerializer.Deserialize<Dictionary<string, object>>(filtros) ?? new Dictionary<string, object>();
                    }
                    catch
                    {
                        return BadRequest("Los filtros no tienen un formato JSON válido.");
                    }
                }

                var request = new ReportFilterRequest
                {
                    ReportName = reportName,
                    Filtros = filtrosDict
                };

                var fileBytes = await _reportService.GenerateReportFromFiltersAsync(request);

                if (fileBytes == null || fileBytes.Length == 0)
                    return BadRequest("No se pudo generar el reporte o está vacío.");

                return File(fileBytes, "application/pdf", $"{reportName}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error generando reporte desde filtros: " + ex);
                return BadRequest($"Error generando reporte: {ex.Message}");
            }
        }

        /// <summary>
        /// Genera un reporte usando data ya preparada por el cliente (POST).
        /// </summary>
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateFromData([FromBody] ReportDataRequest request)
        {
            try
            {
                Console.WriteLine("Generando reporte desde data preparada...");
                Console.WriteLine($"Request: {JsonSerializer.Serialize(request)}");

                if (request == null)
                    return BadRequest("El request es nulo.");

                if (string.IsNullOrEmpty(request.ReportName))
                    return BadRequest("Debe especificar el nombre del reporte.");

                if (request.Data == null || request.Data.Count == 0)
                    return BadRequest("Debe proporcionar los datos para el reporte.");

                var fileBytes = await _reportService.GenerateReportFromDataAsync(request);

                if (fileBytes == null || fileBytes.Length == 0)
                    return BadRequest("No se pudo generar el reporte o está vacío.");

                return File(fileBytes, "application/pdf", $"{request.ReportName}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error generando reporte desde data: " + ex);
                return BadRequest($"Error generando reporte: {ex.Message}");
            }
        }
    }
}