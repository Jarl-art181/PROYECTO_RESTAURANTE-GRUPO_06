using Guna.UI2.WinForms;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PROYECTO_RESTAURANTE
{
    // ==========================================================
    // GENERAR RECIBO · DOCUMENTO: RECIBO DE VENTA O FACTURA
    // ----------------------------------------------------------
    // Los mismos tres cuadros del panel «Datos del cliente» sirven
    // a los dos documentos (no se mueve el diseño):
    //   Recibo : DNI (8 dígitos) · nombres y apellidos · celular — OPCIONALES:
    //            se puede cobrar sin datos; si se escriben, se validan.
    //   Factura: RUC (11 dígitos) · razón social · dirección fiscal
    // Cada documento conserva sus propios datos: cambiar de uno a otro
    // no mezcla un DNI con un RUC y, al volver, se recuperan.
    //
    // La factura es ACADÉMICA (serie FA01; en el A4 lo indican el recuadro
    // «Documento académico» y la nota del IGV del curso): no es un comprobante
    // electrónico autorizado por SUNAT. Se guarda en dbo.COMPROBANTE
    // con tipo 'F' en la MISMA transacción del cobro; verla, imprimirla
    // o guardarla en PDF solo lee los datos (no escribe en SQL).
    // ==========================================================
    public partial class Generar_Boleta
    {
        private const string SerieFactura = "FA01";

        private bool esFactura;
        private Guna2Button btnDocRecibo, btnDocFactura;
        private string autoNombre, autoExtra;          // datos que completó un cliente registrado
        private DateTime? fechaEmision;                 // del comprobante guardado

        private sealed class DatosDocumento
        {
            public string Documento = "", Nombre = "", Extra = "";
            public int? IdCliente;
            public string DocRegistrado, AutoNombre, AutoExtra;
        }
        private readonly DatosDocumento datosRecibo = new DatosDocumento();
        private readonly DatosDocumento datosFactura = new DatosDocumento();

        private sealed class ClienteExacto
        {
            public int Id;
            public string Documento, Nombre, Telefono, Direccion;
        }

        // ---------- Selector (en la fila del título «Datos del cliente») ----------
        private void CrearSelectorDocumento()
        {
            Control panel = txtDNICliente.Parent;
            chkIncluirCliente.Visible = false;     // el comprador siempre se identifica

            // Título corto («Cliente») para que el selector quepa en la misma fila.
            guna2HtmlLabel9.Text = "Cliente";
            btnDocRecibo = BotonDocumento("Recibo", new Rectangle(160, 191, 92, 28));
            btnDocFactura = BotonDocumento("Factura", new Rectangle(258, 191, 94, 28));
            panel.Controls.Add(btnDocRecibo);
            panel.Controls.Add(btnDocFactura);
            btnDocRecibo.BringToFront();
            btnDocFactura.BringToFront();
            btnDocRecibo.Click += (s, e) => CambiarDocumento(false);
            btnDocFactura.Click += (s, e) => CambiarDocumento(true);
            // En la factura, el tercer cuadro es la dirección fiscal: se ve en la vista previa.
            txtCelularCliente.TextChanged += (s, e) => { if (esFactura && !cargandoCliente) MostrarClienteEnVistaPrevia(); };
            consejosBoleta.SetToolTip(btnDocRecibo, "Recibo de venta: DNI y nombres opcionales (el cliente puede comprar sin dar sus datos).");
            consejosBoleta.SetToolTip(btnDocFactura, "Factura académica: RUC (11 dígitos), razón social y dirección fiscal.");
            ConfigurarCamposDocumento();
        }

        private static Guna2Button BotonDocumento(string texto, Rectangle r)
        {
            return new Guna2Button
            {
                Text = texto, Bounds = r, BorderRadius = 8, BorderThickness = 1, BorderColor = Color.Goldenrod,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Cursor = Cursors.Hand
            };
        }

        private void PintarSelector()
        {
            foreach (Guna2Button b in new[] { btnDocRecibo, btnDocFactura })
            {
                bool activo = (b == btnDocFactura) == esFactura;
                b.FillColor = activo ? Color.Gold : Color.White;
                b.ForeColor = Color.Black;
            }
        }

        private void ConfigurarCamposDocumento()
        {
            if (esFactura)
            {
                txtDNICliente.PlaceholderText = "RUC (11 dígitos)";
                txtDNICliente.MaxLength = 11;
                txtNombreCliente.PlaceholderText = "Razón social";
                txtNombreCliente.MaxLength = 255;
                txtCelularCliente.PlaceholderText = "Dirección fiscal";
                txtCelularCliente.MaxLength = 255;
                consejosBoleta.SetToolTip(btnBuscarCliente, "Buscar un cliente registrado con RUC.");
            }
            else
            {
                txtDNICliente.PlaceholderText = "DNI (opcional)";
                txtDNICliente.MaxLength = 8;
                txtNombreCliente.PlaceholderText = "Nombres y apellidos (opcional)";
                txtNombreCliente.MaxLength = 120;
                txtCelularCliente.PlaceholderText = "Celular (opcional)";
                txtCelularCliente.MaxLength = 20;
                consejosBoleta.SetToolTip(btnBuscarCliente, "Buscar un cliente registrado con DNI.");
            }
            PintarSelector();
        }

        // Cambia el documento: guarda los datos del actual y recupera los del otro.
        internal void CambiarDocumento(bool factura)
        {
            if (ModoReimpresion || !btnDocRecibo.Enabled)
                return;                                       // documento ya emitido
            if (factura == esFactura)
            {
                PintarSelector();
                return;
            }

            Guardar(esFactura ? datosFactura : datosRecibo);
            esFactura = factura;
            DatosDocumento d = esFactura ? datosFactura : datosRecibo;
            cargandoCliente = true;
            try
            {
                txtDNICliente.Text = "";
                txtNombreCliente.Text = "";
                txtCelularCliente.Text = "";
                ConfigurarCamposDocumento();
                txtDNICliente.Text = d.Documento;
                txtNombreCliente.Text = d.Nombre;
                txtCelularCliente.Text = d.Extra;
                idClienteSeleccionado = d.IdCliente;
                documentoClienteRegistrado = d.DocRegistrado;
                autoNombre = d.AutoNombre;
                autoExtra = d.AutoExtra;
                consejosBoleta.SetToolTip(txtNombreCliente, d.IdCliente.HasValue
                    ? "Cliente registrado (C" + d.IdCliente.Value.ToString("D4") + "): el pedido quedará asociado a su historial." : "");
            }
            finally
            {
                cargandoCliente = false;
            }
            MostrarClienteEnVistaPrevia();
            ActualizarVistaPrevia();
        }

        private void Guardar(DatosDocumento d)
        {
            d.Documento = txtDNICliente.Text;
            d.Nombre = txtNombreCliente.Text;
            d.Extra = txtCelularCliente.Text;
            d.IdCliente = idClienteSeleccionado;
            d.DocRegistrado = documentoClienteRegistrado;
            d.AutoNombre = autoNombre;
            d.AutoExtra = autoExtra;
        }

        // ---------- Clientes registrados (solo coincidencia EXACTA del documento) ----------
        private ClienteExacto BuscarClienteExacto(string documento)
        {
            try
            {
                using (SqlConnection cx = new SqlConnection(cadenaConexion))
                using (SqlCommand cmd = new SqlCommand(@"
                    SELECT TOP (1) id_cliente, LTRIM(RTRIM(nombre + ' ' + apellidos)),
                           ISNULL([teléfono], ''), ISNULL([dirección], '')
                    FROM dbo.CLIENTE WHERE documento = @Doc AND estado = 'activo';", cx))
                {
                    cmd.CommandTimeout = 10;
                    cmd.Parameters.Add("@Doc", SqlDbType.VarChar, 20).Value = documento;
                    cx.Open();
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        return new ClienteExacto
                        {
                            Id = r.GetInt32(0), Documento = documento, Nombre = r.GetString(1),
                            Telefono = r.GetString(2), Direccion = r.GetString(3)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Registro.Error("Boleta", ex);
                return null;
            }
        }

        private void AsignarClienteFactura(int idCliente, string ruc, string razonSocial, string direccion)
        {
            cargandoCliente = true;
            try
            {
                idClienteSeleccionado = idCliente;
                documentoClienteRegistrado = ruc;
                txtDNICliente.Text = ruc;
                txtNombreCliente.Text = razonSocial;
                if (!string.IsNullOrWhiteSpace(direccion)) txtCelularCliente.Text = direccion.Trim();
                autoNombre = txtNombreCliente.Text;
                autoExtra = txtCelularCliente.Text;
                consejosBoleta.SetToolTip(txtNombreCliente, "Cliente registrado (C" + idCliente.ToString("D4") + "): el pedido quedará asociado a su historial.");
            }
            finally
            {
                cargandoCliente = false;
            }
            MostrarClienteEnVistaPrevia();
        }

        // Al cambiar el documento de un cliente que se completó solo, se quitan
        // sus datos (si no se editaron): no quedan los de otra persona.
        private void SoltarClienteAnterior()
        {
            cargandoCliente = true;
            try
            {
                if (autoNombre != null && txtNombreCliente.Text == autoNombre) txtNombreCliente.Text = "";
                if (!string.IsNullOrEmpty(autoExtra) && txtCelularCliente.Text == autoExtra) txtCelularCliente.Text = "";
            }
            finally
            {
                cargandoCliente = false;
            }
            autoNombre = autoExtra = null;
        }

        // Pedido de un cliente registrado con RUC: sus datos se ofrecen en la factura.
        private void PrecargarEnFactura()
        {
            ClienteExacto c = BuscarClienteExacto((documentoClientePedido ?? "").Trim());
            if (c == null || c.Id != idClientePedido.Value) return;
            datosFactura.Documento = c.Documento;
            datosFactura.Nombre = c.Nombre;
            datosFactura.Extra = c.Direccion;
            datosFactura.IdCliente = c.Id;
            datosFactura.DocRegistrado = c.Documento;
            datosFactura.AutoNombre = c.Nombre;
            datosFactura.AutoExtra = c.Direccion;
            consejosBoleta.SetToolTip(btnDocFactura, "El cliente del pedido tiene RUC: sus datos están en «Factura».");
        }

        // ---------- Validación (antes de confirmar el cobro) ----------
        private static string SinEspaciosDobles(string t)
        {
            t = (t ?? "").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t;
        }

        // null = datos completos. Nunca se abre el cobro con un dato faltante.
        internal string ValidarDatosDocumento(out string documento, out string nombre, out string direccion)
        {
            documento = (txtDNICliente.Text ?? "").Trim();
            nombre = SinEspaciosDobles(txtNombreCliente.Text);
            direccion = esFactura ? SinEspaciosDobles(txtCelularCliente.Text) : "";

            if (esFactura)
            {
                if (documento.Length == 0)
                    return "Ingrese el RUC del cliente (11 dígitos).\n\nSi el cliente no tiene RUC, elija «Recibo» y use su DNI.";
                if (documento.Length != 11 || !documento.All(c => c >= '0' && c <= '9'))
                    return "El RUC debe tener exactamente 11 dígitos numéricos.";
                if (!new[] { "10", "15", "16", "17", "20" }.Contains(documento.Substring(0, 2)))
                    return "El RUC debe empezar con 10, 15, 16, 17 o 20.";
                if (nombre.Length < 3)
                    return "Ingrese la razón social del cliente.";
                if (nombre.Length > 255 || direccion.Length > 255)
                    return "La razón social y la dirección fiscal admiten hasta 255 caracteres.";
                return null;
            }

            // RECIBO: DNI y nombres OPCIONALES. Sin datos, el recibo se emite sin
            // comprador (NULL en SQL, sin inventar datos ni crear clientes).
            if (documento.Length == 0 && nombre.Length == 0)
                return null;
            if (documento.Length > 0)
            {
                if (documento.Length != 8 || !documento.All(c => c >= '0' && c <= '9'))
                    return "El DNI debe tener exactamente 8 dígitos numéricos.\n\nEl DNI es opcional: puede dejarlo vacío.";
                if (nombre.Length == 0)
                    return "Escriba los nombres y apellidos del titular del DNI, o deje ambos campos vacíos.";
            }
            if (!nombre.All(c => char.IsLetter(c) || c == ' ' || c == '\'' || c == '-' || c == '.'))
                return "El nombre del cliente solo puede contener letras.\n\nEl nombre es opcional: puede dejarlo vacío.";
            if (documento.Length > 0)
            {
                // Con DNI: nombre y apellido de 2+ letras (se admiten iniciales: «María F. Torres»).
                string[] partes = nombre.Split(' ');
                if (partes.Count(p => p.Trim('.', '-', '\'').Length >= 2) < 2)
                    return "Escriba los nombres y apellidos completos del titular del DNI (al menos un nombre y un apellido).";
            }
            else if (nombre.Trim('.', '-', '\'').Length < 3)
                return "Escriba el nombre del cliente (al menos 3 letras) o deje el campo vacío.";
            if (nombre.Length > 120)
                return "El nombre del cliente admite hasta 120 caracteres.";
            return null;
        }

        private string NombreDocumento { get { return esFactura ? "factura" : "recibo"; } }

        // ---------- Factura A4 (vista, impresión y PDF con el mismo dibujo) ----------
        private const float AnchoA4 = 827f, AltoA4 = 1169f;     // centésimas de pulgada

        private bool FacturaEmitida
        {
            get { return esFactura && !string.IsNullOrEmpty(numeroRecibo) && numeroRecibo.StartsWith(SerieFactura + "-"); }
        }

        private PrintDocument CrearDocumentoFactura()
        {
            if (cmbImpresoraBoleta.SelectedItem == null)
                throw new Exception("Primero seleccione una impresora.");
            PrintDocument documento = new PrintDocument();
            documento.PrinterSettings.PrinterName = cmbImpresoraBoleta.SelectedItem.ToString();
            if (!documento.PrinterSettings.IsValid)
            {
                documento.Dispose();
                throw new Exception("La impresora seleccionada no está disponible.");
            }
            PaperSize a4 = documento.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
            if (a4 == null)
            {
                documento.Dispose();
                throw new Exception("La impresora «" + cmbImpresoraBoleta.SelectedItem + "» no admite papel A4 " +
                    "(la factura se imprime en A4).\n\nElija otra impresora o use «Guardar PDF» en la vista previa.");
            }
            documento.DefaultPageSettings.PaperSize = a4;
            documento.DefaultPageSettings.Landscape = false;
            documento.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            documento.PrinterSettings.Copies = (short)Math.Max(1, (int)numCopiasBoleta.Value);
            documento.DocumentName = "Factura " + numeroBoletaDemo;

            int fila = 0, pagina = 1;
            documento.BeginPrint += (s, e) => { fila = 0; pagina = 1; };
            documento.PrintPage += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.TranslateTransform(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY);
                e.HasMorePages = DibujarFactura(g, e.PageBounds.Width, e.PageBounds.Height, ref fila, pagina++);
            };
            return documento;
        }

        // Páginas de la factura como imágenes (vista previa y PDF).
        internal List<Bitmap> PaginasFactura(float puntosPorPulgada)
        {
            List<Bitmap> paginas = new List<Bitmap>();
            int fila = 0, pagina = 1;
            bool hayMas = true;
            while (hayMas && pagina <= 20)
            {
                Bitmap b = new Bitmap((int)Math.Round(AnchoA4 * puntosPorPulgada / 100f), (int)Math.Round(AltoA4 * puntosPorPulgada / 100f));
                b.SetResolution(puntosPorPulgada, puntosPorPulgada);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.White);
                    g.PageUnit = GraphicsUnit.Inch;
                    g.PageScale = 0.01f;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    hayMas = DibujarFactura(g, AnchoA4, AltoA4, ref fila, pagina++);
                }
                paginas.Add(b);
            }
            return paginas;
        }

        // Dibuja una página A4 (unidades: centésimas de pulgada). true = hay otra página.
        private bool DibujarFactura(Graphics g, float ancho, float alto, ref int fila, int pagina)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            const float m = 50f;
            float util = ancho - 2 * m;
            float y = 45f;
            bool emitida = FacturaEmitida;
            Color dorado = Color.FromArgb(255, 215, 0);

            using (Font fEmpresa = new Font("Arial", 15, FontStyle.Bold))
            using (Font fTitulo = new Font("Arial", 14, FontStyle.Bold))
            using (Font fNegrita = new Font("Arial", 9.5f, FontStyle.Bold))
            using (Font fTexto = new Font("Arial", 9.5f))
            using (Font fPequeno = new Font("Arial", 8f))
            using (Font fTotal = new Font("Arial", 12, FontStyle.Bold))
            using (Pen borde = new Pen(Color.Black, 1.5f))
            using (Pen fino = new Pen(Color.Silver, 1f))
            using (SolidBrush fondoDorado = new SolidBrush(dorado))
            using (SolidBrush gris = new SolidBrush(Color.FromArgb(245, 245, 245)))
            using (StringFormat centro = new StringFormat { Alignment = StringAlignment.Center })
            using (StringFormat derecha = new StringFormat { Alignment = StringAlignment.Far })
            {
                // ---------- Encabezado: emisor y recuadro del documento ----------
                float xEmpresa = m;
                if (guna2PictureBox7.Image != null)
                {
                    Image logo = guna2PictureBox7.Image;
                    float wLogo = 150f, hLogo = wLogo * logo.Height / logo.Width;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(logo, m, y, wLogo, hLogo);
                    xEmpresa = m + wLogo + 16f;
                }
                g.DrawString("AL ESTILO PIURANO S.A.C.", fEmpresa, Brushes.Black, xEmpresa, y + 4);
                g.DrawString("Av. Principal 123, Trujillo - Perú", fTexto, Brushes.Black, xEmpresa, y + 34);
                g.DrawString("Tel.: 987 654 321", fTexto, Brushes.Black, xEmpresa, y + 52);

                RectangleF caja = new RectangleF(ancho - m - 250f, y, 250f, 104f);
                g.DrawRectangle(borde, caja.X, caja.Y, caja.Width, caja.Height);
                g.DrawString("R.U.C. 20601234567", fNegrita, Brushes.Black, new RectangleF(caja.X, caja.Y + 8, caja.Width, 18), centro);
                g.FillRectangle(fondoDorado, caja.X + 1, caja.Y + 30, caja.Width - 2, 28);
                g.DrawString("FACTURA", fTitulo, Brushes.Black, new RectangleF(caja.X, caja.Y + 33, caja.Width, 24), centro);
                g.DrawString(emitida ? numeroRecibo : "Sin emitir (" + CodigoPedido + ")", fNegrita, Brushes.Black,
                    new RectangleF(caja.X, caja.Y + 62, caja.Width, 18), centro);
                g.DrawString("Documento académico", fPequeno, Brushes.Black, new RectangleF(caja.X, caja.Y + 82, caja.Width, 16), centro);
                y += 124f;

                // ---------- Datos del adquiriente y del pedido ----------
                string razon = SinEspaciosDobles(txtNombreCliente.Text);
                string ruc = (txtDNICliente.Text ?? "").Trim();
                string direccion = SinEspaciosDobles(txtCelularCliente.Text);
                DateTime emision = fechaEmision ?? DateTime.Today;
                string canal = (tipoPedido ?? "").Trim().ToLowerInvariant();
                string pedido = "N.º " + idPedido + " · " + FormatearCanal(tipoPedido) +
                                (canal == "salon" || canal == "salón" ? " · Mesa " + numeroMesa : "");
                string[,] datos =
                {
                    { "Razón social:", razon.Length == 0 ? "-" : razon },
                    { "RUC:", ruc.Length == 0 ? "-" : ruc },
                    { "Dirección fiscal:", direccion.Length == 0 ? "-" : direccion },
                    { "Fecha de emisión:", emision.ToString("dd/MM/yyyy") },
                    { "Moneda:", "Soles (S/)" },
                    { "Pedido:", pedido },
                    { "Forma de pago:", string.IsNullOrWhiteSpace(lblBoletaFormaPago.Text) ? "-" : lblBoletaFormaPago.Text.Trim() }
                };
                float yDatos = y + 10f, etiqueta = 130f;
                for (int i = 0; i < datos.GetLength(0); i++)
                {
                    float h = Math.Max(g.MeasureString(datos[i, 1], fTexto, (int)(util - etiqueta - 20)).Height, 17f);
                    g.DrawString(datos[i, 0], fNegrita, Brushes.Black, m + 10, yDatos);
                    g.DrawString(datos[i, 1], fTexto, Brushes.Black, new RectangleF(m + 10 + etiqueta, yDatos, util - etiqueta - 20, h + 2));
                    yDatos += h + 3;
                }
                g.DrawRectangle(fino, m, y, util, yDatos - y + 8);
                y = yDatos + 22f;

                // ---------- Detalle ----------
                float cCant = 60f, cPrecio = 95f, cDesc = 95f, cImporte = 105f;
                float cDescripcion = util - cCant - cPrecio - cDesc - cImporte;
                Action cabecera = () =>
                {
                    g.FillRectangle(fondoDorado, m, y, util, 24f);
                    float x = m;
                    g.DrawString("Cant.", fNegrita, Brushes.Black, new RectangleF(x, y + 4, cCant, 18), centro); x += cCant;
                    g.DrawString("Descripción", fNegrita, Brushes.Black, x + 6, y + 4); x += cDescripcion;
                    g.DrawString("P. Unit.", fNegrita, Brushes.Black, new RectangleF(x, y + 4, cPrecio - 6, 18), derecha); x += cPrecio;
                    g.DrawString("Desc. unit.", fNegrita, Brushes.Black, new RectangleF(x, y + 4, cDesc - 6, 18), derecha); x += cDesc;
                    g.DrawString("Importe", fNegrita, Brushes.Black, new RectangleF(x, y + 4, cImporte - 6, 18), derecha);
                    y += 28f;
                };
                cabecera();

                const float altoTotales = 230f;
                float limite = alto - 70f;
                bool alterna = false;
                while (fila < productos.Count)
                {
                    LineaBoleta l = productos[fila];
                    float h = Math.Max(g.MeasureString(l.Descripcion ?? "", fTexto, (int)(cDescripcion - 12)).Height, 17f) + 6f;
                    if (y + h > limite) break;
                    if (alterna) g.FillRectangle(gris, m, y - 2, util, h);
                    alterna = !alterna;
                    float x = m;
                    g.DrawString(l.Cantidad.ToString(), fTexto, Brushes.Black, new RectangleF(x, y + 1, cCant, h), centro); x += cCant;
                    g.DrawString(l.Descripcion ?? "", fTexto, Brushes.Black, new RectangleF(x + 6, y + 1, cDescripcion - 12, h)); x += cDescripcion;
                    g.DrawString(Monto(l.PrecioUnitario), fTexto, Brushes.Black, new RectangleF(x, y + 1, cPrecio - 6, h), derecha); x += cPrecio;
                    g.DrawString(l.DescuentoLinea > 0 ? Monto(l.DescuentoLinea) : "-", fTexto, Brushes.Black, new RectangleF(x, y + 1, cDesc - 6, h), derecha); x += cDesc;
                    g.DrawString(Monto(l.Importe), fTexto, Brushes.Black, new RectangleF(x, y + 1, cImporte - 6, h), derecha);
                    y += h;
                    fila++;
                }
                g.DrawLine(fino, m, y + 2, m + util, y + 2);

                bool hayMas = fila < productos.Count || y + altoTotales > limite;
                if (!hayMas)
                {
                    // ---------- Totales (mismos importes que el recibo) ----------
                    y += 16f;
                    decimal baseSinIgv = subtotal - descuentoTotal - igvIncluido;
                    List<string[]> filas = new List<string[]>
                    {
                        new[] { "Subtotal (precios con IGV):", FormatoSoles(subtotal) },
                        new[] { TextoDescuento(), "- " + FormatoSoles(descuentoTotal) },
                        new[] { "Op. gravada (valor de venta):", FormatoSoles(baseSinIgv) },
                        new[] { Impuestos.Etiqueta(tasaIgv) + ":", FormatoSoles(igvIncluido) }
                    };
                    if (propina > 0) filas.Add(new[] { "Propina (no afecta al IGV):", FormatoSoles(propina) });
                    float xT = m + util - 330f;
                    foreach (string[] f in filas)
                    {
                        g.DrawString(f[0], fTexto, Brushes.Black, xT, y);
                        g.DrawString(f[1], fTexto, Brushes.Black, new RectangleF(xT, y, 330f, 18), derecha);
                        y += 20f;
                    }
                    g.FillRectangle(fondoDorado, xT - 8, y + 2, 338f, 30f);
                    g.DrawString("IMPORTE TOTAL:", fTotal, Brushes.Black, xT, y + 7);
                    g.DrawString(FormatoSoles(total), fTotal, Brushes.Black, new RectangleF(xT, y + 7, 326f, 22), derecha);
                    float yQr = y - 20f * filas.Count;
                    if (picQRBoleta.Image != null && emitida)
                        Codigo(g, picQRBoleta.Image, new RectangleF(m, yQr, 110f, 110f));
                    y += 52f;

                    g.DrawString("Caja: " + (cajeroRecibo ?? SesionActual.NombreCompleto ?? ""), fPequeno, Brushes.Black, m, y);
                    y += 16f;
                    g.DrawString("Precios con IGV incluido. IGV calculado con la tasa académica del " +
                        tasaIgv.ToString("0.##", CultureInfo.InvariantCulture) + " % (configuración del curso, no validación tributaria).",
                        fPequeno, Brushes.Black, new RectangleF(m, y, util, 30));
                }

                g.DrawString("Página " + pagina, fPequeno, Brushes.Gray, new RectangleF(m, alto - 45f, util, 16), derecha);

                // Antes del cobro: marca de agua (no es un documento emitido).
                if (!emitida)
                {
                    var estado = g.Save();
                    g.TranslateTransform(ancho / 2f, alto / 2f);
                    g.RotateTransform(-35f);
                    using (Font fAgua = new Font("Arial", 46, FontStyle.Bold))
                    using (SolidBrush agua = new SolidBrush(Color.FromArgb(70, 200, 0, 0)))
                        g.DrawString("VISTA PREVIA · NO EMITIDA", fAgua, agua, new RectangleF(-600f, -50f, 1200f, 100f), centro);
                    g.Restore(estado);
                }
                return hayMas;
            }
        }

        // Vista previa A4 con Imprimir y Guardar PDF (estos dos, solo ya emitida).
        private void MostrarFacturaA4()
        {
            List<Bitmap> paginas = PaginasFactura(100f);
            using (Form f = new Form())
            {
                f.Text = FacturaEmitida ? "Factura " + numeroRecibo : "Vista previa de la factura (sin emitir)";
                Rectangle pantalla = Screen.FromControl(this).WorkingArea;
                f.ClientSize = new Size(Math.Min(900, pantalla.Width - 40), Math.Min(980, pantalla.Height - 40));
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Color.White;
                f.ShowInTaskbar = false;
                f.MinimizeBox = false;

                Guna2Panel barra = new Guna2Panel { Dock = DockStyle.Top, Height = 56, FillColor = Color.Gold };
                Panel area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.DimGray };
                f.Controls.Add(area);
                f.Controls.Add(barra);

                Guna2Button imprimir = BotonBarra("Imprimir", Color.Black, Color.Gold);
                Guna2Button pdf = BotonBarra("Guardar PDF", Color.White, Color.Black);
                Guna2Button cerrar = BotonBarra("Cerrar", Color.Gainsboro, Color.Black);
                Label aviso = new Label
                {
                    AutoSize = false, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Text = FacturaEmitida ? "" : "Vista previa: la factura se emite al registrar el cobro.",
                    TextAlign = ContentAlignment.MiddleLeft
                };
                barra.Controls.AddRange(new Control[] { aviso, imprimir, pdf, cerrar });
                cerrar.Bounds = new Rectangle(f.ClientSize.Width - 130, 10, 116, 36);
                pdf.Bounds = new Rectangle(f.ClientSize.Width - 266, 10, 128, 36);
                imprimir.Bounds = new Rectangle(f.ClientSize.Width - 384, 10, 110, 36);
                aviso.Bounds = new Rectangle(14, 10, f.ClientSize.Width - 404, 36);
                imprimir.Enabled = pdf.Enabled = FacturaEmitida;
                EstiloBotones.AplicarA(imprimir);
                EstiloBotones.AplicarA(pdf);
                EstiloBotones.AplicarA(cerrar);

                int anchoHoja = Math.Min(827, f.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 40);
                int yHoja = 16;
                foreach (Bitmap b in paginas)
                {
                    int altoHoja = (int)Math.Round(anchoHoja * (double)b.Height / b.Width);
                    area.Controls.Add(new PictureBox
                    {
                        Image = b, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White,
                        Bounds = new Rectangle(20, yHoja, anchoHoja, altoHoja)
                    });
                    yHoja += altoHoja + 16;
                }

                imprimir.Click += (s, e) => btnImprimirBoleta_Click(imprimir, EventArgs.Empty);
                pdf.Click += (s, e) => GuardarFacturaPdfConDialogo(f);
                cerrar.Click += (s, e) => f.Close();
                try
                {
                    f.ShowDialog(this);
                }
                finally
                {
                    foreach (Bitmap b in paginas) b.Dispose();
                }
            }
        }

        private static Guna2Button BotonBarra(string texto, Color fondo, Color letra)
        {
            return new Guna2Button
            {
                Text = texto, BorderRadius = 10, FillColor = fondo, ForeColor = letra,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Cursor = Cursors.Hand
            };
        }

        private void GuardarFacturaPdfConDialogo(IWin32Window dueno)
        {
            if (!FacturaEmitida)
            {
                MessageBox.Show(dueno, "La factura se puede guardar en PDF después de registrar el cobro.", "Guardar PDF",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Documento PDF (*.pdf)|*.pdf";
                d.FileName = "Factura_" + numeroRecibo + ".pdf";
                d.Title = "Guardar factura en PDF";
                if (d.ShowDialog(dueno) != DialogResult.OK) return;
                try
                {
                    GuardarFacturaPdf(d.FileName);
                    MessageBox.Show(dueno, "Factura guardada en PDF:\n" + d.FileName + "\n\nNo se registró otra venta.", "Guardar PDF",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Registro.Error("Boleta", ex);
                    MessageBox.Show(dueno, "No se pudo guardar el PDF:\n" + ex.Message, "Guardar PDF",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // PDF A4: una imagen JPEG por página (mismo dibujo que la impresión).
        // Solo escribe el archivo: no consulta ni modifica SQL Server.
        internal void GuardarFacturaPdf(string ruta)
        {
            if (!FacturaEmitida)
                throw new InvalidOperationException("La factura aún no se ha emitido.");
            List<Bitmap> paginas = PaginasFactura(200f);
            List<byte[]> imagenes = new List<byte[]>();
            List<Size> tamanos = new List<Size>();
            try
            {
                ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (EncoderParameters p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                    foreach (Bitmap b in paginas)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            b.Save(ms, jpeg, p);
                            imagenes.Add(ms.ToArray());
                        }
                        tamanos.Add(b.Size);
                    }
                }
            }
            finally
            {
                foreach (Bitmap b in paginas) b.Dispose();
            }

            string carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
            string temporal = Path.Combine(carpeta, Path.GetFileName(ruta) + ".tmp");
            using (FileStream fs = new FileStream(temporal, FileMode.Create, FileAccess.Write))
            {
                List<long> posiciones = new List<long>();
                Encoding ascii = Encoding.ASCII;
                Action<string> w = s => { byte[] b = ascii.GetBytes(s); fs.Write(b, 0, b.Length); };
                int n = imagenes.Count;
                // Objetos: 1 catálogo, 2 páginas, 3 información; por página: imagen, contenido, página.
                Action<int> abrir = num => { while (posiciones.Count < num) posiciones.Add(0); posiciones[num - 1] = fs.Position; w(num + " 0 obj\n"); };
                w("%PDF-1.4\n%âã\n");
                abrir(1); w("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                StringBuilder kids = new StringBuilder();
                for (int i = 0; i < n; i++) kids.Append(6 + i * 3).Append(" 0 R ");
                abrir(2); w("<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>\nendobj\n");
                abrir(3); w("<< /Producer (AL ESTILO PIURANO) /Title (Factura " + numeroRecibo + ") >>\nendobj\n");
                for (int i = 0; i < n; i++)
                {
                    int img = 4 + i * 3, cont = img + 1, pag = img + 2;
                    abrir(img);
                    w("<< /Type /XObject /Subtype /Image /Width " + tamanos[i].Width + " /Height " + tamanos[i].Height +
                      " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + imagenes[i].Length + " >>\nstream\n");
                    fs.Write(imagenes[i], 0, imagenes[i].Length);
                    w("\nendstream\nendobj\n");
                    string contenido = "q 595.28 0 0 841.89 0 0 cm /Im1 Do Q\n";
                    abrir(cont); w("<< /Length " + contenido.Length + " >>\nstream\n" + contenido + "endstream\nendobj\n");
                    abrir(pag);
                    w("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /XObject << /Im1 " + img +
                      " 0 R >> >> /Contents " + cont + " 0 R >>\nendobj\n");
                }
                long xref = fs.Position;
                StringBuilder x = new StringBuilder("xref\n0 " + (posiciones.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long pos in posiciones) x.Append(pos.ToString("D10")).Append(" 00000 n \n");
                x.Append("trailer\n<< /Size " + (posiciones.Count + 1) + " /Root 1 0 R /Info 3 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
                w(x.ToString());
            }
            if (File.Exists(ruta)) File.Delete(ruta);
            File.Move(temporal, ruta);
        }
    }
}
