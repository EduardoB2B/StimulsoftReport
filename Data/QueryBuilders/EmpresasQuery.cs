using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;
using System;

namespace StimulsoftReportDemo.QueryBuilders
{
    /// <summary>
    /// Contiene la lógica para construir y ejecutar el query SQL del reporte "EmpresasReport".
    /// </summary>
    public static class EmpresasQuery
    {
        /// <summary>
        /// Ejecuta el query del reporte de empresas usando los filtros proporcionados.
        /// </summary>
        public static async Task<DataTable> GetDataAsync(Dictionary<string, object> filtros, IConfiguration config)
        {
            string? idEmpresa = null;

            if (filtros.ContainsKey("idEmpresa") && filtros["idEmpresa"] is JsonElement idEmpresaElement && idEmpresaElement.ValueKind != JsonValueKind.Null)
            {
                idEmpresa = idEmpresaElement.GetString();
            }

            var query = @"
                SELECT Mb_Epr_ids, Mb_Epr_cod, Mb_webip, CVProVavDsc, GruEmpIpBatch, GruEmpPathWeb
                FROM EMPRESAS
                WHERE Mb_webip IS NOT NULL
                    AND GruEmpIpBatch IS NOT NULL
                    AND GruEmpPathWeb IS NOT NULL";

            var dt = new DataTable();

            using var connection = new SqlConnection(config.GetConnectionString("DefaultConnection"));
            using var command = new SqlCommand();
            command.Connection = connection;

            // Aplicar filtro dinámico si se especifica y no es comodín
            if (!string.IsNullOrWhiteSpace(idEmpresa) && idEmpresa != "*")
            {
                query += " AND Mb_Epr_cod = @idEmpresa";
                command.Parameters.AddWithValue("@idEmpresa", idEmpresa);
            }

            command.CommandText = query;

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();
            dt.Load(reader);

            return dt;
        }
    }
}
