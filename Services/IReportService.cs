using StimulsoftReport.Models;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReport.Services
{
    public interface IReportService
    {
        Task<byte[]> GenerateReportAsync(ReportRequest request);
    }
}
