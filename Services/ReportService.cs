using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Stimulsoft.Report;
using Stimulsoft.Report.Export;
using Stimulsoft.Report.Web;
using StimulsoftReportDemo.Models;
using StimulsoftReportDemo.Data;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System;

namespace StimulsoftReportDemo.Services
{
    public class ReportService : IReportService
    {
        private readonly IReportDataProvider _dataProvider;
        private readonly IWebHostEnvironment _env;

        public ReportService(IReportDataProvider dataProvider, IWebHostEnvironment env)
        {
            _dataProvider = dataProvider;
            _env = env;
        }

        /// <summary>
        /// Genera un archivo PDF a partir de un .mrt, aplicando los filtros y recuperando datos dinámicamente.
        /// </summary>
        public async Task<byte[]> GenerateReportAsync(ReportRequest request)
        {
            // 1. Localizar la plantilla .mrt
            var reportPath = Path.Combine(_env.ContentRootPath, "Reports", $"{request.ReportName}.mrt");

            if (!File.Exists(reportPath))
                throw new FileNotFoundException($"El archivo de plantilla '{reportPath}' no existe.");

            var report = new StiReport();
            report.Load(reportPath);

            // 2. Obtener datos desde el proveedor según el nombre del reporte
            var data = await _dataProvider.GetDataAsync(request.ReportName, request.Filtros);

            if (data == null || data.Rows.Count == 0)
                throw new Exception("No se recuperaron datos para el reporte.");

            foreach (DataRow row in data.Rows)
            {
                foreach (DataColumn col in data.Columns)
                {
                    Console.Write($"{col.ColumnName}: {row[col]} | ");
                }
                Console.WriteLine();
            }

            // 3. Registrar directamente el DataTable con el nombre esperado por el .mrt
            report.RegData("DATA", data);
            report.Dictionary.Databases.Clear(); // Limpia posibles conexiones embebidas
            report.Dictionary.Synchronize();


            // 4. Renderizar y exportar a PDF
            // report.Compile();
            report.Render(false);

            using var stream = new MemoryStream();
            var pdfSettings = new StiPdfExportSettings();
            var pdfService = new StiPdfExportService();
            pdfService.ExportPdf(report, stream, pdfSettings);

            return stream.ToArray();
        }
    }
}
