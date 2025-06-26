// using Microsoft.Extensions.Configuration;
// using System.Collections.Generic;
// using System.Data;
// using Microsoft.Data.SqlClient;;
// using System.Text.Json;
// using System.Threading.Tasks;

// namespace StimulsoftReport.QueryBuilders
// {
//     /// <summary>
//     /// Contiene la lógica para construir y ejecutar el query SQL del reporte "[NombreReporte]".
//     /// </summary>
//     public static class [NombreQuery]
//     {
//         /// <summary>
//         /// Ejecuta el query usando los filtros proporcionados dinámicamente.
//         /// </summary>
//         public static async Task<DataTable> GetDataAsync(Dictionary<string, object> filtros, IConfiguration config)
//         {
//             // Declarar variables para los filtros que quieres soportar
//             string? filtro1 = null;
//             string? filtro2 = null;
//             DateTime? filtroFecha = null;

//             // Convertir los filtros usando JsonElement para evitar errores de tipo
//             if (filtros.TryGetValue("Filtro1", out var f1Obj) && f1Obj is JsonElement f1El && f1El.ValueKind == JsonValueKind.String)
//                 filtro1 = f1El.GetString();

//             if (filtros.TryGetValue("Filtro2", out var f2Obj) && f2Obj is JsonElement f2El && f2El.ValueKind == JsonValueKind.String)
//                 filtro2 = f2El.GetString();

//             if (filtros.TryGetValue("FiltroFecha", out var ffObj) && ffObj is JsonElement ffEl && ffEl.ValueKind == JsonValueKind.String && DateTime.TryParse(ffEl.GetString(), out var parsedFecha))
//                 filtroFecha = parsedFecha;

//             // Base query SQL
//             var query = @"
//                 SELECT columna1, columna2
//                 FROM TablaX
//                 WHERE 1 = 1"; // Esto permite concatenar más filtros dinámicamente

//             var dt = new DataTable();

//             using var connection = new SqlConnection(config.GetConnectionString("DefaultConnection"));
//             using var command = new SqlCommand();
//             command.Connection = connection;

//             // Agregar condiciones dinámicamente si los filtros están presentes
//             if (!string.IsNullOrWhiteSpace(filtro1))
//             {
//                 query += " AND columna1 = @filtro1";
//                 command.Parameters.AddWithValue("@filtro1", filtro1);
//             }

//             if (!string.IsNullOrWhiteSpace(filtro2))
//             {
//                 query += " AND columna2 = @filtro2";
//                 command.Parameters.AddWithValue("@filtro2", filtro2);
//             }

//             if (filtroFecha.HasValue)
//             {
//                 query += " AND FechaColumna = @filtroFecha";
//                 command.Parameters.AddWithValue("@filtroFecha", filtroFecha.Value);
//             }

//             command.CommandText = query;

//             await connection.OpenAsync();
//             using var reader = await command.ExecuteReaderAsync();
//             dt.Load(reader);

//             return dt;
//         }
//     }
// }


// Instrucciones para usar la plantilla
// Reemplaza NombreQuery por el nombre que identificará tu reporte. Ej: ClientesQuery, PagosQuery.

// Cambia los nombres de filtros (Filtro1, Filtro2, etc.) por los que tu reporte requiere.

// Modifica el SQL base y los columnaX por los reales de tu tabla.

// Agrega más bloques de filtro si los necesitas.

// Registra el nuevo case en ReportDataProvider.cs