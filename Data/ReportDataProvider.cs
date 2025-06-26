using Microsoft.Extensions.Configuration;
using StimulsoftReportDemo.QueryBuilders;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReportDemo.Data
{
    /// <summary>
    /// Implementación que resuelve qué query ejecutar según el nombre del reporte.
    /// Esta clase actúa como un enrutador hacia la lógica SQL correspondiente.
    /// </summary>
    public class ReportDataProvider : IReportDataProvider
    {
        private readonly IConfiguration _configuration;

        public ReportDataProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Obtiene los datos necesarios para el reporte indicado, utilizando los filtros proporcionados.
        /// </summary>
        public async Task<DataTable> GetDataAsync(string reportName, Dictionary<string, object> filtros)
        {
            if (string.IsNullOrWhiteSpace(reportName))
                throw new ArgumentException("El nombre del reporte no puede ser nulo o vacío.", nameof(reportName));

            // Normalizamos el nombre a minúsculas para evitar errores por mayúsculas/minúsculas
            switch (reportName.Trim().ToLower())
            {
                case "empresasreport":
                    return await EmpresasQuery.GetDataAsync(filtros, _configuration);

                // Aquí puedes agregar más reportes:
                // case "clientesreport":
                //     return await ClientesQuery.GetDataAsync(filtros, _configuration);

                case "maestroempleadosreport":
                return await EmpleadosQuery.GetDataAsync(filtros, _configuration);
                
                default:
                    throw new NotSupportedException($"El reporte '{reportName}' no está soportado.");
            }
        }
    }
}
