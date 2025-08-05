using System.Data;
// Models/ReportRequest.cs
using System.Collections.Generic;

namespace StimulsoftReport.Models
{
    public class ReportRequest
    {
        public string ReportName { get; set; } = "";
        public string? JsonFilePath { get; set; }
        public Dictionary<string, object>? SqlParams { get; set; }
    }
}