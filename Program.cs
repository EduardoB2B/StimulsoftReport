using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using StimulsoftReportDemo.Services;
using StimulsoftReportDemo.Data;
using System;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("La cadena de conexión 'DefaultConnection' no está configurada en appsettings.json.");
}

builder.Services.AddControllers();

// Registrar los servicios necesarios para generación de reportes
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportDataProvider, ReportDataProvider>();

var app = builder.Build();

app.MapControllers();

app.Run();
