using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using StimulsoftReport.Configuration;
using StimulsoftReport.Services;
using Stimulsoft.Base;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

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
    // Validación: carpeta de plantillas
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
    // Validación: carpeta de configuraciones
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
    // Validación: licencia de Stimulsoft
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

// Endpoint básico (estado simple: 200 OK o 503)
app.MapHealthChecks("/health");

// Endpoint detallado (con JSON extendido)
app.MapHealthChecks("/health/detailed", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        // ----------------------------------------------------------
        // Obtener versión desde Assembly
        // ----------------------------------------------------------
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        // ----------------------------------------------------------
        // Obtener lista de reportes .mrt con su última modificación
        // ----------------------------------------------------------
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

        // ----------------------------------------------------------
        // Construcción de respuesta JSON
        // ----------------------------------------------------------
        var response = new
        {
            estado = report.Status.ToString(),
            marca_tiempo = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"), // Local, legible
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