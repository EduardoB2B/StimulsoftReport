using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using StimulsoftReportDemo.Models;
using StimulsoftReportDemo.Services; // Asegúrate de tener esta carpeta y el servicio creado

namespace StimulsoftReportDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        // Inyectamos el servicio que maneja la lógica de generación de reportes
        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>
        /// Recibe un request con el nombre del reporte y los filtros, genera y devuelve un PDF.
        /// </summary>
        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] ReportRequest request)
        {
            try
            {
                Console.WriteLine("Iniciando generación de reporte...");
                Console.WriteLine($"Request: {JsonSerializer.Serialize(request)}");

                // Validación básica del request
                if (request == null)
                    return BadRequest("El request es nulo.");

                if (string.IsNullOrEmpty(request.ReportName))
                    return BadRequest("Debe especificar el nombre del reporte.");

                // Llamamos al servicio para generar el PDF
                var fileBytes = await _reportService.GenerateReportAsync(request);

                if (fileBytes == null || fileBytes.Length == 0)
                    return BadRequest("No se pudo generar el reporte o está vacío.");

                // Retornamos el archivo generado
                return File(fileBytes, "application/pdf", $"{request.ReportName}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error generando reporte: " + ex);
                return BadRequest($"Error generando reporte: {ex.Message}");
            }
        }
    }
}
