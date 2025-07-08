using StimulsoftReport.Models;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReport.Services
{
    public interface IReportService
    {
        /// <summary>
        /// Genera un reporte usando filtros para extraer datos de SQL.
        /// </summary>
        Task<byte[]> GenerateReportFromFiltersAsync(ReportFilterRequest request);

        /// <summary>
        /// Genera un reporte usando data ya preparada por el cliente.
        /// </summary>
        Task<byte[]> GenerateReportFromDataAsync(ReportDataRequest request);
    }
}
