using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Stimulsoft.Report;
using Stimulsoft.Report.Export;
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
    Console.WriteLine($" - {key}");

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

        // Manejar el nuevo formato: si es arreglo, tomar el primer elemento
        if (jsonNode is JsonArray jsonArray)
        {
            if (jsonArray.Count == 0)
                return (false, "El JSON es un arreglo vacío.", null);

            jsonObject = jsonArray[0] as JsonObject;
            if (jsonObject == null)
                return (false, "El primer elemento del arreglo JSON no es un objeto válido.", null);
        }
        else
        {
            jsonObject = jsonNode as JsonObject;
            if (jsonObject == null)
                return (false, "El JSON no es un objeto válido.", null);
        }
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

    // Limpia conexiones de BD (si la plantilla tenía) y sincroniza el diccionario con los DataTables registrados
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
    Console.WriteLine($"Registrando DataSources requeridos: {string.Join(", ", config.RequiredDataSources ?? new string[0])}");

    foreach (var dataSourceName in config.RequiredDataSources ?? new string[0])
    {
    if (string.Equals(dataSourceName, "Data", StringComparison.OrdinalIgnoreCase))
    {
    // Crea DataTable "Data" con los campos simples (no arreglos ni objetos) del JSON raíz
    var table = CreateSingleRowTableFromFlatObject("Data", jsonObject);
    Console.WriteLine($"DataSource 'Data' registrado con {table.Rows.Count} fila(s) y {table.Columns.Count} columna(s)");
    report.RegData("Data", table);
    continue;
    }

    // Obtiene el path desde el mapping (o usa el mismo nombre)
    var jsonPath = config.DataSourceMappings != null && config.DataSourceMappings.TryGetValue(dataSourceName, out var mappedPath)
    ? mappedPath
    : dataSourceName;

    Console.WriteLine($"Buscando DataSource '{dataSourceName}' en path '{jsonPath}'");

    var node = GetJsonNodeByPath(jsonObject, jsonPath);

    // Si no se encuentra, registra tabla vacía predecible
    if (node is null)
    {
    Console.WriteLine($"Path '{jsonPath}' no encontrado. Registrando tabla vacía para '{dataSourceName}'.");
    report.RegData(dataSourceName, CreateEmptyTable(dataSourceName));
    continue;
    }

    // Arreglo de objetos
    if (node is JsonArray arr)
    {
    if (arr.Count == 0)
    {
    Console.WriteLine($"Arreglo vacío en '{jsonPath}'. Registrando tabla vacía para '{dataSourceName}'.");
    report.RegData(dataSourceName, CreateEmptyTable(dataSourceName));
    continue;
    }

    // Arreglo de objetos
    if (arr[0] is JsonObject)
    {
    var table = CreateTableFromArrayOfObjects(dataSourceName, arr);
    Console.WriteLine($"DataSource '{dataSourceName}' (array de objetos) registrado con {table.Rows.Count} fila(s) y {table.Columns.Count} columna(s)");
    report.RegData(dataSourceName, table);
    continue;
    }

    // Arreglo de primitivos (string, number, bool)
    var primitiveTable = CreateTableFromPrimitiveArray(dataSourceName, arr);
    Console.WriteLine($"DataSource '{dataSourceName}' (array de primitivos) registrado con {primitiveTable.Rows.Count} fila(s) y {primitiveTable.Columns.Count} columna(s)");
    report.RegData(dataSourceName, primitiveTable);
    continue;
    }

    // Objeto: una sola fila
    if (node is JsonObject objNode)
    {
    var table = CreateSingleRowTableFromObject(dataSourceName, objNode);
    Console.WriteLine($"DataSource '{dataSourceName}' (objeto) registrado con {table.Rows.Count} fila(s) y {table.Columns.Count} columna(s)");
    report.RegData(dataSourceName, table);
    continue;
    }

    // Primitivo: lo convertimos en una tabla de una sola fila y columna "Value"
    var singleValueTable = CreateSingleValueTable(dataSourceName, node);
    Console.WriteLine($"DataSource '{dataSourceName}' (primitivo) registrado con {singleValueTable.Rows.Count} fila(s) y {singleValueTable.Columns.Count} columna(s)");
    report.RegData(dataSourceName, singleValueTable);
    }
    }

    // Navega por paths tipo "A.B[0].C" con índices de arreglo opcionales
    private JsonNode? GetJsonNodeByPath(JsonObject root, string path)
    {
    if (string.IsNullOrWhiteSpace(path)) return root;

    var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
    JsonNode? current = root;

    foreach (var part in parts)
    {
    if (current is null) return null;

    // Soporta índices tipo "Items[0]" o "Items[10]"
    var (propName, indexOpt) = ParsePartWithIndex(part);

    if (current is JsonObject obj)
    {
    if (!obj.TryGetPropertyValue(propName, out var next))
    return null;

    if (indexOpt.HasValue)
    {
    if (next is JsonArray arr)
    {
    var idx = indexOpt.Value;
    if (idx < 0 || idx >= arr.Count) return null;
    current = arr[idx];
    }
    else
    {
    return null;
    }
    }
    else
    {
    current = next;
    }
    }
    else if (current is JsonArray arrFromCurrent)
    {
    // Cuando la parte es solo un índice, ej: "[0]"
    if (propName.Length == 0 && indexOpt.HasValue)
    {
    var idx = indexOpt.Value;
    if (idx < 0 || idx >= arrFromCurrent.Count) return null;
    current = arrFromCurrent[idx];
    }
    else
    {
    // No se puede navegar un nombre de propiedad sobre un array sin índice
    return null;
    }
    }
    else
    {
    // Primitivo, no se puede navegar más
    return null;
    }
    }

    return current;
    }

    private (string propName, int? index) ParsePartWithIndex(string part)
    {
    // Casos válidos:
    //  - "Items" -> ("Items", null)
    //  - "Items[0]" -> ("Items", 0)
    //  - "[0]" -> ("", 0)
    var name = part;
    int? index = null;

    var openIdx = part.IndexOf('[');
    var closeIdx = part.IndexOf(']');

    if (openIdx >= 0 && closeIdx > openIdx)
    {
    var idxStr = part.Substring(openIdx + 1, closeIdx - openIdx - 1);
    if (int.TryParse(idxStr, out var parsed))
    index = parsed;

    name = openIdx == 0 ? "" : part.Substring(0, openIdx);
    }

    return (name, index);
    }

    private DataTable CreateEmptyTable(string tableName)
    {
    var dt = new DataTable(tableName);
    dt.Columns.Add("Empty", typeof(string));
    return dt;
    }

    private DataTable CreateSingleValueTable(string tableName, JsonNode valueNode)
    {
    var dt = new DataTable(tableName);
    dt.Columns.Add("Value", typeof(string));
    var row = dt.NewRow();
    row["Value"] = valueNode?.ToString() ?? "";
    dt.Rows.Add(row);
    return dt;
    }

    private DataTable CreateSingleRowTableFromFlatObject(string tableName, JsonObject jsonObject)
    {
    var dt = new DataTable(tableName);

    // Solo propiedades primitivas del raíz (ignora arrays y objetos anidados)
    foreach (var prop in jsonObject)
    {
    if (prop.Value is not JsonArray && prop.Value is not JsonObject)
    {
    if (!dt.Columns.Contains(prop.Key))
    dt.Columns.Add(prop.Key, typeof(string));
    }
    }

    var row = dt.NewRow();
    foreach (var prop in jsonObject)
    {
    if (prop.Value is not JsonArray && prop.Value is not JsonObject)
    row[prop.Key] = prop.Value?.ToString() ?? "";
    }
    dt.Rows.Add(row);
    return dt;
    }

    private DataTable CreateSingleRowTableFromObject(string tableName, JsonObject obj)
    {
    var dt = new DataTable(tableName);

    foreach (var prop in obj)
    {
    if (!dt.Columns.Contains(prop.Key))
    dt.Columns.Add(prop.Key, typeof(string));
    }

    var row = dt.NewRow();
    foreach (var prop in obj)
    {
    row[prop.Key] = prop.Value?.ToString() ?? "";
    }
    dt.Rows.Add(row);

    return dt;
    }

    private DataTable CreateTableFromArrayOfObjects(string tableName, JsonArray jsonArray)
    {
    var dt = new DataTable(tableName);
    if (jsonArray.Count == 0) return dt;

    // Armar columnas con la unión de todas las keys para ser tolerantes a esquemas flexibles
    var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in jsonArray)
    {
    if (item is JsonObject obj)
    {
    foreach (var prop in obj)
    allKeys.Add(prop.Key);
    }
    }

    foreach (var key in allKeys)
    dt.Columns.Add(key, typeof(string));

    foreach (var item in jsonArray)
    {
    if (item is JsonObject obj)
    {
    var row = dt.NewRow();
    foreach (var key in allKeys)
    {
    obj.TryGetPropertyValue(key, out var value);
    row[key] = value?.ToString() ?? "";
    }
    dt.Rows.Add(row);
    }
    }

    return dt;
    }

    private DataTable CreateTableFromPrimitiveArray(string tableName, JsonArray jsonArray)
    {
    var dt = new DataTable(tableName);
    dt.Columns.Add("Value", typeof(string));

    foreach (var item in jsonArray)
    {
    if (item is not JsonObject && item is not JsonArray)
    {
    var row = dt.NewRow();
    row["Value"] = item?.ToString() ?? "";
    dt.Rows.Add(row);
    }
    }

    return dt;
    }
    }
}