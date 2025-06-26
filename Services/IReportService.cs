using StimulsoftReportDemo.Models;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace StimulsoftReportDemo.Services
{
    public interface IReportService
    {
        Task<byte[]> GenerateReportAsync(ReportRequest request);
    }
}
