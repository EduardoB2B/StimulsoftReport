using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private readonly string _configsFolder;
        private readonly Dictionary<string, ReportConfig> _reportConfigs;

        public ReportService(IOptions<ReportSettings> options)
        {
            _templatesFolder = options.Value.TemplatesFolder;
            _configsFolder = options.Value.ConfigsFolder;
            _reportConfigs = LoadReportConfigs(_configsFolder);
        }

        // Carga todos los archivos .json de la carpeta de configuraciones
        private Dictionary<string, ReportConfig> LoadReportConfigs(string folder)
        {
            var configs = new Dictionary<string, ReportConfig>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folder))
                return configs;

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var config = JsonSerializer.Deserialize<ReportConfig>(json);
                    if (config != null)
                    {
                        var reportName = Path.GetFileNameWithoutExtension(file);
                        configs[reportName] = config;
                    }
                }
                catch
                {
                    // Si hay error en un archivo, lo ignora (puedes loguear si quieres)
                }
            }

            // LOG temporal para depuración
            Console.WriteLine("Reportes encontrados:");
            foreach (var key in configs.Keys)
                Console.WriteLine(key);

            return configs;
        }

        public async Task<(bool Success, string Message, string? PdfPath)> GenerateReportAsync(string reportName, string? jsonFilePath, Dictionary<string, object>? sqlParams = null)
        {
            if (!_reportConfigs.TryGetValue(reportName, out var config))
                return (false, $"No existe configuración para el reporte '{reportName}'", null);

            var templatePath = Path.Combine(_templatesFolder, config.TemplateFile);

            if (!File.Exists(templatePath))
                return (false, $"Plantilla no encontrada en {templatePath}", null);

            JsonObject? jsonObject = null;

            if (!string.IsNullOrEmpty(jsonFilePath))
            {
                if (!File.Exists(jsonFilePath))
                    return (false, $"Archivo JSON no encontrado en {jsonFilePath}", null);

                var jsonString = await File.ReadAllTextAsync(jsonFilePath);
                var jsonNode = JsonNode.Parse(jsonString);
                jsonObject = jsonNode as JsonObject;
                if (jsonObject == null)
                    return (false, "El JSON no es un objeto válido.", null);
            }
            else if (sqlParams != null)
            {
                // Aquí puedes implementar la lógica para obtener datos desde SQL
                // jsonObject = await GetDataFromSqlAsync(reportName, sqlParams);
                return (false, "La obtención de datos desde SQL aún no está implementada.", null);
            }
            else
            {
                return (false, "No se proporcionó ni JSON ni parámetros SQL.", null);
            }

            try
            {
                var report = new StiReport();
                report.Load(templatePath);

                RegisterData(report, jsonObject, config);

                report.Dictionary.Databases.Clear();
                report.Dictionary.Synchronize();

                report.Compile();
                report.Render(false);

                var directory = Path.GetDirectoryName(jsonFilePath ?? "tmp") ?? "tmp";
                var pdfFileName = $"{reportName}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                var pdfFullPath = Path.Combine(directory, pdfFileName);

                report.ExportDocument(StiExportFormat.Pdf, pdfFullPath);

                return (true, "Reporte generado correctamente", pdfFullPath);
            }
            catch (Exception ex)
            {
                return (false, $"Error generando reporte: {ex.Message}", null);
            }
        }

        private void RegisterData(StiReport report, JsonObject jsonObject, ReportConfig config)
        {
            Console.WriteLine($"Registrando DataSources para: {string.Join(", ", config.RequiredDataSources)}");
            
            foreach (var dataSourceName in config.RequiredDataSources)
            {
                if (dataSourceName == "Data")
                {
                    // Crea DataTable "Data" con los campos simples del JSON raíz
                    var table = new DataTable("Data");
                    foreach (var prop in jsonObject)
                    {
                        if (prop.Value is not JsonArray)
                            table.Columns.Add(prop.Key, typeof(string));
                    }
                    var row = table.NewRow();
                    foreach (var prop in jsonObject)
                    {
                        if (prop.Value is not JsonArray)
                            row[prop.Key] = prop.Value?.ToString() ?? "";
                    }
                    table.Rows.Add(row);
                    Console.WriteLine($"DataSource 'Data' registrado con {table.Rows.Count} fila(s) y {table.Columns.Count} columna(s)");
                    report.RegData("Data", table);
                }
                else
                {
                    // Usa el mapeo para encontrar el array correspondiente
                    var jsonPath = config.DataSourceMappings.GetValueOrDefault(dataSourceName, dataSourceName);
                    Console.WriteLine($"Buscando DataSource '{dataSourceName}' en path '{jsonPath}'");
                    
                    var dataSourceNode = FindDataSourceByPath(jsonObject, jsonPath);

                    if (dataSourceNode is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject)
                    {
                        var table = CreateTableFromArray(dataSourceName, arr);
                        Console.WriteLine($"DataSource '{dataSourceName}' registrado con {table.Rows.Count} fila(s) y {table.Columns.Count} columna(s)");
                        report.RegData(dataSourceName, table);
                    }
                    else
                    {
                        var emptyTable = new DataTable(dataSourceName);
                        emptyTable.Columns.Add("EmptyColumn", typeof(string));
                        Console.WriteLine($"DataSource '{dataSourceName}' registrado como tabla vacía (no se encontraron datos)");
                        report.RegData(dataSourceName, emptyTable);
                    }
                }
            }
        }

        private JsonNode? FindDataSourceByPath(JsonObject jsonObject, string path)
        {
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            JsonNode? current = jsonObject;
            foreach (var part in parts)
            {
                if (current is JsonObject obj && obj.TryGetPropertyValue(part, out var next))
                    current = next;
                else
                    return null;
            }
            return current;
        }

        private DataTable CreateTableFromArray(string tableName, JsonArray jsonArray)
        {
            var table = new DataTable(tableName);
            if (jsonArray.Count == 0) return table;
            if (jsonArray[0] is not JsonObject firstObject) return table;

            foreach (var prop in firstObject)
                table.Columns.Add(prop.Key, typeof(string));

            foreach (var item in jsonArray)
            {
                if (item is JsonObject obj)
                {
                    var row = table.NewRow();
                    foreach (var prop in obj)
                        row[prop.Key] = prop.Value?.ToString() ?? "";
                    table.Rows.Add(row);
                }
            }
            return table;
        }
    }
}