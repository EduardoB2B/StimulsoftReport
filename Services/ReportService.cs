using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Stimulsoft.Report;
using Stimulsoft.Report.Export;
using Stimulsoft.Report.Web;
using StimulsoftReport.Models;
using StimulsoftReport.Data;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace StimulsoftReport.Services
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
        /// Genera un reporte usando filtros para extraer datos de SQL.
        /// </summary>
        public async Task<byte[]> GenerateReportFromFiltersAsync(ReportFilterRequest request)
        {
            var reportPath = Path.Combine(_env.ContentRootPath, "Reports", $"{request.ReportName}.mrt");

            if (!File.Exists(reportPath))
                throw new FileNotFoundException($"El archivo de plantilla '{reportPath}' no existe.");

            var report = new StiReport();
            report.Load(reportPath);

            // Obtener datos desde SQL usando filtros
            var dataTables = await _dataProvider.GetDataFromFiltersAsync(request.ReportName, request.Filtros);
            Console.WriteLine("Datos recuperados desde SQL:");
            foreach (var kvp in dataTables)
            {
                Console.WriteLine($"Tabla: {kvp.Key}, Filas: {kvp.Value.Rows.Count}");
            }

            if (dataTables == null || dataTables.Count == 0)
                throw new Exception("No se recuperaron datos para el reporte.");

            // Registrar DataTables
            foreach (var kvp in dataTables)
            {
                report.RegData(kvp.Key, kvp.Value);

                // Compatibilidad: si solo hay una tabla y el nombre no es "DATA", regístrala también como "DATA"
                if (dataTables.Count == 1 && kvp.Key != "DATA")
                {
                    report.RegData("DATA", kvp.Value);
                }
            }

            report.Dictionary.Databases.Clear();
            report.Dictionary.Synchronize();

            return await RenderReportToPdf(report);
        }

        /// <summary>
        /// Genera un reporte usando data ya preparada por el cliente.
        /// </summary>
        public async Task<byte[]> GenerateReportFromDataAsync(ReportDataRequest request)
        {
            var reportPath = Path.Combine(_env.ContentRootPath, "Reports", $"{request.ReportName}.mrt");

            if (!File.Exists(reportPath))
                throw new FileNotFoundException($"El archivo de plantilla '{reportPath}' no existe.");

            var report = new StiReport();
            report.Load(reportPath);

            // Convertir data preparada a DataTables
            var dataTables = await _dataProvider.GetDataFromObjectAsync(request.ReportName, request.Data);
            Console.WriteLine("Datos recuperados desde objeto preparado:");
            foreach (var kvp in dataTables)
            {
                Console.WriteLine($"Tabla: {kvp.Key}, Filas: {kvp.Value.Rows.Count}");
            }

            if (dataTables == null || dataTables.Count == 0)
                throw new Exception("No se pudieron convertir los datos para el reporte.");

            // Registrar DataTables
            foreach (var kvp in dataTables)
            {
                report.RegData(kvp.Key, kvp.Value);
            }

            report.Dictionary.Databases.Clear();
            report.Dictionary.Synchronize();

            return await RenderReportToPdf(report);
        }

        /// <summary>
        /// Método común para renderizar el reporte a PDF.
        /// </summary>
        private async Task<byte[]> RenderReportToPdf(StiReport report)
        {
            await Task.Run(() => report.Render(false));

            using var stream = new MemoryStream();
            var pdfSettings = new StiPdfExportSettings();
            var pdfService = new StiPdfExportService();
            pdfService.ExportPdf(report, stream, pdfSettings);

            return stream.ToArray();
        }
    }
}