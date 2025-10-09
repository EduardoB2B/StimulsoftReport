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
    /// <summary>
    /// Servicio para generar reportes Stimulsoft a partir de plantillas (.mrt)
    /// y datos en JSON (o, en el futuro, SQL).
    /// </summary>
    public class ReportService
    {
        private readonly string _templatesFolder;
        private readonly string _configsFolder;
        private readonly Dictionary<string, ReportConfig> _reportConfigs;

        /// <summary>
        /// Inicializa el servicio de reportes leyendo rutas y configuraciones.
        /// </summary>
        /// <param name="options">Opciones con rutas a Templates y Configs.</param>
        /// <param name="env">Entorno de hosting (no usado actualmente, reservado).</param>
        public ReportService(IOptions<ReportSettings> options, IHostEnvironment env)
        {
            // Normaliza y guarda rutas de carpetas
            _templatesFolder = options.Value.TemplatesFolder?.Trim() ?? "";
            _configsFolder = options.Value.ConfigsFolder?.Trim() ?? "";

            // Carga configuraciones de reportes (por nombre de archivo .json)
            _reportConfigs = LoadReportConfigs(_configsFolder);
        }

        /// <summary>
        /// Carga todas las configuraciones de reportes desde la carpeta indicada.
        /// Cada archivo .json representa la configuración de un reporte.
        /// </summary>
        /// <param name="folder">Carpeta que contiene archivos .json de configuración.</param>
        /// <returns>Diccionario de nombreReporte -> ReportConfig.</returns>
        private Dictionary<string, ReportConfig> LoadReportConfigs(string folder)
        {
            var configs = new Dictionary<string, ReportConfig>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folder))
                return configs;

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    // Lee y deserializa configuración
                    var json = File.ReadAllText(file);
                    var config = JsonSerializer.Deserialize<ReportConfig>(json);
                    if (config != null)
                    {
                        // Usa el nombre de archivo (sin extensión) como clave del reporte
                        var reportName = Path.GetFileNameWithoutExtension(file);
                        configs[reportName] = config;
                    }
                }
                catch
                {
                    // Silencia errores de deserialización o lectura para no romper el arranque
                    // (Opcional: loguear detalle)
                }
            }

            return configs;
        }

        /// <summary>
        /// Genera un reporte PDF desde una plantilla Stimulsoft y datos en JSON.
        /// </summary>
        /// <param name="reportName">Nombre del reporte (clave para buscar su configuración).</param>
        /// <param name="jsonFilePath">Ruta del archivo JSON de datos (opcional si se usará SQL).</param>
        /// <param name="sqlParams">Parámetros para obtención por SQL (no implementado).</param>
        /// <returns>Tupla con éxito, mensaje y ruta del PDF generado (si aplica).</returns>
        public async Task<(bool Success, string Message, string? PdfPath)> GenerateReportAsync(string reportName, string? jsonFilePath, Dictionary<string, object>? sqlParams = null)
        {
            Console.WriteLine($"[Inicio] Solicitud para generar reporte: '{reportName}'");

            if (!_reportConfigs.TryGetValue(reportName, out var config))
            {
                Console.WriteLine($"[Error] No existe configuración para el reporte '{reportName}'");
                return (false, $"No existe configuración para el reporte '{reportName}'", null);
            }

            var templatePath = Path.Combine(_templatesFolder, config.TemplateFile);
            Console.WriteLine($"[Info] Ruta plantilla: {templatePath}");

            if (!File.Exists(templatePath))
            {
                Console.WriteLine($"[Error] Plantilla no encontrada en {templatePath}");
                return (false, $"Plantilla no encontrada en {templatePath}", null);
            }

            JsonNode? jsonNode = null;

            if (!string.IsNullOrEmpty(jsonFilePath))
            {
                Console.WriteLine($"[Info] Procesando archivo JSON: {jsonFilePath}");

                if (!File.Exists(jsonFilePath))
                {
                    Console.WriteLine($"[Error] Archivo JSON no encontrado en {jsonFilePath}");
                    return (false, $"Archivo JSON no encontrado en {jsonFilePath}", null);
                }

                var jsonString = await File.ReadAllTextAsync(jsonFilePath);
                jsonNode = JsonNode.Parse(jsonString);
                if (jsonNode == null)
                {
                    Console.WriteLine("[Error] JSON inválido o vacío.");
                    return (false, "JSON inválido o vacío.", null);
                }
            }
            else if (sqlParams != null)
            {
                Console.WriteLine("[Info] Obtención de datos desde SQL no implementada.");
                return (false, "La obtención de datos desde SQL aún no está implementada.", null);
            }
            else
            {
                Console.WriteLine("[Error] No se proporcionó ni JSON ni parámetros SQL.");
                return (false, "No se proporcionó ni JSON ni parámetros SQL.", null);
            }

            try
            {
                Console.WriteLine("[Info] Cargando plantilla y registrando datos...");
                var report = new StiReport();
                report.Load(templatePath);

                RegisterData(report, jsonNode, config, reportName);
                Console.WriteLine("[Info] Datos registrados en el reporte.");

                report.Dictionary.Databases.Clear();
                report.Dictionary.Synchronize();
                Console.WriteLine("[Info] Diccionario sincronizado.");

                report.Compile();
                Console.WriteLine("[Info] Reporte compilado.");

                report.Render(false);
                Console.WriteLine("[Info] Reporte renderizado.");

                var directory = Path.GetDirectoryName(jsonFilePath ?? "tmp") ?? "tmp";
                var jsonBaseName = Path.GetFileNameWithoutExtension(jsonFilePath ?? reportName);
                var pdfFileName = $"{jsonBaseName}.pdf";
                var pdfFullPath = Path.Combine(directory, pdfFileName);

                report.ExportDocument(StiExportFormat.Pdf, pdfFullPath);
                Console.WriteLine($"[Info] Archivo PDF exportado a: {pdfFullPath}");

                Console.WriteLine("[Fin] Reporte generado correctamente.");
                return (true, "Reporte generado correctamente", pdfFullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Excepción generando reporte: {ex.Message}");
                return (false, $"Error generando reporte: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Transforma el JSON de entrada a DataTables y los registra en el reporte según la configuración.
        /// Aplica reglas específicas por tipo de reporte para balancear filas entre tablas relacionadas.
        /// </summary>
        /// <param name="report">Instancia de StiReport.</param>
        /// <param name="jsonNode">Nodo raíz de los datos JSON.</param>
        /// <param name="config">Configuración del reporte (mapeos, requeridos, etc.).</param>
        /// <param name="reportName">Nombre lógico del reporte para reglas específicas.</param>
        private void RegisterData(StiReport report, JsonNode? jsonNode, ReportConfig config, string reportName)
        {
            Console.WriteLine($"[Info] Registrando datos para reporte '{reportName}'...");

            if (report == null) return;
            if (jsonNode == null) return;

            // Detecta si el JSON raíz es objeto o arreglo
            JsonArray? rootArray = null;
            JsonObject? rootObject = null;

            if (jsonNode is JsonArray arr)
                rootArray = arr;
            else if (jsonNode is JsonObject obj)
                rootObject = obj;
            else
                return;

            // Determina el DataSource principal y su ruta dentro del JSON (si existe)
            string mainDataSourceName = "";
            string mainJsonPath = "";

            if (config.DataSourceMappings != null)
            {
                // Busca mapeo principal (cuando el valor esté vacío, se interpreta como raíz)
                var mainMapping = config.DataSourceMappings.FirstOrDefault(kvp => string.IsNullOrEmpty(kvp.Value));
                if (!string.IsNullOrEmpty(mainMapping.Key))
                {
                    mainDataSourceName = mainMapping.Key;
                    mainJsonPath = mainMapping.Value;
                }
            }

            // Fallback si no se definió explícitamente
            if (string.IsNullOrEmpty(mainDataSourceName))
            {
                mainDataSourceName = config.RequiredDataSources?.FirstOrDefault() ?? "";
                mainJsonPath = config.DataSourceMappings != null && config.DataSourceMappings.TryGetValue(mainDataSourceName, out var mappedPath)
                    ? mappedPath
                    : mainDataSourceName;
            }

            // Resuelve el nodo principal desde el JSON
            JsonNode? mainNode = null;
            if (string.IsNullOrEmpty(mainJsonPath))
            {
                // Si no hay ruta, usa el JSON tal cual
                mainNode = jsonNode;
            }
            else if (rootObject != null)
            {
                mainNode = GetJsonNodeByPath(rootObject, mainJsonPath);
            }
            else if (rootArray != null)
            {
                // No implementado: navegación por rutas sobre raíz que es array
                mainNode = null;
            }

            // Si no se encuentra nodo principal, registra tabla vacía para no romper el reporte
            if (mainNode == null)
            {
                Console.WriteLine($"[Warning] Nodo principal '{mainDataSourceName}' no encontrado en JSON. Se registra tabla vacía.");
                report.RegData(mainDataSourceName, CreateEmptyTable(mainDataSourceName));
                return;
            }

            // Normaliza a arreglo para construir tabla principal
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
            var pkColumnName = $"{mainDataSourceName}Id"; // PK sintética por fila del principal

            // Ajusta/crea columna PK consistente
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

            // Asigna PK incremental 1..N
            for (int i = 0; i < mainTable.Rows.Count; i++)
            {
                mainTable.Rows[i][pkColumnName] = i + 1;
            }

            // Registra DataSource principal
            report.RegData(mainDataSourceName, mainTable);

            // Diccionario de tablas generadas (subnodos)
            var createdTables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

            // Recorre cada item del principal y procesa subpropiedades recursivamente
            for (int i = 0; i < mainArray.Count; i++)
            {
                var item = mainArray[i];
                if (item is not JsonObject itemObj)
                    continue;

                foreach (var prop in itemObj)
                {
                    // Cada propiedad compleja (objeto/array) se convierte en tabla hija
                    ProcessNodeRecursive(prop.Value, prop.Key, i + 1, pkColumnName, createdTables);
                }
            }

            // Aplica reglas específicas por reporte (balanceo y relleno de filas)
            if (string.Equals(reportName, "ReporteCfdiAsimilados", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Console.WriteLine("[Info] Aplicando reglas para ReporteCfdiAsimilados");
                    ApplyAsimiladosRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Aplicando reglas para ReporteCfdiAsimilados: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteCfdi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Console.WriteLine("[Info] Aplicando reglas para ReporteCfdi");
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Aplicando reglas para ReporteCfdi: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteA3o", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Console.WriteLine("[Info] Aplicando reglas para ReporteA3o");
                    ApplyReporteA3oRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Aplicando reglas para ReporteA3o: {ex.Message}");
                }
            }



            if (string.Equals(reportName, "ReporteCFDIMc", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Console.WriteLine("[Info] Aplicando reglas para ReporteCFDIMc");
                    ApplyReporteCfdiRules(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Aplicando reglas para ReporteCFDIMc: {ex.Message}");
                }
            }

            if (string.Equals(reportName, "ReporteResLugarTrabajo", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Console.WriteLine("[Info] Aplicando reglas para ReporteResLugarTrabajo");
                    ApplyReporteLugarTrabajo(createdTables, pkColumnName, mainTable);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Aplicando reglas para ReporteResLugarTrabajo: {ex.Message}");
                }
            }

            // Registra todas las tablas creadas en el diccionario del reporte
            foreach (var kvp in createdTables)
            {
                if (string.Equals(reportName, "ReporteA3o", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(kvp.Key, "Percepciones", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "OtrosPagos", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "Deducciones", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[Info] [ReporteA3o] Tabla '{kvp.Key}' filas: {kvp.Value.Rows.Count}, columnas: {kvp.Value.Columns.Count}");
                }
                report.RegData(kvp.Key, kvp.Value);
            }

            foreach (var kvp in createdTables)
            {
                if (string.Equals(reportName, "ReporteResLugarTrabajo", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(kvp.Key, "Percepciones", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kvp.Key, "Deducciones", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kvp.Key, "DeduccionesSumario", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kvp.Key, "PercepcionesSumario", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[Info] [ReporteResLugarTrabajo] Tabla '{kvp.Key}' filas: {kvp.Value.Rows.Count}, columnas: {kvp.Value.Columns.Count}");
                }
                report.RegData(kvp.Key, kvp.Value);
            }

            Console.WriteLine($"[Info] Datos registrados para reporte '{reportName}'.");
        }

        /// <summary>
        /// Reglas específicas para ReporteCfdiAsimilados:
        /// - Balancea filas entre Percepciones y Deducciones por registro.
        /// - Asegura que OtrosPagos tenga al menos 2 filas por registro.
        /// </summary>
        private void ApplyAsimiladosRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Console.WriteLine("Aplicando reglas para ReporteCfdiAsimilados");

            // Obtiene tablas clave
            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Console.WriteLine("Tablas necesarias no encontradas para ReporteCfdiAsimilados");
                return;
            }

            // Garantiza existencia de OtrosPagos y columna PK
            if (!createdTables.TryGetValue("OtrosPagos", out var otrosPagosTable))
            {
                otrosPagosTable = new DataTable("OtrosPagos");
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
                // Columna de ejemplo; ajustar según diseño necesario
                otrosPagosTable.Columns.Add("someColumn", typeof(string));
                createdTables["OtrosPagos"] = otrosPagosTable;
            }

            if (!otrosPagosTable.Columns.Contains(pkColumnName))
                otrosPagosTable.Columns.Add(pkColumnName, typeof(int));

            // Recorre cada registro principal para balancear y completar mínimos
            foreach (DataRow mainRow in mainTable.Rows)
            {
                var mainIdObj = mainRow[pkColumnName];
                if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                int mainId = Convert.ToInt32(mainIdObj);

                int pCount = percepcionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int dCount = deduccionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int oCount = otrosPagosTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                int maxCount = Math.Max(pCount, dCount);

                // Rellena filas vacías para alinear altura visual
                if (pCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                if (dCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);

                // Mínimo 2 filas en OtrosPagos
                if (oCount < 2)
                    AddEmptyRowsWithPk(otrosPagosTable, 2 - oCount, pkColumnName, mainId);
            }
        }

        /// <summary>
        /// Reglas para ReporteCfdi y ReporteCFDIMc:
        /// balancea el número de filas entre Percepciones y Deducciones por registro.
        /// </summary>
        private void ApplyReporteCfdiRules(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Console.WriteLine("Aplicando reglas para ReporteCfdi");

            // Verifica tablas requeridas
            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable))
            {
                Console.WriteLine("Tablas necesarias no encontradas para ReporteCfdi");
                return;
            }

            // Garantiza que ambas tablas tengan columna PK
            if (!percepcionesTable.Columns.Contains(pkColumnName))
                percepcionesTable.Columns.Add(pkColumnName, typeof(int));
            if (!deduccionesTable.Columns.Contains(pkColumnName))
                deduccionesTable.Columns.Add(pkColumnName, typeof(int));

            // Balancea filas por cada registro principal
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

        /// <summary>
        /// Reglas para ReporteResLugarTrabajo:
        /// balancea filas entre Percepciones, Deducciones y sus sumarios por registro.
        /// </summary>
        private void ApplyReporteLugarTrabajo(Dictionary<string, DataTable> createdTables, string pkColumnName, DataTable mainTable)
        {
            Console.WriteLine("Aplicando reglas para ReporteResLugarTrabajo");

            // Verifica existencia de todas las tablas necesarias
            if (!createdTables.TryGetValue("Percepciones", out var percepcionesTable) ||
                !createdTables.TryGetValue("Deducciones", out var deduccionesTable) ||
                !createdTables.TryGetValue("DeduccionesSumario", out var deduccionesSumarioTable) ||
                !createdTables.TryGetValue("PercepcionesSumario", out var percepcionesSumarioTable))
            {
                Console.WriteLine("Tablas necesarias no encontradas para ReporteResLugarTrabajo");
                return;
            }

            // Asegura presencia de PK en todas
            if (!percepcionesTable.Columns.Contains(pkColumnName))
                percepcionesTable.Columns.Add(pkColumnName, typeof(int));
            if (!deduccionesTable.Columns.Contains(pkColumnName))
                deduccionesTable.Columns.Add(pkColumnName, typeof(int));
            if (!deduccionesSumarioTable.Columns.Contains(pkColumnName))
                deduccionesSumarioTable.Columns.Add(pkColumnName, typeof(int));
            if (!percepcionesSumarioTable.Columns.Contains(pkColumnName))
                percepcionesSumarioTable.Columns.Add(pkColumnName, typeof(int));

            // Balancea filas al máximo entre las cuatro tablas por registro
            foreach (DataRow mainRow in mainTable.Rows)
            {
                var mainIdObj = mainRow[pkColumnName];
                if (mainIdObj == null || mainIdObj == DBNull.Value) continue;
                int mainId = Convert.ToInt32(mainIdObj);

                int pCount = percepcionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int dCount = deduccionesTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int dsCount = deduccionesSumarioTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);
                int psCount = percepcionesSumarioTable.AsEnumerable().Count(r => r.Field<int>(pkColumnName) == mainId);

                int maxCount = Math.Max(Math.Max(pCount, dCount), Math.Max(dsCount, psCount));

                if (pCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesTable, maxCount - pCount, pkColumnName, mainId);
                if (dCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesTable, maxCount - dCount, pkColumnName, mainId);
                if (dsCount < maxCount)
                    AddEmptyRowsWithPk(deduccionesSumarioTable, maxCount - dsCount, pkColumnName, mainId);
                if (psCount < maxCount)
                    AddEmptyRowsWithPk(percepcionesSumarioTable, maxCount - psCount, pkColumnName, mainId);
            }
        }

        /// <summary>
        /// Reglas especiales para ReporteA3o:
        /// balancea filas entre Percepciones, OtrosPagos y Deducciones por registro.
        /// </summary>
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

                // Garantiza PK en todas las tablas
                if (!percepcionesTable.Columns.Contains(pkColumnName))
                    percepcionesTable.Columns.Add(pkColumnName, typeof(int));
                if (!otrosPagosTable.Columns.Contains(pkColumnName))
                    otrosPagosTable.Columns.Add(pkColumnName, typeof(int));
                if (!deduccionesTable.Columns.Contains(pkColumnName))
                    deduccionesTable.Columns.Add(pkColumnName, typeof(int));

                // Balancea al máximo entre las tres
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

        /// <summary>
        /// Convierte un nodo JSON a tablas (DataTables) de forma recursiva.
        /// - Arrays: generan filas; Objetos: generan columnas.
        /// - Se agrega una PK sintética para relacionar con el principal.
        /// </summary>
        private void ProcessNodeRecursive(JsonNode? node, string nodeName, int parentId, string pkColumnName, Dictionary<string, DataTable> createdTables)
        {
            if (node == null) return;

            if (node is JsonArray arr)
            {
                // Crea/ajusta esquema de tabla para el array actual
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromArray(nodeName, arr);
                    if (!table.Columns.Contains(pkColumnName))
                        table.Columns.Add(pkColumnName, typeof(int));
                    createdTables[nodeName] = table;
                }
                else
                {
                    // Agrega columnas nuevas que aparezcan en otros elementos del array
                    var allKeys = CollectKeysFromArray(arr);
                    foreach (var k in allKeys)
                        if (!table.Columns.Contains(k))
                            table.Columns.Add(k, typeof(string));
                }

                // Itera cada elemento del array
                foreach (var element in arr)
                {
                    if (element is JsonObject childObj)
                    {
                        var row = table.NewRow();

                        // Copia propiedades conocidas a columnas; rellena faltantes con ""
                        foreach (DataColumn col in table.Columns)
                        {
                            if (col.ColumnName == pkColumnName) continue;

                            if (childObj.TryGetPropertyValue(col.ColumnName, out var val))
                                row[col.ColumnName] = val?.ToString() ?? "";
                            else
                                row[col.ColumnName] = "";
                        }

                        // Asigna relación con el registro principal
                        row[pkColumnName] = parentId;
                        table.Rows.Add(row);

                        // Procesa propiedades anidadas (objetos/arrays)
                        foreach (var p in childObj)
                        {
                            if (p.Value is JsonArray || p.Value is JsonObject)
                                ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
                        }
                    }
                    else
                    {
                        // Elemento "simple" dentro del array: crea columna "Value"
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
                // Crea/ajusta esquema para objeto
                if (!createdTables.TryGetValue(nodeName, out var table))
                {
                    table = BuildTableSchemaFromObject(nodeName, obj);
                    if (!table.Columns.Contains(pkColumnName))
                        table.Columns.Add(pkColumnName, typeof(int));
                    createdTables[nodeName] = table;
                }
                else
                {
                    // Agrega columnas nuevas si aparecen propiedades no vistas
                    foreach (var p in obj)
                        if (!table.Columns.Contains(p.Key))
                            table.Columns.Add(p.Key, typeof(string));
                }

                // Inserta fila con valores del objeto
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

                // Procesa propiedades anidadas
                foreach (var p in obj)
                {
                    if (p.Value is JsonArray || p.Value is JsonObject)
                        ProcessNodeRecursive(p.Value, p.Key, parentId, pkColumnName, createdTables);
                }
            }
            else
            {
                // Nodo simple (string/num/bool): crea tabla con columna Value
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

        /// <summary>
        /// Construye esquema de tabla a partir de un array de objetos JSON (union de claves).
        /// </summary>
        private DataTable BuildTableSchemaFromArray(string tableName, JsonArray arr)
        {
            var dt = new DataTable(tableName);
            var keys = CollectKeysFromArray(arr);
            foreach (var k in keys)
                dt.Columns.Add(k, typeof(string));
            return dt;
        }

        /// <summary>
        /// Construye esquema de tabla a partir de un objeto JSON (propiedades -> columnas).
        /// </summary>
        private DataTable BuildTableSchemaFromObject(string tableName, JsonObject obj)
        {
            var dt = new DataTable(tableName);
            foreach (var p in obj)
            {
                dt.Columns.Add(p.Key, typeof(string));
            }
            return dt;
        }

        /// <summary>
        /// Obtiene el conjunto de claves presentes en todos los objetos de un array.
        /// </summary>
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

        /// <summary>
        /// Navega un JSON por una ruta con notación de puntos y opcionalmente índices [i].
        /// Ej: "Emisor.Domicilio[0].Calle"
        /// </summary>
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
                    // Caso especial: parte sin nombre y con índice apunta directo al array actual
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

        /// <summary>
        /// Parsea una parte de ruta con índice opcional. Ej: "Domicilio[0]" => ("Domicilio", 0)
        /// </summary>
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

        /// <summary>
        /// Crea una tabla vacía con una columna placeholder para registrar en el reporte.
        /// </summary>
        private DataTable CreateEmptyTable(string tableName)
        {
            var dt = new DataTable(tableName);
            dt.Columns.Add("Empty", typeof(string));
            return dt;
        }

        /// <summary>
        /// Agrega filas vacías a una tabla conservando tipos de columnas y asignando la PK.
        /// Útil para balancear alturas visuales entre bandas/tablas.
        /// </summary>
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

                // Coloca valores por defecto por tipo
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

        /// <summary>
        /// Crea un DataTable a partir de un array de objetos JSON:
        /// - Columnas: unión de todas las claves presentes.
        /// - Filas: valores por objeto.
        /// </summary>
        private DataTable CreateTableFromArrayOfObjects(string tableName, JsonArray jsonArray)
        {
            var dt = new DataTable(tableName);
            if (jsonArray.Count == 0) return dt;

            // Determina columnas por unión de claves
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

            // Crea filas con valores por clave; faltantes se llenan con ""
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