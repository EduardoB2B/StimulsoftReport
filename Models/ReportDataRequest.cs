using System.Data;
using System.Collections.Generic;

namespace StimulsoftReport.Models
{
    /// <summary>
    /// Request para reportes donde el cliente ya tiene toda la data
    /// y solo necesita que el backend genere el archivo.
    /// </summary>
    public class ReportDataRequest
    {
        public string ReportName { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    }
}