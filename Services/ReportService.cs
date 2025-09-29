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

                int numberOfCopies = 1; // siempre por defecto 1

                try
                {
                    JsonNode? duplicaNode = null;

                    if (jsonNode is JsonObject objRoot)
                    {
                        // Caso: raíz es objeto
                        objRoot.TryGetPropertyValue("duplica", out duplicaNode);
                    }
                    else if (jsonNode is JsonArray arrRoot && arrRoot.FirstOrDefault() is JsonObject firstObj)
                    {
                        // Caso: raíz es array -> tomamos el primer objeto
                        firstObj.TryGetPropertyValue("duplica", out duplicaNode);
                    }

                    if (duplicaNode != null && int.TryParse(duplicaNode.ToString(), out int duplicaFlag))
                    {
                        // Si duplica=1 => imprimir 2 copias, si duplica=0 => solo 1
                        numberOfCopies = duplicaFlag == 1 ? 2 : 1;
                    }

                    if (report.Pages.Count > 0)
                    {
                        report.Pages[0].NumberOfCopies = numberOfCopies;
                        Console.WriteLine($"[ReportService] NumberOfCopies configurado en {numberOfCopies}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error seteando NumberOfCopies dinámico: {ex.Message}");
                }

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
            if (report == null) return;
            if (jsonNode == null) return;

            JsonArray? rootArray = null;
            JsonObject? rootObject = null;

            if (jsonNode is JsonArray arr)
                rootArray = arr;
            else if (jsonNode is JsonObject obj)
                rootObject = obj;
            else
                return;

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
                report.RegData(mainDataSourceName, CreateEmptyTable(mainDataSourceName));
                return;
            }

            var mainTable = CreateTableFromArrayOfObjects(mainDataSourceName, mainArray);
            var pkColumnName = $"{mainDataSourceName}Id";

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

            for (int i = 0; i < mainTable.Rows.Count; i++)
            {
                mainTable.Rows[i][pkColumnName] = i + 1;
            }

            report.RegData(mainDataSourceName, mainTable);

            var createdTables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < mainArray.Count; i++)
            {
                var item = mainArray[i];
                if (item is not JsonObject itemObj)
                    continue;

                foreach (var prop in itemObj)
                {
                    ProcessNodeRecursive(prop.Value, prop.Key, i + 1, pkColumnName, createdTables);
                }
            }

            if (string.Equals(reportName, "ReporteCfdiAsimilados", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ApplyAsimiladosRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error aplicando reglas para ReporteCfdiAsimilados: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteCfdi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error aplicando reglas para ReporteCfdi: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteA3o", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ApplyReporteA3oRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error aplicando reglas para ReporteA3o: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteCFDIMc", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error aplicando reglas para ReporteCFDIMc: {ex.Message}");
                }
            }

            foreach (var kvp in createdTables)
            {
                if (string.Equals(reportName, "ReporteA3o", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(kvp.Key, "Percepciones", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kvp.Key, "OtrosPagos", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kvp.Key, "Deducciones", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[ReporteA3o] Tabla '{kvp.Key}' filas: {kvp.Value.Rows.Count}, columnas: {kvp.Value.Columns.Count}");
                }
                report.RegData(kvp.Key, kvp.Value);
            }
        }

        private void ApplyAsimiladosRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            // Aquí va la lógica específica para ReporteCfdiAsimilados
            // Por ejemplo, balancear Percepciones y Deducciones por registro, y asegurar OtrosPagos mínimo 2 filas
            Console.WriteLine("Aplicando reglas para ReporteCfdiAsimilados");

            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Console.WriteLine("Tablas necesarias no encontradas para ReporteCfdiAsimilados");
                return;
            }

            if (!createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable))
            {
                // Crear tabla OtrosPagos vacía con 2 filas si no existe
                otrosPagosTable = new DataTable("OtrosPagos");
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
                otrosPagosTable.Columns.Add("someColumn", typeof(string)); // Ajusta columnas según necesidad
                createdTables["OtrosPagos"] = otrosPagosTable;
            }

            if (!otrosPagosTable.Columns.Contains(pkColumnName))
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));

            foreach (DataRow mainRow in mainTable.Rows)
            {
                var mainIdObj = mainRow[pkColumnName];
                if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                int mainId = Convert.ToInt32(mainIdObj);

                int pCount = percepcionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int dCount = deduccionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int oCount = otrosPagosTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                int maxCount = Math.Max(pCount, dCount);

                if (pCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                if (dCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);

                if (oCount < 2)
                    AddEmptyRowsWithPk(otrosPagosTable, 2 - oCount, pkColumnName, mainId);
            }
        }

        private void ApplyReporteCfdiRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            // Aquí va la lógica específica para ReporteCfdi
            // Por ejemplo, balancear Percepciones y Deducciones por registro
            Console.WriteLine("Aplicando reglas para ReporteCfdi");

            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Console.WriteLine("Tablas necesarias no encontradas para ReporteCfdi");
                return;
            }

            if (!percepcionesTable.Columns.Contains(pkColumnName))
                percepcionesTable.Columns.Add(pkColumnName, typeof(int));
            if (!deduccionesTable.Columns.Contains(pkColumnName))
                deduccionesTable.Columns.Add(pkColumnName, typeof(int));

            foreach (DataRow mainRow in mainTable.Rows)
            {
                var mainIdObj = mainRow[pkColumnName];
                if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                int mainId = Convert.ToInt32(mainIdObj);

                int pCount = percepcionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int dCount = deduccionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                int maxCount = Math.Max(pCount, dCount);

                if (pCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                if (dCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);
            }
        }

        private void ApplyReporteA3oRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Console.WriteLine("Aplicando reglas especiales para ReporteA3o (balance filas Percepciones, OtrosPagos y Deducciones) por registro");

            try
            {
                createdTables.TryGetValue("Percepciones", out var percepcionesTable);
                createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable);
                createdTables.TryGetValue("Deducciones", out var deduccionesTable);

                if (percepcionesTable == null)
                {
                    Console.WriteLine("ERROR: Tabla 'Percepciones' no encontrada.");
                    return;
                }
                if (otrosPagosTable == null)
                {
                    Console.WriteLine("ERROR: Tabla 'OtrosPagos' no encontrada.");
                    return;
                }
                if (deduccionesTable == null)
                {
                    Console.WriteLine("ERROR: Tabla 'Deducciones' no encontrada.");
                    return;
                }

                if (!percepcionesTable.Columns.Contains(pkColumnName))
                    percepcionesTable.Columns.Add(pkColumnName, typeof(int));
                if (!otrosPagosTable.Columns.Contains(pkColumnName))
                    otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
                if (!deduccionesTable.Columns.Contains(pkColumnName))
                    deduccionesTable.Columns.Add(pkColumnName, typeof(int));

                foreach (DataRow mainRow in mainTable.Rows)
                {
                    var mainIdObj = mainRow[pkColumnName];
                    if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                    int mainId = Convert.ToInt32(mainIdObj);

                    int pCount = percepcionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                    int oCount = otrosPagosTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                    int dCount = deduccionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                    int maxCount = Math.Max(pCount, Math.Max(oCount, dCount));

                    if (pCount < maxCount)
                    {
                        Console.WriteLine($"[mainId={mainId}] Agregando {maxCount - pCount} filas vacías a 'Percepciones'");
                        AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                    }
                    if (oCount < maxCount)
                    {
                        Console.WriteLine($"[mainId={mainId}] Agregando {maxCount - oCount} filas vacías a 'OtrosPagos'");
                        AddEmptyRowsWithPk(otrosPagosTable, maxCount - oCount, pkColumnName, mainId);
                    }
                    if (dCount < maxCount)
                    {
                        Console.WriteLine($"[mainId={mainId}] Agregando {maxCount - dCount} filas vacías a 'Deducciones'");
                        AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);
                    }
                }

                Console.WriteLine("Reglas especiales para ReporteA3o aplicadas por registro.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ApplyReporteA3oRules: {ex.Message}");
            }
        }

        private void ProcessNodeRecursive(JsonNode? node, string nodeName, int parentId, string pkColumnName, Dictionary<string, DataTable> createdTables)
        {
            if (node == null) return;

            if (node is JsonArray arr)
            {
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromArray(nodeName, arr);
                    if (!table.Columns.Contains(pkColumnName))
                        table.Columns.Add(pkColumnName, typeof(int));
                    createdTables[nodeName] = table;
                }
                else
                {
                    var allKeys = CollectKeysFromArray(arr);
                    foreach (var k in allKeys)
                        if (!table.Columns.Contains(k))
                            table.Columns.Add(k, typeof(string));
                }

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

                        foreach (var p in childObj)
                        {
                            if (p.Value is JsonArray || p.Value is JsonObject)
                                ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
                        }
                    }
                    else
                    {
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
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromObject(nodeName, obj);
                    if (!table.Columns.Contains(pkColumnName))
                        table.Columns.Add(pkColumnName, typeof(int));
                    createdTables[nodeName] = table;
                }
                else
                {
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

                foreach (var p in obj)
                {
                    if (p.Value is JsonArray || p.Value is JsonObject)
                        ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
                }
            }
            else
            {
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

        private DataTable BuildTableSchemaFromArray(string tableName, JsonArray arr)
        {
            var dt = new DataTable(tableName);
            var keys = CollectKeysFromArray(arr);
            foreach (var k in keys)
                dt.Columns.Add(k, typeof(string));
            return dt;
        }

        private DataTable BuildTableSchemaFromObject(string tableName, JsonObject obj)
        {
            var dt = new DataTable(tableName);
            foreach (var p in obj)
            {
                dt.Columns.Add(p.Key, typeof(string));
            }
            return dt;
        }

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

        private void AddEmptyRowsWithPk(DataTable table, int count, string pkColumnName, int parentId)
        {
            if (count <= 0) return;

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

                    if (col.DataType == typeof(string))
                        row[col.ColumnName] = "";
                    else if (col.DataType.IsValueType)
                        row[col.ColumnName] = Activator.CreateInstance(col.DataType) ?? DBNull.Value;
                    else
                        row[col.ColumnName] = DBNull.Value;
                }
                row[pkColumnName] = parentId;
                table.Rows.Add(row);
            }
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
    }
}