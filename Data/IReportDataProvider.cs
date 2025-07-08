using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReport.Data
{
    public interface IReportDataProvider
    {
        /// <summary>
        /// Obtiene los datos para el reporte desde SQL usando filtros.
        /// </summary>
        Task<Dictionary<string, DataTable>> GetDataFromFiltersAsync(string reportName, Dictionary<string, object> filtros);

        /// <summary>
        /// Convierte un objeto de datos preparado a DataTables.
        /// </summary>
        Task<Dictionary<string, DataTable>> GetDataFromObjectAsync(string reportName, Dictionary<string, object> data);
    }
}