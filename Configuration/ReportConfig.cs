using System.Collections.Generic;

namespace StimulsoftReport.Configuration
{
    public class ReportConfig
    {
        public string TemplateFile { get; set; } = "";
        public Dictionary<string, string> DataSourceMappings { get; set; } = new();
        public string[] RequiredDataSources { get; set; } = new string[0];
    }
}