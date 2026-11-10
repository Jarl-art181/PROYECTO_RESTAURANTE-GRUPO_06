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

        // Llegada anticipada: el comensal puede confirmar su llegada desde
        // 60 minutos antes de la hora reservada (y Pedidos vincula su pedido
        // a la reserva dentro de esa ventana). No cambia con el apartado.
        public const int LlegadaAnticipadaMinutos = 60;

        // Nombre anterior, que aún usa Pedidos para vincular el pedido a la
        // reserva con llegada confirmada: es la ventana de llegada anticipada,
        // NO el apartado de la mesa (ver ApartadoPrevioMinutos).
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
