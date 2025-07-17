using System;
using System.Data;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Stimulsoft.Report;
using StimulsoftReport.Configuration;

namespace StimulsoftReport.Services
{
    public class ReportService
    {
        private readonly string _templatesFolder;

        public ReportService(IOptions<ReportSettings> options)
        {
            _templatesFolder = options.Value.TemplatesFolder;
        }

        public async Task<(bool Success, string Message, string? PdfPath)> GenerateReportAsync(string reportName, string jsonFilePath)
        {
            var templatePath = Path.Combine(_templatesFolder, $"{reportName}.mrt");

            if (!File.Exists(templatePath))
                return (false, $"Plantilla no encontrada en {templatePath}", null);

            if (!File.Exists(jsonFilePath))
                return (false, $"Archivo JSON no encontrado en {jsonFilePath}", null);

            var directory = Path.GetDirectoryName(jsonFilePath);
            if (directory == null)
                return (false, "La ruta del archivo JSON no tiene directorio válido.", null);

            try
            {
                var report = new StiReport();
                report.Load(templatePath);

                var jsonString = await File.ReadAllTextAsync(jsonFilePath);
                var jsonNode = JsonNode.Parse(jsonString);
                var jsonObject = jsonNode as JsonObject;

                if (jsonObject == null)
                    return (false, "El JSON no es un objeto válido.", null);

                RegisterData(report, jsonObject);

                report.Dictionary.Databases.Clear();
                report.Dictionary.Synchronize();

                report.Compile();
                report.Render(false);

                var pdfFileName = Path.GetFileNameWithoutExtension(jsonFilePath) + ".pdf";
                var pdfFullPath = Path.Combine(directory, pdfFileName);

                report.ExportDocument(StiExportFormat.Pdf, pdfFullPath);

                return (true, "Reporte generado correctamente", pdfFullPath);
            }
            catch (Exception ex)
            {
                return (false, $"Error generando reporte: {ex.Message}", null);
            }
        }

        private void RegisterData(StiReport report, JsonObject jsonObject)
        {
            // Tabla Data con propiedades simples
            var dataTable = new DataTable("Data");

            foreach (var prop in jsonObject)
            {
                if (prop.Value is JsonArray)
                    continue; // Ignorar arrays

                dataTable.Columns.Add(prop.Key, typeof(string));
            }

            var row = dataTable.NewRow();

            foreach (var prop in jsonObject)
            {
                if (prop.Value is JsonArray)
                    continue;

                row[prop.Key] = prop.Value?.ToString() ?? "";
            }

            dataTable.Rows.Add(row);
            report.RegData("Data", dataTable);

            // Tabla Detalle con arreglo de objetos
            if (jsonObject.TryGetPropertyValue("Detalle", out var detalleNode) && detalleNode is JsonArray detalleArray)
            {
                var detalleTable = new DataTable("Detalle");

                if (detalleArray.Count > 0 && detalleArray[0] is JsonObject firstDetalle)
                {
                    foreach (var col in firstDetalle)
                    {
                        detalleTable.Columns.Add(col.Key, typeof(string));
                    }

                    foreach (var item in detalleArray)
                    {
                        var detalleRow = detalleTable.NewRow();
                        var obj = item as JsonObject;
                        foreach (var col in obj)
                        {
                            detalleRow[col.Key] = col.Value?.ToString() ?? "";
                        }
                        detalleTable.Rows.Add(detalleRow);
                    }
                }

                report.RegData("Detalle", detalleTable);
            }
        }
    }
}