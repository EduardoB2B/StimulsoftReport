using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;

namespace StimulsoftReport.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly string _logFolder;

        public LogsController(IHostEnvironment env)
        {
            _logFolder = Path.Combine(env.ContentRootPath, "Logs");
            Console.WriteLine($"Logs folder path: {_logFolder}");
        }

        [HttpGet]
        public IActionResult ListLogs()
        {
            if (!Directory.Exists(_logFolder))
                return NotFound("No hay logs disponibles.");

            var files = Directory.GetFiles(_logFolder, "log-*.txt")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToList();

            return Ok(files);
        }

        [HttpGet("{fileName}")]
        public IActionResult DownloadLog(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return BadRequest("Nombre de archivo inválido.");

            var fullPath = Path.Combine(_logFolder, fileName);
            if (!System.IO.File.Exists(fullPath))
                return NotFound("Archivo no encontrado.");

            var contentType = "text/plain";

            try
            {
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return File(stream, contentType, fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al leer el archivo: {ex.Message}");
            }
        }
    }
}