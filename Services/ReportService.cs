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
using Serilog;

namespace StimulsoftReport.Services
{
    /// <summary>
    /// Servicio para generar reportes Stimulsoft a partir de plantillas (.mrt)
    /// y datos en JSON (o, en el futuro, SQL).
    /// </summary>
    public class ReportService
    {
        private readonly string _templatesFolder;
        private readonly string _configsFolder;
        private readonly Dictionary<string, ReportConfig> _reportConfigs;
        private readonly Dictionary<string, int> _idCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object _idLock = new object();

        /// <summary>
        /// Inicializa el servicio de reportes leyendo rutas y configuraciones.
        /// </summary>
        /// <param name="options">Opciones con rutas a Templates y Configs.</param>
        /// <param name="env">Entorno de hosting (no usado actualmente, reservado).</param>
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
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error leyendo config '{File}'", file);
                }
            }

            return configs;
        }

        public async Task<(bool Success, string Message, string? PdfPath)> GenerateReportAsync(string reportName, string? jsonFilePath, Dictionary<string, object>? sqlParams = null)
        {
            Log.Information("Solicitud para generar reporte: '{ReportName}'", reportName);

            if (!_reportConfigs.TryGetValue(reportName, out var config))
            {
                Log.Error("No existe configuración para el reporte '{ReportName}'", reportName);
                return (false, $"No existe configuración para el reporte '{reportName}'", null);
            }

            var templatePath = Path.Combine(_templatesFolder, config.TemplateFile);
            Log.Information("Ruta plantilla: {TemplatePath}", templatePath);

            if (!File.Exists(templatePath))
            {
                Log.Error("Plantilla no encontrada en: {TemplatePath}", templatePath);
                return (false, "Plantilla no encontrada para el reporte.", null);
            }

            JsonNode? jsonNode = null;

            if (!string.IsNullOrEmpty(jsonFilePath))
            {
                Log.Information("Procesando archivo JSON: {JsonFilePath}", jsonFilePath);

                try
                {
                    if (!File.Exists(jsonFilePath))
                    {
                        Log.Error("Archivo JSON no encontrado en: {JsonFilePath}", jsonFilePath);
                        return (false, "Archivo JSON no encontrado.", null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "File.Exists lanzó excepción para '{JsonFilePath}'", jsonFilePath);
                    try
                    {
                        using (var fs = File.Open(jsonFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                        }
                    }
                    catch (UnauthorizedAccessException uex)
                    {
                        Log.Error(uex, "Sin permisos para leer el archivo JSON: {JsonFilePath}", jsonFilePath);
                        return (false, "Permisos insuficientes para leer el archivo JSON.", null);
                    }
                    catch (FileNotFoundException fnf)
                    {
                        Log.Error(fnf, "Archivo JSON no encontrado en: {JsonFilePath}", jsonFilePath);
                        return (false, "Archivo JSON no encontrado.", null);
                    }
                    catch (DirectoryNotFoundException dnf)
                    {
                        Log.Error(dnf, "Directorio no encontrado para la ruta JSON: {JsonFilePath}", jsonFilePath);
                        return (false, "Directorio del archivo JSON no encontrado.", null);
                    }
                    catch (IOException ioEx)
                    {
                        Log.Error(ioEx, "Error de E/S verificando archivo JSON '{JsonFilePath}'", jsonFilePath);
                        return (false, "Error de E/S accediendo al archivo JSON.", null);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error(ex2, "Excepción verificando archivo JSON '{JsonFilePath}'", jsonFilePath);
                        return (false, "Error verificando el archivo JSON.", null);
                    }
                }

                try
                {
                    var jsonString = await File.ReadAllTextAsync(jsonFilePath);
                    try
                    {
                        jsonNode = JsonNode.Parse(jsonString);
                        if (jsonNode == null)
                        {
                            Log.Error("JSON inválido o vacío.");
                            return (false, "JSON inválido o vacío.", null);
                        }
                    }
                    catch (JsonException jex)
                    {
                        Log.Error(jex, "JSON mal formado en '{JsonFilePath}'", jsonFilePath);
                        return (false, $"JSON mal formado: {jex.Message}", null);
                    }
                }
                catch (UnauthorizedAccessException uex)
                {
                    Log.Error(uex, "Sin permisos para leer el archivo JSON: {JsonFilePath}", jsonFilePath);
                    return (false, "Permisos insuficientes para leer el archivo JSON.", null);
                }
                catch (FileNotFoundException fnf)
                {
                    Log.Error(fnf, "Archivo JSON no encontrado en: {JsonFilePath}", jsonFilePath);
                    return (false, "Archivo JSON no encontrado.", null);
                }
                catch (IOException ex)
                {
                    Log.Error(ex, "Error de E/S leyendo el JSON '{JsonFilePath}'", jsonFilePath);
                    return (false, "Error de E/S leyendo el archivo JSON.", null);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Excepción leyendo/parsing JSON '{JsonFilePath}'", jsonFilePath);
                    return (false, "Error leyendo o parseando JSON.", null);
                }
            }
            else if (sqlParams != null)
            {
                Log.Information("Obtención de datos desde SQL no implementada.");
                return (false, "La obtención de datos desde SQL aún no está implementada.", null);
            }
            else
            {
                Log.Error("No se proporcionó ni JSON ni parámetros SQL.");
                return (false, "No se proporcionó ni JSON ni parámetros SQL.", null);
            }

            try
            {
                Log.Information("Cargando plantilla y registrando datos...");
                var report = new StiReport();

                try
                {
                    report.Load(templatePath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "No se pudo cargar la plantilla desde {TemplatePath}", templatePath);
                    return (false, "Error cargando la plantilla del reporte.", null);
                }

                try
                {
                    RegisterData(report, jsonNode, config, reportName);
                    Log.Information("Datos registrados en el reporte.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Excepción registrando datos en el reporte '{ReportName}'", reportName);
                    return (false, $"Error procesando datos del reporte: {ex.Message}", null);
                }

                try
                {
                    report.Dictionary.Databases.Clear();
                    report.Dictionary.Synchronize();
                    Log.Information("Diccionario sincronizado.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error sincronizando diccionario del reporte");
                    return (false, "Error sincronizando diccionario del reporte.", null);
                }

                try
                {
                    report.Compile();
                    Log.Information("Reporte compilado.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error compilando reporte");
                    return (false, "Error compilando el reporte.", null);
                }

                try
                {
                    report.Render(false);
                    Log.Information("Reporte renderizado.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error renderizando reporte");
                    return (false, "Error renderizando el reporte.", null);
                }

                var directory = Path.GetDirectoryName(jsonFilePath ?? "tmp") ?? "tmp";
                var jsonBaseName = Path.GetFileNameWithoutExtension(jsonFilePath ?? reportName);
                var pdfFileName = $"{jsonBaseName}.pdf";
                var pdfFullPath = Path.Combine(directory, pdfFileName);

                try
                {
                    report.ExportDocument(StiExportFormat.Pdf, pdfFullPath);
                    Log.Information("Archivo PDF exportado a: {PdfFullPath}", pdfFullPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error exportando PDF a {PdfFullPath}", pdfFullPath);
                    return (false, "Error exportando el PDF del reporte.", null);
                }

                Log.Information("Reporte generado correctamente.");
                return (true, "Reporte generado correctamente", pdfFullPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Excepción generando reporte");
                return (false, $"Error generando reporte: {ex.Message}", null);
            }
        }

        private void RegisterData(StiReport report, JsonNode? jsonNode, ReportConfig config, string reportName)
        {
            Log.Information("Registrando datos para reporte '{ReportName}'...", reportName);

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
                Log.Warning("Nodo principal '{MainDataSourceName}' no encontrado en JSON. Se registra tabla vacía.", mainDataSourceName);
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
                    var idColumn = mainTable.Columns["Id"];
                    if (idColumn != null)
                    {
                        idColumn.ColumnName = pkColumnName;
                    }
                    else
                    {
                        Log.Warning("La columna 'Id' no existe en mainTable.Columns aunque Contains devolvió true.");
                    }
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
                    ProcessNodeRecursive(prop.Value, prop.Key, i + 1, pkColumnName, null, null, new List<(string, int)>(), createdTables);
                }
            }

            if (string.Equals(reportName, "ReporteCfdiAsimilados", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Information("Aplicando reglas para ReporteCfdiAsimilados");
                    ApplyAsimiladosRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Aplicando reglas para ReporteCfdiAsimilados");
                }
            }

            if (string.Equals(reportName, "ReporteCfdi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Information("Aplicando reglas para ReporteCfdi");
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Aplicando reglas para ReporteCfdi");
                }
            }

            if (string.Equals(reportName, "ReporteA3o", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Information("Aplicando reglas para ReporteA3o");
                    ApplyReporteA3oRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Aplicando reglas para ReporteA3o");
                }
            }

            if (string.Equals(reportName, "ReporteCFDIMc", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Information("Aplicando reglas para ReporteCFDIMc");
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Aplicando reglas para ReporteCFDIMc");
                }
            }

            if (string.Equals(reportName, "ReporteResLugarTrabajo", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Log.Information("Aplicando reglas para ReporteResLugarTrabajo");
                    ApplyReporteLugarTrabajo(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Aplicando reglas para ReporteResLugarTrabajo");
                }
            }

            foreach (var kvp in createdTables)
            {
                report.RegData(kvp.Key, kvp.Value);
            }

            Log.Information("Datos registrados para reporte '{ReportName}'.", reportName);
        }

        private void ApplyAsimiladosRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Log.Information("Aplicando reglas para ReporteCfdiAsimilados");

            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Log.Warning("Tablas necesarias no encontradas para ReporteCfdiAsimilados");
                return;
            }

            if (!createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable))
            {
                otrosPagosTable = new DataTable("OtrosPagos");
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
                otrosPagosTable.Columns.Add("someColumn", typeof(string));
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
            Log.Information("Aplicando reglas para ReporteCfdi");

            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Log.Warning("Tablas necesarias no encontradas para ReporteCfdi");
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

        private void ApplyReporteLugarTrabajo(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Log.Information("Aplicando reglas para ReporteResLugarTrabajo");

            if (!createdTables.TryGetValue("DeduccionesSumario", out var deduccionesSumarioTable) ||
                !createdTables.TryGetValue("PercepcionesSumario", out var percepcionesSumarioTable))
            {
                Log.Warning("Tablas necesarias no encontradas para ReporteResLugarTrabajo");
                return;
            }

            if (!deduccionesSumarioTable.Columns.Contains(pkColumnName))
                deduccionesSumarioTable.Columns.Add(pkColumnName, typeof(int));
            if (!percepcionesSumarioTable.Columns.Contains(pkColumnName))
                percepcionesSumarioTable.Columns.Add(pkColumnName, typeof(int));

            foreach (DataRow mainRow in mainTable.Rows)
            {
                var mainIdObj = mainRow[pkColumnName];
                if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                int mainId = Convert.ToInt32(mainIdObj);

                int dsCount = deduccionesSumarioTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int psCount = percepcionesSumarioTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                int maxCount = Math.Max(dsCount, psCount);

                if (dsCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesSumarioTable, maxCount - dsCount, pkColumnName, mainId);
                if (psCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesSumarioTable, maxCount - psCount, pkColumnName, mainId);
            }
        }

        private void ApplyReporteA3oRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Log.Information("Aplicando reglas especiales para ReporteA3o (balance filas Percepciones, OtrosPagos y Deducciones) por registro");

            try
            {
                createdTables.TryGetValue("Percepciones", out var percepcionesTable);
                createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable);
                createdTables.TryGetValue("Deducciones", out var deduccionesTable);

                if (percepcionesTable == null)
                {
                    Log.Error("Tabla 'Percepciones' no encontrada.");
                    return;
                }
                if (otrosPagosTable == null)
                {
                    Log.Error("Tabla 'OtrosPagos' no encontrada.");
                    return;
                }
                if (deduccionesTable == null)
                {
                    Log.Error("Tabla 'Deducciones' no encontrada.");
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
                        Log.Debug("[mainId={MainId}] Agregando {Count} filas vacías a 'Percepciones'", mainId, maxCount - pCount);
                        AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                    }
                    if (oCount < maxCount)
                    {
                        Log.Debug("[mainId={MainId}] Agregando {Count} filas vacías a 'OtrosPagos'", mainId, maxCount - oCount);
                        AddEmptyRowsWithPk(otrosPagosTable, maxCount - oCount, pkColumnName, mainId);
                    }
                    if (dCount < maxCount)
                    {
                        Log.Debug("[mainId={MainId}] Agregando {Count} filas vacías a 'Deducciones'", mainId, maxCount - dCount);
                        AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);
                    }
                }

                Log.Information("Reglas especiales para ReporteA3o aplicadas por registro.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error en ApplyReporteA3oRules");
            }
        }

        private void ProcessNodeRecursive(JsonNode? node, string nodeName, int mainId, string mainPkColumnName, int? parentRowId, string? parentNodeName, List<(string name, int id)> ancestors, Dictionary<string, DataTable> createdTables)
        {
            if (node == null) return;

            ancestors ??= new List<(string name, int id)>();

            if (node is JsonArray arr)
            {
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromArray(nodeName, arr);
                    createdTables[nodeName] = table;
                    Log.Debug("Creada tabla {TableName} con columnas iniciales.", nodeName);
                }
                else
                {
                    var allKeys = CollectKeysFromArray(arr);
                    foreach (var k in allKeys)
                        if (!table.Columns.Contains(k))
                            table.Columns.Add(k, typeof(string));
                }

                if (!table.Columns.Contains(mainPkColumnName))
                    table.Columns.Add(mainPkColumnName, typeof(int));
                var ownIdCol = $"{nodeName}Id";
                if (!table.Columns.Contains(ownIdCol))
                    table.Columns.Add(ownIdCol, typeof(int));
                foreach (var anc in ancestors)
                {
                    var ancCol = $"{anc.name}Id";
                    if (!table.Columns.Contains(ancCol))
                        table.Columns.Add(ancCol, typeof(int));
                }

                lock (_idLock)
                {
                    if (!_idCounters.ContainsKey(nodeName))
                    {
                        try
                        {
                            var existingMax = table.Rows.Cast<DataRow>()
                                .Where(r => r[ownIdCol] != DBNull.Value)
                                .Select(r => Convert.ToInt32(r[ownIdCol]))
                                .DefaultIfEmpty(0)
                                .Max();
                            _idCounters[nodeName] = existingMax;
                        }
                        catch
                        {
                            _idCounters[nodeName] = table.Rows.Count;
                        }
                    }
                }

                foreach (var element in arr)
                {
                    if (element is JsonObject childObj)
                    {
                        var row = table.NewRow();

                        foreach (DataColumn col in table.Columns)
                        {
                            var colName = col.ColumnName;
                            if (colName == mainPkColumnName || colName == ownIdCol) continue;
                            if (ancestors.Any(a => $"{a.name}Id" == colName)) continue;

                            if (childObj.TryGetPropertyValue(colName, out var val))
                            {
                                var converted = ConvertJsonNodeToColumnValue(val, col.DataType);
                                row[colName] = converted ?? DBNull.Value;
                            }
                            else
                            {
                                row[colName] = (col.DataType == typeof(string)) ? "" : DBNull.Value;
                            }
                        }

                        row[mainPkColumnName] = mainId;

                        foreach (var anc in ancestors)
                        {
                            var ancCol = $"{anc.name}Id";
                            row[ancCol] = anc.id;
                        }

                        if (parentRowId.HasValue && !string.IsNullOrEmpty(parentNodeName) && !ancestors.Any(a => a.name == parentNodeName))
                        {
                            var parentCol = $"{parentNodeName}Id";
                            if (!table.Columns.Contains(parentCol))
                                table.Columns.Add(parentCol, typeof(int));
                            row[parentCol] = parentRowId.Value;
                        }

                        int newId;
                        lock (_idLock)
                        {
                            _idCounters[nodeName] = _idCounters.GetValueOrDefault(nodeName) + 1;
                            newId = _idCounters[nodeName];
                        }
                        row[ownIdCol] = newId;

                        table.Rows.Add(row);

                        var newAncestors = new List<(string name, int id)>(ancestors) { (nodeName, newId) };

                        foreach (var p in childObj)
                        {
                            if (p.Value is JsonArray || p.Value is JsonObject)
                                ProcessNodeRecursive(p.Value, p.Key, mainId, mainPkColumnName, newId, nodeName, newAncestors, createdTables);
                        }
                    }
                    else
                    {
                        if (!table.Columns.Contains("Value"))
                            table.Columns.Add("Value", typeof(string));

                        var row = table.NewRow();
                        if (table.Columns.Contains("Value"))
                            row["Value"] = element?.ToString() ?? "";

                        row[mainPkColumnName] = mainId;

                        foreach (var anc in ancestors)
                        {
                            var ancCol = $"{anc.name}Id";
                            row[ancCol] = anc.id;
                        }

                        var ownIdColLocal = $"{nodeName}Id";
                        int newId;
                        lock (_idLock)
                        {
                            _idCounters[nodeName] = _idCounters.GetValueOrDefault(nodeName) + 1;
                            newId = _idCounters[nodeName];
                        }
                        row[ownIdColLocal] = newId;

                        table.Rows.Add(row);
                    }
                }
            }
            else if (node is JsonObject obj)
            {
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromObject(nodeName, obj);
                    createdTables[nodeName] = table;
                    Log.Debug("Creada tabla {TableName} desde objeto.", nodeName);
                }
                else
                {
                    foreach (var p in obj)
                        if (!table.Columns.Contains(p.Key))
                            table.Columns.Add(p.Key, typeof(string));
                }

                if (!table.Columns.Contains(mainPkColumnName))
                    table.Columns.Add(mainPkColumnName, typeof(int));
                var ownIdCol = $"{nodeName}Id";
                if (!table.Columns.Contains(ownIdCol))
                    table.Columns.Add(ownIdCol, typeof(int));
                foreach (var anc in ancestors)
                {
                    var ancCol = $"{anc.name}Id";
                    if (!table.Columns.Contains(ancCol))
                        table.Columns.Add(ancCol, typeof(int));
                }

                lock (_idLock)
                {
                    if (!_idCounters.ContainsKey(nodeName))
                    {
                        try
                        {
                            var existingMax = table.Rows.Cast<DataRow>()
                                .Where(r => r[ownIdCol] != DBNull.Value)
                                .Select(r => Convert.ToInt32(r[ownIdCol]))
                                .DefaultIfEmpty(0)
                                .Max();
                            _idCounters[nodeName] = existingMax;
                        }
                        catch
                        {
                            _idCounters[nodeName] = table.Rows.Count;
                        }
                    }
                }

                var row = table.NewRow();
                foreach (DataColumn col in table.Columns)
                {
                    if (col.ColumnName == mainPkColumnName) continue;
                    if (col.ColumnName == ownIdCol) continue;
                    if (ancestors.Any(a => $"{a.name}Id" == col.ColumnName)) continue;

                    if (obj.TryGetPropertyValue(col.ColumnName, out var val))
                    {
                        var converted = ConvertJsonNodeToColumnValue(val, col.DataType);
                        row[col.ColumnName] = converted ?? DBNull.Value;
                    }
                    else
                    {
                        row[col.ColumnName] = (col.DataType == typeof(string)) ? "" : DBNull.Value;
                    }
                }

                row[mainPkColumnName] = mainId;
                foreach (var anc in ancestors)
                {
                    var ancCol = $"{anc.name}Id";
                    row[ancCol] = anc.id;
                }

                if (parentRowId.HasValue && !string.IsNullOrEmpty(parentNodeName) && !ancestors.Any(a => a.name == parentNodeName))
                {
                    var parentCol = $"{parentNodeName}Id";
                    if (!table.Columns.Contains(parentCol))
                        table.Columns.Add(parentCol, typeof(int));
                    row[parentCol] = parentRowId.Value;
                }

                int newId;
                lock (_idLock)
                {
                    _idCounters[nodeName] = _idCounters.GetValueOrDefault(nodeName) + 1;
                    newId = _idCounters[nodeName];
                }
                row[ownIdCol] = newId;

                table.Rows.Add(row);

                var newAncestors = new List<(string name, int id)>(ancestors) { (nodeName, newId) };
                foreach (var p in obj)
                {
                    if (p.Value is JsonArray || p.Value is JsonObject)
                        ProcessNodeRecursive(p.Value, p.Key, mainId, mainPkColumnName, newId, nodeName, newAncestors, createdTables);
                }
            }
            else
            {
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = new DataTable(nodeName);
                    table.Columns.Add("Value", typeof(string));
                    createdTables[nodeName] = table;
                }

                if (!table.Columns.Contains(mainPkColumnName))
                    table.Columns.Add(mainPkColumnName, typeof(int));
                var ownIdCol2 = $"{nodeName}Id";
                if (!table.Columns.Contains(ownIdCol2))
                    table.Columns.Add(ownIdCol2, typeof(int));
                foreach (var anc in ancestors)
                {
                    var ancCol = $"{anc.name}Id";
                    if (!table.Columns.Contains(ancCol))
                        table.Columns.Add(ancCol, typeof(int));
                }

                var row = table.NewRow();
                row["Value"] = node.ToString() ?? "";
                row[mainPkColumnName] = mainId;
                foreach (var anc in ancestors)
                {
                    var ancCol = $"{anc.name}Id";
                    row[ancCol] = anc.id;
                }

                int newId;
                lock (_idLock)
                {
                    _idCounters[nodeName] = _idCounters.GetValueOrDefault(nodeName) + 1;
                    newId = _idCounters[nodeName];
                }
                row[ownIdCol2] = newId;
                table.Rows.Add(row);
            }
        }

        private DataTable BuildTableSchemaFromArray(string tableName, JsonArray arr)
        {
            var dt = new DataTable(tableName);
            var keys = CollectKeysFromArray(arr);
            foreach (var k in keys)
                dt.Columns.Add(k, typeof(string));

            var ownIdCol = $"{tableName}Id";
            if (!dt.Columns.Contains(ownIdCol))
                dt.Columns.Add(ownIdCol, typeof(int));

            lock (_idLock)
            {
                if (!_idCounters.ContainsKey(tableName))
                    _idCounters[tableName] = 0;
            }

            return dt;
        }

        private DataTable BuildTableSchemaFromObject(string tableName, JsonObject obj)
        {
            var dt = new DataTable(tableName);
            foreach (var p in obj)
            {
                dt.Columns.Add(p.Key, typeof(string));
            }

            var ownIdCol = $"{tableName}Id";
            if (!dt.Columns.Contains(ownIdCol))
                dt.Columns.Add(ownIdCol, typeof(int));

            lock (_idLock)
            {
                if (!_idCounters.ContainsKey(tableName))
                    _idCounters[tableName] = 0;
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

            var ownIdCol = $"{table.TableName}Id";
            if (!table.Columns.Contains(ownIdCol))
                table.Columns.Add(ownIdCol, typeof(int));

            lock (_idLock)
            {
                if (!_idCounters.TryGetValue(table.TableName, out var counter))
                {
                    try
                    {
                        counter = table.Rows.Cast<DataRow>()
                            .Where(r => r[ownIdCol] != DBNull.Value)
                            .Select(r => Convert.ToInt32(r[ownIdCol]))
                            .DefaultIfEmpty(0)
                            .Max();
                    }
                    catch
                    {
                        counter = table.Rows.Count;
                    }
                    _idCounters[table.TableName] = counter;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var row = table.NewRow();

                foreach (DataColumn col in table.Columns)
                {
                    if (col.ColumnName == pkColumnName) continue;
                    if (col.ColumnName == ownIdCol) continue;

                    if (col.DataType == typeof(string))
                        row[col.ColumnName] = "";
                    else if (col.DataType.IsValueType)
                        row[col.ColumnName] = Activator.CreateInstance(col.DataType) ?? DBNull.Value;
                    else
                        row[col.ColumnName] = DBNull.Value;
                }

                row[pkColumnName] = parentId;

                int newId;
                lock (_idLock)
                {
                    _idCounters[table.TableName] = _idCounters.GetValueOrDefault(table.TableName) + 1;
                    newId = _idCounters[table.TableName];
                }
                row[ownIdCol] = newId;

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

        private object ConvertJsonNodeToColumnValue(JsonNode? node, Type targetType)
        {
            if (node == null) return DBNull.Value;

            var s = node.ToString() ?? "";

            if (string.IsNullOrEmpty(s))
            {
                if (targetType == typeof(string)) return "";
                return DBNull.Value;
            }

            try
            {
                if (targetType == typeof(string)) return s;

                if (targetType == typeof(int))
                {
                    if (int.TryParse(s, out var i)) return i;
                    return DBNull.Value;
                }
                if (targetType == typeof(long))
                {
                    if (long.TryParse(s, out var l)) return l;
                    return DBNull.Value;
                }
                if (targetType == typeof(decimal))
                {
                    if (decimal.TryParse(s, out var d)) return d;
                    return DBNull.Value;
                }
                if (targetType == typeof(double))
                {
                    if (double.TryParse(s, out var d)) return d;
                    return DBNull.Value;
                }
                if (targetType == typeof(bool))
                {
                    if (bool.TryParse(s, out var b)) return b;
                    if (s == "0") return false;
                    if (s == "1") return true;
                    return DBNull.Value;
                }
                if (targetType == typeof(DateTime))
                {
                    if (DateTime.TryParse(s, out var dt)) return dt;
                    return DBNull.Value;
                }

                return Convert.ChangeType(s, targetType);
            }
            catch
            {
                return DBNull.Value;
            }
        }
    }
}