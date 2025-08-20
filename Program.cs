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

var builder = WebApplication.CreateBuilder(args);

// Cargar licencia de Stimulsoft usando ContentRootPath
var licensePath = Path.Combine(builder.Environment.ContentRootPath, "license.key");
StiLicense.LoadFromFile(licensePath);

// Registrar configuración de reportes
builder.Services.Configure<ReportSettings>(builder.Configuration.GetSection("ReportSettings"));

// Registrar servicios
builder.Services.AddSingleton<ReportService>();
builder.Services.AddControllers();

// *** NUEVO: Agregar HealthChecks con validaciones personalizadas ***
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
            return HealthCheckResult.Degraded($"La carpeta de plantillas existe pero no contiene archivos .mrt: {templatesPath}");
            
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
            return HealthCheckResult.Degraded($"La carpeta de configuraciones existe pero no contiene archivos .json: {configsPath}");
            
        return HealthCheckResult.Healthy($"Carpeta de configuraciones OK - Se encontraron {jsonFiles.Length} archivos .json");
    })
    .AddCheck("stimulsoft-license", () =>
    {
        try
        {
            var licenseFullPath = Path.Combine(builder.Environment.ContentRootPath, "license.key");
            
            if (!File.Exists(licenseFullPath))
                return HealthCheckResult.Degraded("Archivo de licencia no encontrado, usando modo de prueba");
                
            return HealthCheckResult.Healthy($"Archivo de licencia de Stimulsoft encontrado");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Error al verificar la licencia: {ex.Message}");
        }
    });

var app = builder.Build();

// Mapear controladores
app.MapControllers();

// *** NUEVO: Mapear endpoints de HealthChecks ***
// Health check básico (respuesta simple)
app.MapHealthChecks("/health");

// Health check detallado con respuesta JSON personalizada
app.MapHealthChecks("/health/detailed", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            estado = report.Status.ToString(),
            marca_tiempo = DateTime.UtcNow,
            duracion_ms = report.TotalDuration.TotalMilliseconds,
            verificaciones = report.Entries.Select(x => new
            {
                nombre = x.Key,
                estado = x.Value.Status.ToString(),
                duracion_ms = x.Value.Duration.TotalMilliseconds,
                descripcion = x.Value.Description,
                excepcion = x.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        }));
    }
});

app.Run();