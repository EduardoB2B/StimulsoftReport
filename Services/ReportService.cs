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

    public ReportService(IOptions<ReportSettings> options, IHostEnvironment env)
    {
    _templatesFolder = options.Value.TemplatesFolder?.Trim() ?? "";
    _configsFolder = options.Value.ConfigsFolder?.Trim() ?? "";

    Console.WriteLine($"[ReportService] TemplatesFolder = {_templatesFolder}");
    Console.WriteLine($"[ReportService] ConfigsFolder   = {_configsFolder}");

    _reportConfigs = LoadReportConfigs(_configsFolder);
    }

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
    }
    }

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

    JsonNode? jsonNode = null;

    if (!string.IsNullOrEmpty(jsonFilePath))
    {
    if (!File.Exists(jsonFilePath))
    return (false, $"Archivo JSON no encontrado en {jsonFilePath}", null);

    var jsonString = await File.ReadAllTextAsync(jsonFilePath);
    jsonNode = JsonNode.Parse(jsonString);
    if (jsonNode == null)
    return (false, "JSON inválido o vacío.", null);
    }
    else if (sqlParams != null)
    {
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

    RegisterData(report, jsonNode, config, reportName);

    report.Dictionary.Databases.Clear();
    report.Dictionary.Synchronize();

    report.Compile();
    report.Render(false);

    var directory = Path.GetDirectoryName(jsonFilePath ?? "tmp") ?? "tmp";
    var jsonBaseName = Path.GetFileNameWithoutExtension(jsonFilePath ?? reportName);
    var pdfFileName = $"{jsonBaseName}.pdf";
    var pdfFullPath = Path.Combine(directory, pdfFileName);

    report.ExportDocument(StiExportFormat.Pdf, pdfFullPath);

    return (true, "Reporte generado correctamente", pdfFullPath);
    }
    catch (Exception ex)
    {
    return (false, $"Error generando reporte: {ex.Message}", null);
    }
    }

    private void RegisterData(StiReport report, JsonNode? jsonNode, ReportConfig config, string reportName)
    {
    Console.WriteLine($"Registrando DataSources requeridos: {string.Join(", ", config.RequiredDataSources ?? Array.Empty<string>())}");

    if (jsonNode == null)
    {
    Console.WriteLine("JSON nulo, no se puede registrar data.");
    return;
    }

    JsonArray? rootArray = null;
    JsonObject? rootObject = null;

    if (jsonNode is JsonArray arr)
    rootArray = arr;
    else if (jsonNode is JsonObject obj)
    rootObject = obj;
    else
    {
    Console.WriteLine("El JSON raíz no es ni objeto ni arreglo.");
    return;
    }

    string mainDataSourceName = "";
    string mainJsonPath = "";

    if (config.DataSourceMappings != null)
    {
    var mainMapping = config.DataSourceMappings.FirstOrDefault(kvp => string.IsNullOrEmpty(kvp.Value));
    if (!string.IsNullOrEmpty(mainMapping.Key))
    {
    mainDataSourceName = mainMapping.Key;
    mainJsonPath = mainMapping.Value;
    }
    }

    if (string.IsNullOrEmpty(mainDataSourceName))
    {
    mainDataSourceName = config.RequiredDataSources?.FirstOrDefault() ?? "";
    mainJsonPath = config.DataSourceMappings != null && config.DataSourceMappings.TryGetValue(mainDataSourceName, out var mappedPath)
    ? mappedPath
    : mainDataSourceName;
    }

    Console.WriteLine($"DataSource principal detectado: '{mainDataSourceName}' con path '{mainJsonPath}'");

    JsonNode? mainNode = null;
    if (string.IsNullOrEmpty(mainJsonPath))
    {
    mainNode = jsonNode;
    }
    else if (rootObject != null)
    {
    mainNode = GetJsonNodeByPath(rootObject, mainJsonPath);
    }
    else if (rootArray != null)
    {
    mainNode = null;
    }

    if (mainNode == null)
    {
    Console.WriteLine($"No se encontró el nodo principal para '{mainDataSourceName}' en el path '{mainJsonPath}'. Registrando tabla vacía.");
    report.RegData(mainDataSourceName, CreateEmptyTable(mainDataSourceName));
    return;
    }

    JsonArray mainArray;
    if (mainNode is JsonArray mainArr)
    mainArray = mainArr;
    else if (mainNode is JsonObject mainObj)
    mainArray = new JsonArray(mainObj);
    else
    {
    Console.WriteLine($"El nodo principal '{mainDataSourceName}' no es ni objeto ni arreglo. Registrando tabla vacía.");
    report.RegData(mainDataSourceName, CreateEmptyTable(mainDataSourceName));
    return;
    }

    // Crear la tabla principal
    var mainTable = CreateTableFromArrayOfObjects(mainDataSourceName, mainArray);
    var pkColumnName = $"{mainDataSourceName}Id";

    // Si existe columna "Id" renombrarla a pkColumnName; si no existe pkColumnName crearla.
    if (mainTable.Columns.Contains("Id") && !mainTable.Columns.Contains(pkColumnName))
    {
    try
    {
    mainTable.Columns["Id"].ColumnName = pkColumnName;
    }
    catch
    {
    if (!mainTable.Columns.Contains(pkColumnName))
    mainTable.Columns.Add(pkColumnName, typeof(int));
    for (int r = 0; r < mainTable.Rows.Count; r++)
    mainTable.Rows[r][pkColumnName] = DBNull.Value;
    }
    }
    else
    {
    if (!mainTable.Columns.Contains(pkColumnName))
    mainTable.Columns.Add(pkColumnName, typeof(int));
    }

    // Poblamos PK con 1..N
    for (int i = 0; i < mainTable.Rows.Count; i++)
    {
    mainTable.Rows[i][pkColumnName] = i + 1;
    }

    Console.WriteLine($"Tabla principal '{mainDataSourceName}' creada con {mainTable.Rows.Count} filas y {mainTable.Columns.Count} columnas.");

    // LOG por cada registro raíz con su ID
    Console.WriteLine($"Detalle de registros en '{mainDataSourceName}' (después de asignar {pkColumnName}):");
    for (int i = 0; i < mainTable.Rows.Count; i++)
    {
    var row = mainTable.Rows[i];
    var parts = new List<string>();
    parts.Add($"{pkColumnName}={row[pkColumnName]}");
    foreach (DataColumn col in mainTable.Columns)
    {
    if (string.Equals(col.ColumnName, pkColumnName, StringComparison.OrdinalIgnoreCase)) continue;
    var val = row[col.ColumnName];
    parts.Add($"{col.ColumnName}={(val == null || val == DBNull.Value ? "" : val.ToString())}");
    }
    Console.WriteLine($" - Registro #{i + 1}: {string.Join("; ", parts)}");
    }

    // Registrar principal
    report.RegData(mainDataSourceName, mainTable);

    // Diccionario de tablas hijas / descendientes
    var createdTables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

    // Procesar recursivamente cada elemento raíz para crear/llenar tablas hijas/nietas...
    for (int i = 0; i < mainArray.Count; i++)
    {
    var item = mainArray[i];
    if (item is not JsonObject itemObj)
    continue;

    // Para cada propiedad del objeto raíz, procesar su nodo (array/obj/primitive)
    foreach (var prop in itemObj)
    {
    ProcessNodeRecursive(prop.Value, prop.Key, i + 1, pkColumnName, createdTables);
    }
    }

    // Aplicar reglas especiales si es ReporteCfdiAsimilados
    if (string.Equals(reportName, "ReporteCfdiAsimilados", StringComparison.OrdinalIgnoreCase))
    {
        ApplyAsimiladosRules(createdTables, pkColumnName, mainTable);
    }

    // Registrar tablas hijas en el reporte
    foreach (var kvp in createdTables)
    {
    Console.WriteLine($"Registrando tabla hija/desc '{kvp.Key}' con {kvp.Value.Rows.Count} filas y {kvp.Value.Columns.Count} columnas.");
    report.RegData(kvp.Key, kvp.Value);
    }
    }

    // Procesa un nodo (JsonNode) recursivamente, creando/llenando tablas en createdTables.
    // parentId es el valor del PK del registro raíz (main PK).
    private void ProcessNodeRecursive(JsonNode? node, string nodeName, int parentId, string pkColumnName, Dictionary<string, DataTable> createdTables)
    {
    if (node == null) return;

    // Si es arreglo
    if (node is JsonArray arr)
    {
    // Asegurar tabla para este nodeName
    if (!createdTables.TryGetValue(nodeName, out var table))
    {
    table = BuildTableSchemaFromArray(nodeName, arr);
    // asegurar columna FK
    if (!table.Columns.Contains(pkColumnName))
    table.Columns.Add(pkColumnName, typeof(int));
    createdTables[nodeName] = table;
    }
    else
    {
    // Si aparecen nuevas keys en este array, agregarlas a la tabla existente
    var allKeys = CollectKeysFromArray(arr);
    foreach (var k in allKeys)
    if (!table.Columns.Contains(k))
    table.Columns.Add(k, typeof(string));
    }

    // Agregar filas para cada elemento del array
    foreach (var element in arr)
    {
    if (element is JsonObject childObj)
    {
    var row = table.NewRow();
    foreach (DataColumn col in table.Columns)
    {
    if (col.ColumnName == pkColumnName) continue;

    if (childObj.TryGetPropertyValue(col.ColumnName, out var val))
    row[col.ColumnName] = val?.ToString() ?? "";
    else
    row[col.ColumnName] = "";
    }
    row[pkColumnName] = parentId;
    table.Rows.Add(row);

    // Procesar propiedades dentro de este objeto (nietos)
    foreach (var p in childObj)
    {
    // Si la propiedad es la que alimentó la tabla (nodeName) la ignoramos aquí
    // porque ya procesamos sus elementos; sin embargo los subnodos dentro del elemento se procesan ahora.
    if (p.Value is JsonArray || p.Value is JsonObject)
    ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
    }
    }
    else
    {
    // Array de primitivos
    if (!table.Columns.Contains("Value"))
    table.Columns.Add("Value", typeof(string));
    var row = table.NewRow();
    if (table.Columns.Contains("Value"))
    row["Value"] = element?.ToString() ?? "";
    row[pkColumnName] = parentId;
    table.Rows.Add(row);
    }
    }
    }
    else if (node is JsonObject obj)
    {
    // Nodo único: crear/usar tabla nodeName con 1 fila por padre
    if (!createdTables.TryGetValue(nodeName, out var table))
    {
    table = BuildTableSchemaFromObject(nodeName, obj);
    if (!table.Columns.Contains(pkColumnName))
    table.Columns.Add(pkColumnName, typeof(int));
    createdTables[nodeName] = table;
    }
    else
    {
    // Asegurar columnas nuevas
    foreach (var p in obj)
    if (!table.Columns.Contains(p.Key))
    table.Columns.Add(p.Key, typeof(string));
    }

    var row = table.NewRow();
    foreach (DataColumn col in table.Columns)
    {
    if (col.ColumnName == pkColumnName) continue;
    if (obj.TryGetPropertyValue(col.ColumnName, out var val))
    row[col.ColumnName] = val?.ToString() ?? "";
    else
    row[col.ColumnName] = "";
    }
    row[pkColumnName] = parentId;
    table.Rows.Add(row);

    // Procesar propiedades internas (nietos)
    foreach (var p in obj)
    {
    if (p.Value is JsonArray || p.Value is JsonObject)
    ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
    }
    }
    else
    {
    // Primitivo -> opcional: crear tabla nodeName con columna Value
    if (!createdTables.TryGetValue(nodeName, out var table))
    {
    table = new DataTable(nodeName);
    table.Columns.Add("Value", typeof(string));
    table.Columns.Add(pkColumnName, typeof(int));
    createdTables[nodeName] = table;
    }

    var row = table.NewRow();
    row["Value"] = node.ToString() ?? "";
    row[pkColumnName] = parentId;
    table.Rows.Add(row);
    }
    }

    // Construye esquema inicial para una tabla basada en un array de objetos (unión de keys)
    private DataTable BuildTableSchemaFromArray(string tableName, JsonArray arr)
    {
    var dt = new DataTable(tableName);
    var keys = CollectKeysFromArray(arr);
    foreach (var k in keys)
    dt.Columns.Add(k, typeof(string));
    return dt;
    }

    // Construye esquema para un objeto único
    private DataTable BuildTableSchemaFromObject(string tableName, JsonObject obj)
    {
    var dt = new DataTable(tableName);
    foreach (var p in obj)
    {
    if (p.Value is not JsonArray && p.Value is not JsonObject)
    dt.Columns.Add(p.Key, typeof(string));
    else
    dt.Columns.Add(p.Key, typeof(string)); // si es object/array también agregamos la columna por si hay valores primitivos relevantes
    }
    return dt;
    }

    // Recolecta keys de todos los objetos dentro de un array
    private HashSet<string> CollectKeysFromArray(JsonArray arr)
    {
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in arr)
    {
    if (item is JsonObject jo)
    {
    foreach (var p in jo)
    keys.Add(p.Key);
    }
    }
    return keys;
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
    if (propName.Length == 0 && indexOpt.HasValue)
    {
    var idx = indexOpt.Value;
    if (idx < 0 || idx >= arrFromCurrent.Count) return null;
    current = arrFromCurrent[idx];
    }
    else
    {
    return null;
    }
    }
    else
    {
    return null;
    }
    }

    return current;
    }

    private (string propName, int? index) ParsePartWithIndex(string part)
    {
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
// Aplica las reglas especiales para ReporteCfdiAsimilados (balance por cada registro principal)
    private void ApplyAsimiladosRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
    {
        Console.WriteLine("Aplicando reglas especiales para ReporteCfdiAsimilados (por registro)");

        createdTables.TryGetValue("Percepciones", out var percepcionesTable);
        createdTables.TryGetValue("Deducciones", out var deduccionesTable);
        createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable);

        // Asegurar estructura base de OtrosPagos si no existe (se completará/ajustará por registro)
        if (otrosPagosTable == null)
        {
            var newOtros = new DataTable("OtrosPagos");
            if (percepcionesTable != null)
            {
                foreach (DataColumn c in percepcionesTable.Columns)
                    newOtros.Columns.Add(c.ColumnName, c.DataType);
            }
            else
            {
                newOtros.Columns.Add("TipoOtroPago", typeof(string));
                newOtros.Columns.Add("Clave", typeof(string));
                newOtros.Columns.Add("Importe", typeof(string));
            }
            if (!newOtros.Columns.Contains(pkColumnName)) newOtros.Columns.Add(pkColumnName, typeof(int));
            otrosPagosTable = newOtros;
            createdTables["OtrosPagos"] = otrosPagosTable;
            Console.WriteLine("Creada estructura inicial de OtrosPagos");
        }
        else
        {
            if (!otrosPagosTable.Columns.Contains(pkColumnName))
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
        }

        // Asegurar PK column en percepciones/deducciones
        if (percepcionesTable != null && !percepcionesTable.Columns.Contains(pkColumnName))
            percepcionesTable.Columns.Add(pkColumnName, typeof(int));
        if (deduccionesTable != null && !deduccionesTable.Columns.Contains(pkColumnName))
            deduccionesTable.Columns.Add(pkColumnName, typeof(int));

        // Para cada registro principal (mainTable) balancear por su PK
        foreach (DataRow mainRow in mainTable.Rows)
        {
            var mainIdObj = mainRow[pkColumnName];
            if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
            int mainId = Convert.ToInt32(mainIdObj);

            int pCount = percepcionesTable?.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId) ?? 0;
            int dCount = deduccionesTable?.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId) ?? 0;

            if (percepcionesTable != null && dCount > pCount)
                AddEmptyRowsWithPk(percepcionesTable, dCount - pCount, pkColumnName, mainId);
            else if (deduccionesTable != null && pCount > dCount)
                AddEmptyRowsWithPk(deduccionesTable, pCount - dCount, pkColumnName, mainId);

            // OtrosPagos: al menos 2 filas por mainId
            int oCount = otrosPagosTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
            if (oCount < 2)
                AddEmptyRowsWithPk(otrosPagosTable, 2 - oCount, pkColumnName, mainId);
        }

        Console.WriteLine("Reglas especiales aplicadas por registro.");
    }

    // Helper que agrega filas vacías con pk = parentId
    private void AddEmptyRowsWithPk(DataTable table, int count, string pkColumnName, int parentId)
    {
        if (count <= 0) return;

        // Asegurar que la tabla tenga alguna columna para rellenar; si sólo tiene pk, crear columna "Empty"
        if (table.Columns.Count == 0)
            table.Columns.Add("Empty", typeof(string));
        if (!table.Columns.Contains(pkColumnName))
            table.Columns.Add(pkColumnName, typeof(int));

        for (int i = 0; i < count; i++)
        {
            var row = table.NewRow();
            foreach (DataColumn col in table.Columns)
            {
                if (col.ColumnName == pkColumnName) continue;
                row[col.ColumnName] = ""; // vacío para todo
            }
            row[pkColumnName] = parentId;
            table.Rows.Add(row);
        }
    }
    }
}