using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StimulsoftReport.Configuration;
using StimulsoftReport.Services;

var builder = WebApplication.CreateBuilder(args);

// Registrar configuración
builder.Services.Configure<ReportSettings>(builder.Configuration.GetSection("ReportSettings"));

// Registrar servicios
builder.Services.AddControllers();
builder.Services.AddScoped<ReportService>();

var app = builder.Build();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.Run();