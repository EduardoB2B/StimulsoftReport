using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using StimulsoftReport.Configuration;
using StimulsoftReport.Services;
using Stimulsoft.Base;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Leer solo retainedFileCountLimit con valor por defecto 10
int retainedFileCountLimit = builder.Configuration.GetValue<int?>("SerilogSettings:RetainedFileCountLimit") ?? 10;

// Crear el switch para controlar el nivel de logging dinámicamente
var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = LogEventLevel.Information // Nivel inicial fijo, se controla dinámicamente luego
};

// ----------------------------
// Configurar Serilog
// ----------------------------
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(builder.Environment.ContentRootPath, "Logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainedFileCountLimit,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1));

Log.Logger = loggerConfig.CreateLogger();

builder.Host.UseSerilog();

// Registrar el switch en DI para usarlo en el controlador
builder.Services.AddSingleton(levelSwitch);

// ------------------------------------------------------------------
// 1. Cargar licencia de Stimulsoft usando directorio raíz de la app
// ------------------------------------------------------------------
var licensePath = Path.Combine(builder.Environment.ContentRootPath, "license.key");
StiLicense.LoadFromFile(licensePath);

// ------------------------------------------------------------------
// 2. Registrar configuración y servicios
// ------------------------------------------------------------------
builder.Services.Configure<ReportSettings>(builder.Configuration.GetSection("ReportSettings"));
builder.Services.AddSingleton<ReportService>();
builder.Services.AddControllers();

// ------------------------------------------------------------------
// 3. Configurar HealthChecks personalizados
// ------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck("templates-folder", () =>
    {
        var templatesPath = builder.Configuration["ReportSettings:TemplatesFolder"];

        if (string.IsNullOrEmpty(templatesPath))
            return HealthCheckResult.Unhealthy("La configuración TemplatesFolder no está definida");

        if (!Directory.Exists(templatesPath))
            return HealthCheckResult.Unhealthy($"No se encontró la carpeta de plantillas: {templatesPath}");

        var templateFiles = Directory.GetFiles(templatesPath, "*.mrt");
        if (templateFiles.Length == 0)
            return HealthCheckResult.Degraded("La carpeta de plantillas existe pero no contiene archivos .mrt");

        return HealthCheckResult.Healthy($"Carpeta de plantillas OK - Se encontraron {templateFiles.Length} archivos .mrt");
    })
    .AddCheck("configs-folder", () =>
    {
        var configsPath = builder.Configuration["ReportSettings:ConfigsFolder"];

        if (string.IsNullOrEmpty(configsPath))
            return HealthCheckResult.Unhealthy("La configuración ConfigsFolder no está definida");

        if (!Directory.Exists(configsPath))
            return HealthCheckResult.Unhealthy($"No se encontró la carpeta de configuraciones: {configsPath}");

        var jsonFiles = Directory.GetFiles(configsPath, "*.json");
        if (jsonFiles.Length == 0)
            return HealthCheckResult.Degraded("La carpeta de configuraciones existe pero no contiene archivos .json");

        return HealthCheckResult.Healthy($"Carpeta de configuraciones OK - Se encontraron {jsonFiles.Length} archivos .json");
    })
    .AddCheck("stimulsoft-license", () =>
    {
        try
        {
            var licenseFullPath = Path.Combine(builder.Environment.ContentRootPath, "license.key");

            if (!File.Exists(licenseFullPath))
                return HealthCheckResult.Degraded("Archivo de licencia no encontrado, usando modo de prueba");

            return HealthCheckResult.Healthy("Archivo de licencia de Stimulsoft encontrado");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Error al verificar la licencia: {ex.Message}");
        }
    });

var app = builder.Build();

// ------------------------------------------------------------------
// 4. Mapear controladores
// ------------------------------------------------------------------
app.MapControllers();

// ------------------------------------------------------------------
// 5. Endpoints de HealthChecks
// ------------------------------------------------------------------
app.MapHealthChecks("/health");

app.MapHealthChecks("/health/detailed", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        var templatesPath = builder.Configuration["ReportSettings:TemplatesFolder"];
        var reportes = Array.Empty<object>();

        if (!string.IsNullOrEmpty(templatesPath) && Directory.Exists(templatesPath))
        {
            reportes = Directory.GetFiles(templatesPath, "*.mrt")
                .Select(file => new
                {
                    reporte = Path.GetFileName(file),
                    ultima_modificacion = File.GetLastWriteTime(file).ToString("dd-MM-yyyy HH:mm")
                })
                .ToArray();
        }

        var response = new
        {
            estado = report.Status.ToString(),
            marca_tiempo = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"),
            version = version,
            duracion_ms = report.TotalDuration.TotalMilliseconds,
            verificaciones = report.Entries.Select(x => new
            {
                nombre = x.Key,
                estado = x.Value.Status.ToString(),
                duracion_ms = x.Value.Duration.TotalMilliseconds,
                descripcion = x.Value.Description,
                excepcion = x.Value.Exception?.Message
            }),
            reportes = reportes
        };

        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(response,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        );
    }
});

app.Run();