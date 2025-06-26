using System.Data;
using System.Collections.Generic;

namespace StimulsoftReport.Models
{
    public class ReportRequest
    {
        public string ReportName { get; set; } = string.Empty;
        public Dictionary<string, object> Filtros { get; set; } = new();

    }
}
