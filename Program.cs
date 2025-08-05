using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StimulsoftReport.Configuration;
using StimulsoftReport.Services;
using Stimulsoft.Base;

var builder = WebApplication.CreateBuilder(args);

// Cargar licencia de Stimulsoft
StiLicense.LoadFromFile("./license.key");

// Registrar configuración de reportes
builder.Services.Configure<ReportSettings>(builder.Configuration.GetSection("ReportSettings"));

// Registrar servicios
builder.Services.AddSingleton<ReportService>();
builder.Services.AddControllers();

var app = builder.Build();

// Mapear controladores
app.MapControllers();

app.Run();