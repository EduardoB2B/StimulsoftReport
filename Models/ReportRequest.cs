using System.Data;
using System.Collections.Generic;

namespace StimulsoftReport.Models
{
    public class ReportRequest
    {
        public string? ReportName { get; set; }
        public string? JsonFilePath { get; set; }
    }
}