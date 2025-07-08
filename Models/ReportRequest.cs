using System.Data;
using System.Collections.Generic;

namespace StimulsoftReport.Models
{
    /// <summary>
    /// Request para reportes que extraen datos de SQL usando filtros.
    /// </summary>
    public class ReportFilterRequest
    {
        public string ReportName { get; set; } = string.Empty;
        public Dictionary<string, object> Filtros { get; set; } = new Dictionary<string, object>();
    }
}