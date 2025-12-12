using System.Collections.Generic;

namespace StimulsoftReport.Configuration
{
    /// <summary>
    /// Configuración de un reporte, cargada desde un archivo JSON.
    /// </summary>
    public class ReportConfig
    {
        /// <summary>
        /// Nombre del archivo de plantilla .mrt del reporte.
        /// </summary>
        public string TemplateFile { get; set; } = "";

        /// <summary>
        /// Mapeo de nombres de DataSource a rutas en el JSON.
        /// Si la ruta está vacía, se considera el DataSource principal.
        /// </summary>
        public Dictionary<string, string>? DataSourceMappings { get; set; }

        /// <summary>
        /// Lista de DataSources requeridos por el reporte.
        /// </summary>
        public List<string>? RequiredDataSources { get; set; }

        /// <summary>
        /// Reglas dinámicas para balancear el número de filas entre grupos de tablas.
        /// Opcional. Si no se especifica, no se aplican reglas dinámicas.
        /// </summary>
        public List<RowBalanceRuleConfig>? RowBalanceRules { get; set; }
    }

    /// <summary>
    /// Configuración de una regla de balance de filas.
    /// Define un grupo de tablas que deben tener el mismo número de filas por registro principal.
    /// </summary>
    public class RowBalanceRuleConfig
    {
        /// <summary>
        /// Lista de nombres de tablas que deben balancearse entre sí.
        /// Todas las tablas del grupo tendrán el mismo número de filas por cada registro principal.
        /// </summary>
        public List<string> Tables { get; set; } = new List<string>();

        /// <summary>
        /// Opcional. Define el número mínimo de filas que debe tener cada tabla especificada,
        /// independientemente del balance con otras tablas del grupo.
        /// Clave: nombre de la tabla. Valor: número mínimo de filas.
        /// </summary>
        public Dictionary<string, int>? MinRowsPerTable { get; set; }
    }
}