using System;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace PROYECTO_RESTAURANTE
{
    // ==========================================================
    // HISTORIAL GENERAL DE PEDIDOS Y VENTAS (solo lectura)
    // ----------------------------------------------------------
    // Una fila por pedido, de TODOS los estados. Para no duplicar:
    //  * productos y pagos se agregan por pedido ANTES de unirse
    //    (un pedido con varios productos o un pago mixto sigue
    //    siendo una sola fila);
    //  * Total = V_PEDIDO_TOTAL.total (líneas con descuento,
    //    descuento del pedido y propina), el mismo de los reportes;
    //  * método y fecha de cobro solo si el pedido está cobrado
    //    y tiene pagos (sin datos ficticios);
    //  * "Monto cobrado" suma solo pedidos cobrados: pendientes,
    //    entregados sin cobrar y cancelados no son ingresos.
    // Solo SELECT parametrizados: no escribe en la base.
    // ==========================================================
    internal sealed class FiltroHistorial
    {
        public bool TodoElHistorial = true;      // sin límite de fechas (predeterminado)
        public DateTime Desde = DateTime.Today;  // fecha de registro, inclusive
        public DateTime Hasta = DateTime.Today;  // inclusive
        public string Estado;                    // null = todos
        public string Canal;                     // null = todos
        public int? IdPedido;
        public string Cliente;                   // nombre, apellidos o documento (parcial)
        public bool Ascendente;                  // true = del primero al último

        public string Descripcion()
        {
            StringBuilder t = new StringBuilder(TodoElHistorial ? "Todo el historial"
                : Desde.ToString("dd/MM/yyyy") + " al " + Hasta.ToString("dd/MM/yyyy"));
            if (Estado != null) t.Append(" · estado ").Append(HistorialDatos.NombreEstado(Estado));
            if (Canal != null) t.Append(" · ").Append(ReporteDatos.NombreCanal(Canal));
            if (IdPedido.HasValue) t.Append(" · pedido N.º ").Append(IdPedido.Value);
            if (!string.IsNullOrWhiteSpace(Cliente)) t.Append(" · cliente «").Append(Cliente.Trim()).Append("»");
            t.Append(Ascendente ? " · del primero al último" : " · del más reciente al más antiguo");
            return t.ToString();
        }
    }

    internal sealed class IndicadoresHistorial
    {
        public int Registrados, Cobrados, EnProceso, Cancelados;
        public decimal MontoCobrado;
    }

    internal sealed class DetallePedido
    {
        public DataRow Cabecera;      // una fila del historial
        public DataTable Productos;
        public DataTable Pagos;
        public string Promocion;
        public decimal DescuentoPedido, Propina;
    }

    internal static class HistorialDatos
    {
        public static readonly string[] Estados = { "pendiente", "en_preparacion", "listo", "entregado", "cobrado", "cancelado" };

        public static string NombreEstado(string e)
        {
            switch ((e ?? "").ToLowerInvariant())
            {
                case "pendiente": return "Pendiente";
                case "en_preparacion": return "En preparación";
                case "listo": return "Listo";
                case "entregado": return "Entregado";
                case "cobrado": return "Cobrado";
                case "cancelado": return "Cancelado";
                default: return e ?? "-";
            }
        }

        private const string Base = @"
            WITH Lineas AS (
                SELECT id_pedido, SUM(cantidad * precio_unitario) AS bruto, SUM(cantidad * descuento) AS desc_lineas
                FROM (SELECT id_pedido, cantidad, precio_unitario, descuento FROM dbo.DETALLE_PEDIDO_PLATO
                      UNION ALL
                      SELECT id_pedido, cantidad, precio_unitario, descuento FROM dbo.DETALLE_PEDIDO_COMBO) L
                GROUP BY id_pedido
            ),
            Pagos AS (
                SELECT id_pedido, MAX(fecha_hora) AS fecha_cobro,
                       MAX(id_pago) AS ultimo_pago
                FROM dbo.PAGO GROUP BY id_pedido
            ),
            Metodos AS (
                SELECT id_pedido, STRING_AGG(metodo, ' + ') WITHIN GROUP (ORDER BY primero) AS metodos
                FROM (SELECT id_pedido, LOWER(LTRIM(RTRIM([método]))) AS metodo, MIN(id_pago) AS primero
                      FROM dbo.PAGO GROUP BY id_pedido, LOWER(LTRIM(RTRIM([método])))) X
                GROUP BY id_pedido
            ),
            Historial AS (
                SELECT P.id_pedido, P.fecha_hora, P.canal, P.estado, M.[número] AS mesa,
                       CASE WHEN C.id_cliente IS NULL THEN NULL ELSE LTRIM(RTRIM(C.nombre + ' ' + C.apellidos)) END AS cliente,
                       C.documento,
                       COALESCE(LTRIM(RTRIM(EM.nombre + ' ' + EM.apellidos)), LTRIM(RTRIM(EC.nombre + ' ' + EC.apellidos))) AS empleado,
                       ISNULL(L.bruto, 0) AS subtotal,
                       ISNULL(L.desc_lineas, 0) + ISNULL(P.descuento_aplicado, 0) AS descuentos,
                       ISNULL(V.total, 0) AS total,
                       CASE WHEN P.estado = 'cobrado' THEN MT.metodos END AS metodos,
                       CASE WHEN P.estado = 'cobrado' THEN PG.fecha_cobro END AS fecha_cobro
                FROM dbo.PEDIDO P
                LEFT JOIN Lineas L ON L.id_pedido = P.id_pedido
                LEFT JOIN dbo.V_PEDIDO_TOTAL V ON V.id_pedido = P.id_pedido
                LEFT JOIN Pagos PG ON PG.id_pedido = P.id_pedido
                LEFT JOIN Metodos MT ON MT.id_pedido = P.id_pedido
                LEFT JOIN dbo.PAGO UP ON UP.id_pago = PG.ultimo_pago
                LEFT JOIN dbo.MESA M ON M.id_mesa = P.id_mesa
                LEFT JOIN dbo.CLIENTE C ON C.id_cliente = P.id_cliente
                LEFT JOIN dbo.EMPLEADO EM ON EM.id_empleado = P.id_mesero
                LEFT JOIN dbo.EMPLEADO EC ON EC.id_empleado = UP.id_cajero
                WHERE P.id_sucursal = @Sucursal
                  AND (@Todo = 1 OR (P.fecha_hora >= @Desde AND P.fecha_hora < @Hasta))
                  AND (@Estado IS NULL OR P.estado = @Estado)
                  AND (@Canal IS NULL OR P.canal = @Canal)
                  AND (@Pedido IS NULL OR P.id_pedido = @Pedido)
                  AND (@Cliente IS NULL OR (C.nombre + ' ' + C.apellidos) LIKE @Cliente OR C.documento LIKE @Cliente)
            )";

        private static SqlCommand Comando(string sql, SqlConnection cx, FiltroHistorial f)
        {
            SqlCommand cmd = new SqlCommand(sql, cx) { CommandTimeout = 60 };
            cmd.Parameters.Add("@Sucursal", SqlDbType.Int).Value = BaseDatos.IdSucursalActual;
            cmd.Parameters.Add("@Todo", SqlDbType.Bit).Value = f.TodoElHistorial;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = f.Desde.Date;
            cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = f.Hasta.Date.AddDays(1);
            cmd.Parameters.Add("@Estado", SqlDbType.VarChar, 20).Value = (object)f.Estado ?? DBNull.Value;
            cmd.Parameters.Add("@Canal", SqlDbType.VarChar, 20).Value = (object)f.Canal ?? DBNull.Value;
            cmd.Parameters.Add("@Pedido", SqlDbType.Int).Value = f.IdPedido.HasValue ? (object)f.IdPedido.Value : DBNull.Value;
            string cli = (f.Cliente ?? "").Trim();
            cmd.Parameters.Add("@Cliente", SqlDbType.VarChar, 120).Value =
                cli.Length == 0 ? (object)DBNull.Value : "%" + cli.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            return cmd;
        }

        // Página de la tabla (tamaño 0 = todas las filas, para exportar).
        public static DataTable Consultar(FiltroHistorial f, int pagina, int tamano)
        {
            string orden = f.Ascendente ? "fecha_hora ASC, id_pedido ASC" : "fecha_hora DESC, id_pedido DESC";
            string sql = Base + @"
                SELECT id_pedido AS [N.º pedido],
                       CONVERT(varchar(16), fecha_hora, 120) AS [Fecha y hora],
                       CASE canal WHEN 'salon' THEN 'Salón' WHEN 'delivery' THEN 'Delivery' WHEN 'recojo' THEN 'Recojo' ELSE canal END AS [Tipo],
                       CASE WHEN mesa IS NULL THEN '-' ELSE CAST(mesa AS varchar(10)) END AS [Mesa],
                       ISNULL(cliente, '-') AS [Cliente],
                       ISNULL(empleado, '-') AS [Atendió],
                       CASE estado WHEN 'pendiente' THEN 'Pendiente' WHEN 'en_preparacion' THEN 'En preparación'
                                   WHEN 'listo' THEN 'Listo' WHEN 'entregado' THEN 'Entregado'
                                   WHEN 'cobrado' THEN 'Cobrado' WHEN 'cancelado' THEN 'Cancelado' ELSE estado END AS [Estado],
                       subtotal AS [Subtotal (S/)], descuentos AS [Descuentos (S/)], total AS [Total (S/)],
                       ISNULL(metodos, '-') AS [Método de pago],
                       ISNULL(CONVERT(varchar(16), fecha_cobro, 120), '-') AS [Fecha de cobro]
                FROM Historial
                ORDER BY " + orden + (tamano > 0 ? @"
                OFFSET @Saltar ROWS FETCH NEXT @Tomar ROWS ONLY;" : ";");
            DataTable t = new DataTable();
            using (SqlConnection cx = new SqlConnection(BaseDatos.CadenaConexion))
            using (SqlCommand cmd = Comando(sql, cx, f))
            {
                if (tamano > 0)
                {
                    cmd.Parameters.Add("@Saltar", SqlDbType.Int).Value = Math.Max(0, pagina) * tamano;
                    cmd.Parameters.Add("@Tomar", SqlDbType.Int).Value = tamano;
                }
                using (SqlDataAdapter a = new SqlDataAdapter(cmd))
                    a.Fill(t);
            }
            // Métodos con su nombre legible (Efectivo + Yape...).
            foreach (DataRow r in t.Rows)
            {
                string m = Convert.ToString(r["Método de pago"]);
                if (m != "-")
                {
                    string[] partes = m.Split(new[] { " + " }, StringSplitOptions.None);
                    for (int i = 0; i < partes.Length; i++) partes[i] = ReporteDatos.NombreMetodo(partes[i]);
                    r["Método de pago"] = string.Join(" + ", partes);
                }
            }
            return t;
        }

        public static IndicadoresHistorial Indicadores(FiltroHistorial f)
        {
            string sql = Base + @"
                SELECT COUNT(*),
                       SUM(CASE WHEN estado = 'cobrado' THEN 1 ELSE 0 END),
                       SUM(CASE WHEN estado IN ('pendiente', 'en_preparacion', 'listo', 'entregado') THEN 1 ELSE 0 END),
                       SUM(CASE WHEN estado = 'cancelado' THEN 1 ELSE 0 END),
                       SUM(CASE WHEN estado = 'cobrado' THEN total ELSE 0 END)
                FROM Historial;";
            IndicadoresHistorial r = new IndicadoresHistorial();
            using (SqlConnection cx = new SqlConnection(BaseDatos.CadenaConexion))
            using (SqlCommand cmd = Comando(sql, cx, f))
            {
                cx.Open();
                using (SqlDataReader d = cmd.ExecuteReader())
                {
                    if (d.Read())
                    {
                        r.Registrados = d.IsDBNull(0) ? 0 : d.GetInt32(0);
                        r.Cobrados = d.IsDBNull(1) ? 0 : d.GetInt32(1);
                        r.EnProceso = d.IsDBNull(2) ? 0 : d.GetInt32(2);
                        r.Cancelados = d.IsDBNull(3) ? 0 : d.GetInt32(3);
                        r.MontoCobrado = d.IsDBNull(4) ? 0 : d.GetDecimal(4);
                    }
                }
            }
            return r;
        }

        // Detalle de un pedido: productos, promoción, pagos (solo lectura).
        public static DetallePedido Detalle(int idPedido)
        {
            DetallePedido d = new DetallePedido();
            DataTable cab = Consultar(new FiltroHistorial { IdPedido = idPedido }, 0, 0);
            if (cab.Rows.Count == 0) return null;
            d.Cabecera = cab.Rows[0];
            using (SqlConnection cx = new SqlConnection(BaseDatos.CadenaConexion))
            {
                cx.Open();
                d.Productos = Tabla(cx, @"
                    SELECT PL.nombre AS [Producto], D.cantidad AS [Cant.], D.precio_unitario AS [P. unit. (S/)],
                           D.descuento AS [Desc. unit. (S/)], D.cantidad * (D.precio_unitario - D.descuento) AS [Importe (S/)]
                    FROM dbo.DETALLE_PEDIDO_PLATO D JOIN dbo.PLATO PL ON PL.id_plato = D.id_plato
                    WHERE D.id_pedido = @Pedido
                    UNION ALL
                    SELECT 'Combo: ' + CB.nombre, D.cantidad, D.precio_unitario, D.descuento, D.cantidad * (D.precio_unitario - D.descuento)
                    FROM dbo.DETALLE_PEDIDO_COMBO D JOIN dbo.COMBO CB ON CB.id_combo = D.id_combo
                    WHERE D.id_pedido = @Pedido;", idPedido);
                d.Pagos = Tabla(cx, @"
                    SELECT PG.[método] AS [Método], PG.importe AS [Importe (S/)], CONVERT(varchar(16), PG.fecha_hora, 120) AS [Fecha],
                           LTRIM(RTRIM(E.nombre + ' ' + E.apellidos)) AS [Cobró]
                    FROM dbo.PAGO PG LEFT JOIN dbo.EMPLEADO E ON E.id_empleado = PG.id_cajero
                    WHERE PG.id_pedido = @Pedido ORDER BY PG.id_pago;", idPedido);
                foreach (DataRow r in d.Pagos.Rows) r["Método"] = ReporteDatos.NombreMetodo(Convert.ToString(r["Método"]));
                DataTable p = Tabla(cx, @"
                    SELECT ISNULL(PR.nombre, ''), ISNULL(P.descuento_aplicado, 0), ISNULL(P.propina, 0)
                    FROM dbo.PEDIDO P LEFT JOIN dbo.PROMOCION PR ON PR.id_promocion = P.id_promocion
                    WHERE P.id_pedido = @Pedido;", idPedido);
                if (p.Rows.Count > 0)
                {
                    d.Promocion = Convert.ToString(p.Rows[0][0]);
                    d.DescuentoPedido = Convert.ToDecimal(p.Rows[0][1]);
                    d.Propina = Convert.ToDecimal(p.Rows[0][2]);
                }
            }
            return d;
        }

        private static DataTable Tabla(SqlConnection cx, string sql, int idPedido)
        {
            DataTable t = new DataTable();
            using (SqlCommand cmd = new SqlCommand(sql, cx))
            {
                cmd.Parameters.Add("@Pedido", SqlDbType.Int).Value = idPedido;
                using (SqlDataAdapter a = new SqlDataAdapter(cmd)) a.Fill(t);
            }
            return t;
        }
    }
}
