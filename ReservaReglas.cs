using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace PROYECTO_RESTAURANTE
{
    // Reglas únicas de reservas. Las usan Nueva reserva (botón superior y
    // botón «Reservar» del panel de la mesa), el monitor automático y la
    // confirmación de llegada, para que no existan dos criterios distintos.
    internal static class ReservaReglas
    {
        // Horario de atención del restaurante: 11:00 a 23:00.
        public static readonly TimeSpan HoraApertura = new TimeSpan(11, 0, 0);
        public static readonly TimeSpan HoraCierre = new TimeSpan(23, 0, 0);

        // Las reservas se ofrecen cada 30 minutos.
        public const int IntervaloMinutos = 30;

        // Atención mínima antes del cierre: la última reserva es a las 22:00,
        // para que el cliente tenga al menos una hora de servicio.
        public const int AtencionMinimaMinutos = 60;

        // Tiempo que una reserva ocupa la mesa. Dos reservas de la misma
        // mesa deben separarse al menos este tiempo (19:00 y 21:00 sí;
        // 19:00 y 20:30 no).
        public const int DuracionReservaMinutos = 120;

        // Tolerancia de llegada: pasado este tiempo la reserva vence.
        public const int ToleranciaLlegadaMinutos = 30;

        // ------------------------------------------------------------------
        // APARTADO DE LA MESA (regla única: al guardar y en el monitor)
        //   * Reserva de la apertura (11:00): la mesa queda «reservada» desde
        //     el inicio de ESE día (antes de abrir), nunca desde el día anterior.
        //   * Cualquier otra hora: exactamente 15 minutos antes.
        //   * Hasta la tolerancia (30 min después), cuando la reserva vence.
        //   Nunca se aparta una mesa con un pedido activo.
        // ------------------------------------------------------------------
        public const int ApartadoPrevioMinutos = 15;

        // Llegada anticipada: el personal autorizado confirma la llegada en
        // cualquier momento del MISMO día de la reserva (ControlReservas-
        // Automaticas.ConfirmarLlegada) y Pedidos vincula el pedido a esa
        // reserva de hoy. Este valor ya no limita la llegada; se conserva
        // porque Pedidos aún envía el parámetro @Apartado (sin uso en SQL).
        public const int LlegadaAnticipadaMinutos = 60;

        // Nombre anterior del mismo valor (lo usa Pedidos). NO es el apartado
        // de la mesa (ver ApartadoPrevioMinutos).
        public const int ApartadoMinutos = LlegadaAnticipadaMinutos;

        public static DateTime InicioApartado(DateTime fechaHora)
        {
            return fechaHora.TimeOfDay <= HoraApertura
                ? fechaHora.Date
                : fechaHora.AddMinutes(-ApartadoPrevioMinutos);
        }

        // ¿La mesa debe figurar «reservada» ahora por esta reserva confirmada?
        public static bool DebeEstarApartada(DateTime fechaHora, DateTime ahora)
        {
            return ahora >= InicioApartado(fechaHora) &&
                   ahora < fechaHora.AddMinutes(ToleranciaLlegadaMinutos);
        }

        // La misma regla en SQL (alias R = RESERVA_MESA, @Ahora declarado con
        // SqlAhora). Requiere los parámetros de AgregarParametros.
        public const string SqlDebeEstarApartada = @"
            @Ahora >= CASE WHEN CAST(R.fecha_hora AS TIME) <= @HoraApertura
                           THEN CAST(CAST(R.fecha_hora AS DATE) AS DATETIME2)
                           ELSE DATEADD(MINUTE, -@ApartadoPrevio, R.fecha_hora) END
            AND @Ahora < DATEADD(MINUTE, @Tolerancia, R.fecha_hora)";

        // «Ahora» del servidor SQL (más el desfase del reloj de pruebas).
        public const string SqlAhora = "DECLARE @Ahora DATETIME2 = DATEADD(SECOND, @Desfase, SYSDATETIME());";

        // Reloj de pruebas: SOLO las pruebas automáticas lo cambian, para
        // simular otra hora sin tocar el reloj del sistema. En uso normal es 0.
        internal static TimeSpan DesfasePruebas = TimeSpan.Zero;

        // Hora local del equipo más el mismo desfase (igual a DateTime.Now en uso normal).
        public static DateTime AhoraLocal() { return DateTime.Now + DesfasePruebas; }

        public static void AgregarParametros(SqlCommand cmd)
        {
            cmd.Parameters.Add("@Desfase", SqlDbType.Int).Value = (int)DesfasePruebas.TotalSeconds;
            cmd.Parameters.Add("@HoraApertura", SqlDbType.Time).Value = HoraApertura;
            cmd.Parameters.Add("@ApartadoPrevio", SqlDbType.Int).Value = ApartadoPrevioMinutos;
            cmd.Parameters.Add("@Tolerancia", SqlDbType.Int).Value = ToleranciaLlegadaMinutos;
        }

        public static DateTime AhoraServidor(SqlConnection conexion, SqlTransaction tx)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT DATEADD(SECOND, @Desfase, SYSDATETIME());", conexion, tx))
            {
                cmd.Parameters.Add("@Desfase", SqlDbType.Int).Value = (int)DesfasePruebas.TotalSeconds;
                return Convert.ToDateTime(cmd.ExecuteScalar());
            }
        }

        public const int DiasAnticipacionMaxima = 30;
        public const int MaxPersonasPorMesa = 4;

        public static TimeSpan UltimoHorario
        {
            get { return HoraCierre - TimeSpan.FromMinutes(AtencionMinimaMinutos); }
        }

        // Horarios ofrecidos para una fecha. Si es hoy, solo los posteriores
        // a «ahora»; si es una fecha posterior, toda la jornada.
        public static List<DateTime> Horarios(DateTime fecha, DateTime ahora)
        {
            List<DateTime> lista = new List<DateTime>();
            DateTime dia = fecha.Date;
            if (dia < ahora.Date || dia > ahora.Date.AddDays(DiasAnticipacionMaxima))
                return lista;

            for (DateTime hora = dia.Add(HoraApertura);
                 hora <= dia.Add(UltimoHorario);
                 hora = hora.AddMinutes(IntervaloMinutos))
            {
                if (hora > ahora)
                    lista.Add(hora);
            }
            return lista;
        }

        public static bool HorarioValido(DateTime fechaHora, DateTime ahora)
        {
            if (fechaHora <= ahora) return false;
            if (fechaHora.Date > ahora.Date.AddDays(DiasAnticipacionMaxima)) return false;
            if (fechaHora.Second != 0 || fechaHora.Millisecond != 0) return false;
            if (fechaHora.Minute % IntervaloMinutos != 0) return false;
            TimeSpan t = fechaHora.TimeOfDay;
            return t >= HoraApertura && t <= UltimoHorario;
        }

        // Dos reservas de la misma mesa chocan si están a menos de la duración.
        public static bool EnConflicto(DateTime a, DateTime b)
        {
            return Math.Abs((a - b).TotalMinutes) < DuracionReservaMinutos;
        }

        // Una reserva cercana necesita la mesa libre AHORA (no ocupada por
        // un pedido en curso); una lejana solo necesita que no choque con
        // otra reserva.
        // También si la mesa debe apartarse ya (reserva de la apertura del día).
        public static bool RequiereMesaLibreAhora(DateTime fechaHora, DateTime ahora)
        {
            return fechaHora < ahora.AddMinutes(DuracionReservaMinutos) || DebeEstarApartada(fechaHora, ahora);
        }

        public static bool SeApartaAlRegistrar(DateTime fechaHora, DateTime ahora)
        {
            return DebeEstarApartada(fechaHora, ahora);
        }

        // ------------------------------------------------------------------
        // PEDIDOS NUEVOS EN UNA MESA CON RESERVA PRÓXIMA
        // No se abre un pedido nuevo de salón en una mesa cuya reserva
        // confirmada empieza dentro de DuracionReservaMinutos (2 horas) o
        // aún está en su tolerancia: el pedido impediría apartarla. Cuando el
        // cliente de la reserva llega (llegada confirmada) la reserva queda
        // 'finalizada' y su pedido se abre con normalidad.
        // Devuelve el motivo para el usuario, o null si se puede abrir.
        // Con transacción, bloquea las reservas leídas (UPDLOCK, HOLDLOCK).
        // ------------------------------------------------------------------
        public static string MotivoReservaProxima(SqlConnection conexion, SqlTransaction tx, int idMesa)
        {
            string sql = SqlAhora + @"
                SELECT TOP (1) R.id_reserva, R.fecha_hora, R.nombre_cliente, M.[número]
                FROM dbo.RESERVA_MESA R" + (tx != null ? " WITH (UPDLOCK, HOLDLOCK)" : "") + @"
                INNER JOIN dbo.MESA M ON M.id_mesa = R.id_mesa
                WHERE R.id_mesa = @Mesa
                  AND R.estado = 'confirmada'
                  AND R.fecha_hora < DATEADD(MINUTE, @Duracion, @Ahora)
                  AND DATEADD(MINUTE, @Tolerancia, R.fecha_hora) > @Ahora
                ORDER BY R.fecha_hora, R.id_reserva;";
            using (SqlCommand cmd = new SqlCommand(sql, conexion, tx))
            {
                AgregarParametros(cmd);
                cmd.Parameters.Add("@Duracion", SqlDbType.Int).Value = DuracionReservaMinutos;
                cmd.Parameters.Add("@Mesa", SqlDbType.Int).Value = idMesa;
                using (SqlDataReader rd = cmd.ExecuteReader())
                {
                    if (!rd.Read())
                        return null;
                    return "La Mesa " + rd[3] + " tiene la reserva N.º " + rd[0] + " a las " +
                           Convert.ToDateTime(rd[1]).ToString("HH:mm") + " (" + Convert.ToString(rd[2]) + ").\n\n" +
                           "No se abren pedidos nuevos en una mesa desde " + (DuracionReservaMinutos / 60) +
                           " horas antes de su reserva, para no comprometerla. Elija otra mesa.\n\n" +
                           "Si es el cliente de la reserva, confirme su llegada en Gestión de Mesas " +
                           "(«Confirmar llegada», también si llega antes de su hora).";
                }
            }
        }

        // ------------------------------------------------------------------
        // LLEGADA REGISTRADA PENDIENTE DE PEDIDO (alias R = RESERVA_MESA)
        // Tras «Confirmar llegada» la reserva queda 'finalizada' con
        // llegada_registrada (columna persistente, SQL/RES_01). Si su mesa está
        // libre o apartada, la mesa queda 'reservada' para ESE cliente hasta
        // que se guarde su pedido; si la mesa sigue ocupada por el cliente
        // anterior, el cliente queda «esperando mesa» (la mesa sigue ocupada).
        // No vence sola (ni a los 30 minutos ni a las 2 horas): se resuelve
        // al abrir su pedido, al reasignarla a otra mesa o al cancelarla.
        // Las reservas antiguas 'finalizada' sin llegada_registrada no cuentan.
        // ------------------------------------------------------------------
        public const string SqlLlegadaPendiente = @"
            R.estado = 'finalizada'
            AND R.llegada_registrada IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM dbo.PEDIDO PX WHERE PX.id_reserva = R.id_reserva)";

        // ------------------------------------------------------------------
        // MESA OCUPADA CON RESERVA PRÓXIMA (aviso del conflicto)
        // Desde que la reserva debe apartar la mesa (11:00: inicio del día;
        // otras horas: 15 min antes) y mientras la mesa siga ocupada:
        //   antes de la hora  -> «Mesa ocupada · Reserva próxima 19:00»
        //   desde la hora     -> «Mesa ocupada · Reserva en espera»
        //   cliente registrado-> «Mesa ocupada · Cliente llegó, esperando mesa»
        // ------------------------------------------------------------------
        public static string TextoConflicto(DateTime fechaHora, bool llego, DateTime ahora)
        {
            if (llego)
                return "Mesa ocupada · Cliente llegó, esperando mesa";
            return ahora >= fechaHora
                ? "Mesa ocupada · Reserva en espera"
                : "Mesa ocupada · Reserva próxima " + fechaHora.ToString("HH:mm");
        }

        // ------------------------------------------------------------------
        // REASIGNACIÓN DE UNA RESERVA A OTRA MESA
        // La atención empieza ahora si el cliente ya llegó; si no, a su hora
        // (o ahora, si ya pasó). La mesa destino debe estar libre, sin pedido
        // activo, sin otro cliente con llegada registrada, con capacidad y sin
        // otra reserva confirmada vigente a menos de DuracionReservaMinutos.
        // Devuelve el motivo para el usuario, o null si la mesa sirve.
        // Con transacción, bloquea la mesa y sus reservas (UPDLOCK, HOLDLOCK).
        // ------------------------------------------------------------------
        public static DateTime InicioAtencion(DateTime fechaHora, bool llego, DateTime ahora)
        {
            return llego || fechaHora < ahora ? ahora : fechaHora;
        }

        public static string MotivoMesaDestinoNoValida(SqlConnection conexion, SqlTransaction tx,
            int idReserva, int idMesaDestino, int personas, DateTime inicio)
        {
            string bloqueo = tx != null ? " WITH (UPDLOCK, HOLDLOCK)" : "";
            string sql = SqlAhora + @"
                SELECT M.[número], LOWER(LTRIM(RTRIM(M.estado))), M.capacidad,
                       (SELECT COUNT(*) FROM dbo.PEDIDO P" + bloqueo + @"
                        WHERE P.id_mesa = M.id_mesa AND P.canal = 'salon'
                          AND P.estado IN ('pendiente','en_preparacion','listo','entregado')),
                       (SELECT COUNT(*) FROM dbo.RESERVA_MESA R" + bloqueo + @"
                        WHERE R.id_mesa = M.id_mesa AND R.id_reserva <> @Reserva AND " + SqlLlegadaPendiente + @"),
                       (SELECT TOP (1) CONVERT(VARCHAR(5), R.fecha_hora, 108) FROM dbo.RESERVA_MESA R" + bloqueo + @"
                        WHERE R.id_mesa = M.id_mesa AND R.id_reserva <> @Reserva AND R.estado = 'confirmada'
                          AND DATEADD(MINUTE, @Tolerancia, R.fecha_hora) > @Ahora
                          AND ABS(DATEDIFF(MINUTE, R.fecha_hora, @Inicio)) < @DuracionConflicto
                        ORDER BY R.fecha_hora)
                FROM dbo.MESA M" + bloqueo + @"
                WHERE M.id_mesa = @Mesa;";
            using (SqlCommand cmd = new SqlCommand(sql, conexion, tx))
            {
                AgregarParametros(cmd);
                cmd.Parameters.Add("@Reserva", SqlDbType.Int).Value = idReserva;
                cmd.Parameters.Add("@Mesa", SqlDbType.Int).Value = idMesaDestino;
                cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicio;
                cmd.Parameters.Add("@DuracionConflicto", SqlDbType.Int).Value = DuracionReservaMinutos;
                using (SqlDataReader rd = cmd.ExecuteReader())
                {
                    if (!rd.Read())
                        return "La mesa no existe.";
                    string mesa = "La Mesa " + rd[0];
                    if (rd.GetString(1) != "libre")
                        return mesa + " ya no está libre (" + rd.GetString(1) + ").";
                    if (Convert.ToInt32(rd[2]) < personas)
                        return mesa + " no tiene capacidad para " + personas + " personas.";
                    if (Convert.ToInt32(rd[3]) > 0)
                        return mesa + " tiene un pedido activo.";
                    if (Convert.ToInt32(rd[4]) > 0)
                        return mesa + " está apartada para otro cliente que ya llegó.";
                    if (!rd.IsDBNull(5))
                        return mesa + " tiene otra reserva a las " + rd.GetString(5) + " (a menos de " +
                               (DuracionReservaMinutos / 60) + " horas).";
                    return null;
                }
            }
        }

        // «Ahora» del servidor (con el desfase del reloj de pruebas), para la interfaz.
        public static DateTime AhoraServidor()
        {
            using (SqlConnection conexion = new SqlConnection(BaseDatos.CadenaConexion))
            {
                conexion.Open();
                return AhoraServidor(conexion, null);
            }
        }

        // ¿La mesa (alias M) debe figurar 'reservada' ahora? Por una reserva
        // confirmada que ya debe apartarla, o por una llegada pendiente de pedido.
        public const string SqlMesaDebeEstarReservada = @"
            EXISTS (SELECT 1 FROM dbo.RESERVA_MESA R
                    WHERE R.id_mesa = M.id_mesa
                      AND ((R.estado = 'confirmada' AND " + SqlDebeEstarApartada + @")
                           OR (" + SqlLlegadaPendiente + @")))";

        // Estado que recibe una mesa al quedar sin pedido (cobro), en la misma
        // transacción: 'reservada' si debe seguir apartada; si no, 'libre'.
        // Alias M = MESA; requiere SqlAhora y AgregarParametros.
        public const string SqlEstadoAlLiberar = @"
            CASE WHEN " + SqlMesaDebeEstarReservada + @"
                 THEN 'reservada' ELSE 'libre' END";

        // ¿La reserva tiene su llegada confirmada en ESTA mesa y aún no tiene
        // pedido? (identidad y estado; con transacción, bloquea la reserva).
        public static bool LlegadaPendienteValida(SqlConnection conexion, SqlTransaction tx, int idReserva, int idMesa)
        {
            string sql = SqlAhora + @"
                SELECT COUNT(*) FROM dbo.RESERVA_MESA R" + (tx != null ? " WITH (UPDLOCK, HOLDLOCK)" : "") + @"
                WHERE R.id_reserva = @Reserva AND R.id_mesa = @Mesa AND " + SqlLlegadaPendiente + ";";
            using (SqlCommand cmd = new SqlCommand(sql, conexion, tx))
            {
                AgregarParametros(cmd);
                cmd.Parameters.Add("@Reserva", SqlDbType.Int).Value = idReserva;
                cmd.Parameters.Add("@Mesa", SqlDbType.Int).Value = idMesa;
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        // Texto para el usuario: cuándo se apartará la mesa.
        public static string TextoApartado(DateTime fechaHora)
        {
            return fechaHora.TimeOfDay <= HoraApertura
                ? "desde el inicio del " + fechaHora.ToString("dd/MM/yyyy") + " (reserva de la apertura)"
                : "a las " + InicioApartado(fechaHora).ToString("HH:mm") + " (" + ApartadoPrevioMinutos + " minutos antes)";
        }

        public static string TextoHorario()
        {
            return DateTime.Today.Add(HoraApertura).ToString("h:mm tt", Cultura) + " a " +
                   DateTime.Today.Add(HoraCierre).ToString("h:mm tt", Cultura) +
                   " (última reserva " + DateTime.Today.Add(UltimoHorario).ToString("HH:mm") + ")";
        }

        private static readonly System.Globalization.CultureInfo Cultura =
            System.Globalization.CultureInfo.GetCultureInfo("es-PE");
    }
}
