using Microsoft.AspNetCore.Mvc;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StimulsoftReport.Controllers
{
    [ApiController]
    [Route("api/logging")]
    public class LoggingController : ControllerBase
    {
        private readonly LoggingLevelSwitch _levelSwitch;

        public LoggingController(LoggingLevelSwitch levelSwitch)
        {
            _levelSwitch = levelSwitch;
        }

        // GET api/logging/level
        [HttpGet("level")]
        public IActionResult GetCurrentLevel()
        {
            return Ok(new
            {
                CurrentLevel = _levelSwitch.MinimumLevel.ToString()
            });
        }

        // POST api/logging/level?level=Information
        [HttpPost("level")]
        public IActionResult SetLevel([FromQuery] string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return BadRequest("Debe especificar un nivel de logging.");

            if (level.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                _levelSwitch.MinimumLevel = LogEventLevel.Fatal; // Nivel más restrictivo para "apagar"
                return Ok(new { Message = "Logging apagado (nivel Fatal aplicado)." });
            }

            if (!TryParseLogLevel(level, out var parsedLevel))
                return BadRequest($"Nivel inválido. Niveles válidos: {string.Join(", ", GetValidLevels())}");

            _levelSwitch.MinimumLevel = parsedLevel;

            return Ok(new
            {
                Message = $"Nivel de logging cambiado a {parsedLevel}"
            });
        }

        private bool TryParseLogLevel(string level, out LogEventLevel logLevel)
        {
            return Enum.TryParse(level, true, out logLevel) && GetValidLevels().Contains(logLevel);
        }

        private IEnumerable<LogEventLevel> GetValidLevels()
        {
            return new[]
            {
                LogEventLevel.Verbose,
                LogEventLevel.Debug,
                LogEventLevel.Information,
                LogEventLevel.Warning,
                LogEventLevel.Error,
                LogEventLevel.Fatal
            };
        }
    }
}