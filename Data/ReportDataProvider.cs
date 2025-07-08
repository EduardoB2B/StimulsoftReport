using Microsoft.Extensions.Configuration;
using StimulsoftReport.QueryBuilders;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace StimulsoftReport.Data
{
    public class ReportDataProvider : IReportDataProvider
    {
        private readonly IConfiguration _configuration;

        public ReportDataProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Obtiene los datos para el reporte desde SQL usando filtros.
        /// </summary>
        public async Task<Dictionary<string, DataTable>> GetDataFromFiltersAsync(string reportName, Dictionary<string, object> filtros)
        {
            if (string.IsNullOrWhiteSpace(reportName))
                throw new ArgumentException("El nombre del reporte no puede ser nulo o vacío.", nameof(reportName));

            var result = new Dictionary<string, DataTable>();

            switch (reportName.Trim().ToLower())
            {
                case "empresasreport":
                    result["Empresas"] = await EmpresasQuery.GetDataAsync(filtros, _configuration);
                    break;

                case "maestroempleadosreport":
                    result["Empleados"] = await EmpleadosQuery.GetDataAsync(filtros, _configuration);
                    break;

                // Agrega aquí más reportes SQL según tu lógica

                default:
                    throw new NotSupportedException($"El reporte '{reportName}' no está soportado para filtros SQL.");
            }

            if (result.Count == 0)
                throw new Exception($"No se pudieron obtener datos para el reporte '{reportName}'.");

            return result;
        }

        /// <summary>
        /// Convierte un objeto de datos preparado a DataTables.
        /// </summary>
        public async Task<Dictionary<string, DataTable>> GetDataFromObjectAsync(string reportName, Dictionary<string, object> data)
        {
            if (string.IsNullOrWhiteSpace(reportName))
                throw new ArgumentException("El nombre del reporte no puede ser nulo o vacío.", nameof(reportName));

            var result = new Dictionary<string, DataTable>();

            switch (reportName.Trim().ToLower())
            {
                case "cfdi":
                    // Convertir cada sección del objeto data a DataTable
                    foreach (var kvp in data)
                    {
                        // Soporta tanto JArray como string JSON o listas
                        if (kvp.Value is JArray jArray)
                        {
                            if (jArray.Count == 0)
                            {
                                var emptyTable = new DataTable(kvp.Key);
                                result[kvp.Key] = emptyTable;
                                continue;
                            }

                            try
                            {
                                var dt = JsonConvert.DeserializeObject<DataTable>(jArray.ToString());
                                if (dt != null)
                                {
                                    dt.TableName = kvp.Key;
                                    result[kvp.Key] = dt;
                                }
                                else
                                {
                                    var emptyTable = new DataTable(kvp.Key);
                                    result[kvp.Key] = emptyTable;
                                }
                            }
                            catch (JsonException ex)
                            {
                                throw new ArgumentException($"Error al convertir la sección '{kvp.Key}' a DataTable: {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            var jsonString = kvp.Value?.ToString();
                            if (string.IsNullOrWhiteSpace(jsonString))
                            {
                                var emptyTable = new DataTable(kvp.Key);
                                result[kvp.Key] = emptyTable;
                                continue;
                            }

                            try
                            {
                                var jToken = JToken.Parse(jsonString);
                                if (jToken is JArray array)
                                {
                                    var dt = JsonConvert.DeserializeObject<DataTable>(array.ToString());
                                    if (dt != null)
                                    {
                                        dt.TableName = kvp.Key;
                                        result[kvp.Key] = dt;
                                    }
                                    else
                                    {
                                        var emptyTable = new DataTable(kvp.Key);
                                        result[kvp.Key] = emptyTable;
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                throw new ArgumentException($"Error al convertir la sección '{kvp.Key}' a DataTable: {ex.Message}", ex);
                            }
                        }
                    }
                    break;

                // Agrega aquí más reportes que reciban data preparada si lo necesitas

                default:
                    throw new NotSupportedException($"El reporte '{reportName}' no está soportado para datos preparados.");
            }

            if (result.Count == 0)
                throw new Exception($"No se pudieron convertir los datos para el reporte '{reportName}'.");

            return await Task.FromResult(result);
        }
    }
}