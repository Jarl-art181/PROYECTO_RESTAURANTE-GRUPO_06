using System;
using System.Configuration;
using System.Globalization;

namespace PROYECTO_RESTAURANTE
{
    // ==========================================================
    // IGV · CONFIGURACIÓN ACADÉMICA (no es validación tributaria SUNAT)
    // ----------------------------------------------------------
    // Única fuente de la tasa de IGV para Pedidos, Generar recibo, su
    // vista previa y el comprobante impreso. Los precios INCLUYEN el IGV:
    //     IGV = Importe × tasa / (100 + tasa)      (10 %: Importe × 10 / 110)
    // App.config:
    //   TasaIgvAcademica       tasa vigente (por defecto 10)
    //   TasaIgvAcademicaDesde  fecha y hora desde la que rige; los pedidos
    //                          cobrados antes conservan la tasa anterior
    //                          (18 %) al reimprimirse, porque la base de
    //                          datos no guarda el IGV de cada recibo.
    // ==========================================================
    // ==========================================================
    // FORMATO DE PANTALLA Y DOCUMENTOS (solo presentación)
    // ----------------------------------------------------------
    // Un único estilo en Inicio, Pedidos, Cobro, Recibos, Facturas y
    // Reportes, el de Perú: S/ 1,234.50 · 19/11/2026 · 19/11/2026 14:05.
    // No interviene en cálculos ni en lo que se escribe en los campos.
    // ==========================================================
    internal static class Formato
    {
        public static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

        public static string Monto(decimal v) { return v.ToString("N2", Peru); }
        public static string Soles(decimal v) { return "S/ " + Monto(v); }
        public static string Porcentaje(decimal v) { return v.ToString("0.0", Peru) + "%"; }
        public static string Fecha(DateTime f) { return f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); }
        public static string FechaHora(DateTime f) { return f.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture); }
        public static string FechaHoraCorta(DateTime f) { return f.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture); }

        // Encabezado de las ventanas: «Jue. 19 Nov 2026».
        public static string FechaEncabezado(DateTime f)
        {
            string[] dias = { "Dom.", "Lun.", "Mar.", "Mié.", "Jue.", "Vier.", "Sáb." };
            string[] meses = { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
            return dias[(int)f.DayOfWeek] + " " + f.Day.ToString("00") + " " + meses[f.Month - 1] + " " + f.Year;
        }
    }

    internal static class Impuestos
    {
        public const decimal TasaAnterior = 18m;
        private static readonly DateTime CambioAcademico = new DateTime(2026, 11, 9, 20, 0, 0);

        public static decimal Tasa
        {
            get
            {
                decimal t;
                string v = ConfigurationManager.AppSettings["TasaIgvAcademica"];
                return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out t) && t >= 0 && t < 100 ? t : 10m;
            }
        }

        public static DateTime? VigenteDesde
        {
            get
            {
                DateTime d;
                string v = ConfigurationManager.AppSettings["TasaIgvAcademicaDesde"];
                // Sin la clave se usa la fecha real del cambio: los recibos históricos
                // conservan su 18 % aunque se borre la configuración.
                return DateTime.TryParseExact(v, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : CambioAcademico;
            }
        }

        // Tasa que corresponde a un pedido: la vigente, o la anterior si se
        // cobró antes del cambio (reimpresión de recibos históricos).
        public static decimal TasaPara(DateTime? fechaCobro)
        {
            DateTime? desde = VigenteDesde;
            if (fechaCobro.HasValue && desde.HasValue && fechaCobro.Value < desde.Value)
                return TasaAnterior;
            return Tasa;
        }

        // IGV contenido en un importe que ya lo incluye.
        public static decimal IgvIncluido(decimal importe, decimal tasa)
        {
            return Math.Round(importe * tasa / (100m + tasa), 2, MidpointRounding.AwayFromZero);
        }

        public static string Etiqueta(decimal tasa)
        {
            return "IGV (" + tasa.ToString("0.##", CultureInfo.InvariantCulture) + " %)";
        }
    }
}
