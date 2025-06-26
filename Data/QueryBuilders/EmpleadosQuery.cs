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
    /// Contiene la lógica para construir y ejecutar el query SQL del reporte "MaestroEmpleadosReport".
    /// </summary>
    public static class EmpleadosQuery
    {
        /// <summary>
        /// Ejecuta el query de empleados usando filtros dinámicos.
        /// </summary>
        public static async Task<DataTable> GetDataAsync(Dictionary<string, object> filtros, IConfiguration config)
        {
            string? codEmpresa = null;
            string? empNie = null;
            string? empRfc = null;
            DateTime? fechaIngreso = null;

            // Filtros dinámicos
            if (filtros.TryGetValue("Mb_Epr_cod", out var eprCodObj) && eprCodObj is JsonElement eprCodEl && eprCodEl.ValueKind == JsonValueKind.String)
                codEmpresa = eprCodEl.GetString();

            if (filtros.TryGetValue("Emp_nie", out var nieObj) && nieObj is JsonElement nieEl && nieEl.ValueKind == JsonValueKind.String)
                empNie = nieEl.GetString();

            if (filtros.TryGetValue("EmpRFC_Enc", out var rfcObj) && rfcObj is JsonElement rfcEl && rfcEl.ValueKind == JsonValueKind.String)
                empRfc = rfcEl.GetString();

            if (filtros.TryGetValue("EmpFechaIngreso", out var fechaObj) && fechaObj is JsonElement fechaEl && fechaEl.ValueKind == JsonValueKind.String && DateTime.TryParse(fechaEl.GetString(), out var parsedFecha))
                fechaIngreso = parsedFecha;

            var query = @"
                SELECT 
                    A.Mb_Epr_cod,
                    B.Mb_Epr_ids,
                    A.Emp_nie,
                    A.Emp_nombres_Enc,
                    A.Emp_patern_Enc,
                    A.Emp_matern_Enc,
                    (LTRIM(RTRIM(ISNULL(A.Emp_patern_Enc, ''))) + ' ' + LTRIM(RTRIM(ISNULL(A.Emp_matern_Enc, ''))) + ' ' + LTRIM(RTRIM(ISNULL(A.Emp_nombres_Enc, '')))) AS Nombre,
                    CASE 
                        WHEN DATEADD(YEAR, DATEDIFF(YEAR, A.EmpFechaNacimiento, GETDATE()), A.EmpFechaNacimiento) > GETDATE() 
                        THEN DATEDIFF(YEAR, A.EmpFechaNacimiento, GETDATE()) - 1 
                        ELSE DATEDIFF(YEAR, A.EmpFechaNacimiento, GETDATE()) 
                    END AS Edad,
                    A.EmpFechaIngreso,
                    A.EmpRFC_Enc,
                    A.EmpCURP_Enc,
                    A.EmpNumeroIMSS_Enc
                FROM EMPLEADO A
                LEFT JOIN EMPRESAS B ON A.Mb_Epr_cod = B.Mb_Epr_cod
                WHERE 1 = 1";

            var dt = new DataTable();

            using var connection = new SqlConnection(config.GetConnectionString("DefaultConnection"));
            using var command = new SqlCommand();
            command.Connection = connection;

            // Filtros condicionales
            if (!string.IsNullOrWhiteSpace(codEmpresa))
            {
                query += " AND A.Mb_Epr_cod = @codEmpresa";
                command.Parameters.AddWithValue("@codEmpresa", codEmpresa);
            }

            if (!string.IsNullOrWhiteSpace(empNie))
            {
                query += " AND A.Emp_nie = @empNie";
                command.Parameters.AddWithValue("@empNie", empNie);
            }

            if (!string.IsNullOrWhiteSpace(empRfc))
            {
                query += " AND A.EmpRFC_Enc = @empRfc";
                command.Parameters.AddWithValue("@empRfc", empRfc);
            }

            if (fechaIngreso.HasValue)
            {
                query += " AND A.EmpFechaIngreso = @fechaIngreso";
                command.Parameters.AddWithValue("@fechaIngreso", fechaIngreso.Value);
            }

            command.CommandText = query;

            await connection.OpenAsync();
            using var reader = await command.ExecuteReaderAsync();
            dt.Load(reader);

            return dt;
        }
    }
}
