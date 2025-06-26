using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReport.Data
{
    /// <summary>
    /// Contrato para la obtención de datos dinámicos según el nombre del reporte.
    /// Cada reporte debe tener su propia lógica para construir y ejecutar queries.
    /// </summary>
    public interface IReportDataProvider
    {
        /// <summary>
        /// Obtiene los datos para el reporte en base al nombre y los filtros recibidos.
        /// </summary>
        /// <param name="reportName">Nombre del .mrt (sin extensión)</param>
        /// <param name="filtros">Diccionario de filtros enviados en el POST</param>
        /// <returns>Una tabla de datos que el reporte puede utilizar</returns>
        Task<DataTable> GetDataAsync(string reportName, Dictionary<string, object> filtros);
    }
}
