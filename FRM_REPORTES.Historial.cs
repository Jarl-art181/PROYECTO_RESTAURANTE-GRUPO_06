using Guna.UI2.WinForms;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PROYECTO_RESTAURANTE
{
    // ==========================================================
    // REPORTES · HISTORIAL GENERAL DE PEDIDOS Y VENTAS
    // ----------------------------------------------------------
    // Apartado propio (botón arriba de la lista de reportes). Ocupa el
    // lugar de los filtros, tarjetas, tabla y gráfico de los reportes,
    // que se ocultan mientras se consulta el historial y vuelven al
    // elegir cualquier otro reporte. Predeterminado: «Todo el historial».
    // Tabla por páginas (100 pedidos) para que miles de pedidos no
    // congelen la ventana; los indicadores y la exportación usan TODOS
    // los pedidos que cumplen los filtros. Solo lectura y solo para el
    // Administrador (la misma autorización de Reportes).
    // ==========================================================
    public partial class FRM_REPORTES
    {
        private const int FilasPorPagina = 100;

        private Guna2Button btnHistorial;
        private readonly List<Control> controlesReporte = new List<Control>();
        private readonly List<Control> controlesHistorial = new List<Control>();
        private bool historialActivo;
        private bool historialTodo = true;
        private int paginaHistorial, totalHistorial;

        private Guna2Button btnTodoH, btnRangoH, btnGenerarH, btnDetalleH, btnExcelH, btnPdfH, btnAnteriorH, btnSiguienteH;
        private Guna2DateTimePicker dtDesdeH, dtHastaH;
        private Guna2ComboBox cmbEstadoH, cmbCanalH, cmbOrdenH;
        private Guna2TextBox txtPedidoH, txtClienteH;
        private Label lblRegistradosH, lblCobradosH, lblProcesoH, lblCanceladosH, lblMontoH, lblFiltrosH, lblPaginaH;
        private Guna2DataGridView tablaHistorial;

        // Llamado al construir la ventana, después de CrearCuerpo y antes de adaptar.
        private void CrearHistorial(HashSet<Control> delDiseno)
        {
            Control lista = botonesReporte[0].Parent;
            foreach (Control c in Controls)
                if (!delDiseno.Contains(c) && c != lista)
                    controlesReporte.Add(c);

            // Lista: «Historial general» arriba y los 10 reportes debajo (11 en el mismo panel).
            const int paso = 53, alto = 48;
            btnHistorial = new Guna2Button
            {
                Text = "Historial general", Bounds = new Rectangle(8, 8, 194, alto), BorderRadius = 10,
                FillColor = Color.Black, ForeColor = Color.Gold, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Left, TextOffset = new Point(4, 0), Cursor = Cursors.Hand
            };
            btnHistorial.Click += (s, e) => VerHistorial(true);
            lista.Controls.Add(btnHistorial);
            for (int i = 0; i < botonesReporte.Count; i++)
                botonesReporte[i].Bounds = new Rectangle(8, 8 + paso * (i + 1), 194, alto);

            int antes = Controls.Count;

            // Fila 1: período, generar, detalle y exportar.
            btnTodoH = Boton("Todo el historial", Naranja, new Rectangle(351, 146, 184, 40));
            btnRangoH = Boton("Rango de fechas", Dorado, new Rectangle(541, 146, 160, 40));
            dtDesdeH = Fecha(new Rectangle(707, 146, 150, 40));
            dtHastaH = Fecha(new Rectangle(863, 146, 150, 40));
            btnDetalleH = Boton("Ver detalle", Color.White, new Rectangle(1019, 146, 128, 40));
            btnDetalleH.BorderColor = Color.Goldenrod;
            btnDetalleH.BorderThickness = 1;
            btnGenerarH = Boton("Generar", Naranja, new Rectangle(1153, 146, 120, 40));
            btnExcelH = Boton("Excel", Color.FromArgb(120, 200, 120), new Rectangle(1281, 146, 120, 40));
            btnPdfH = Boton("PDF", Color.FromArgb(240, 120, 110), new Rectangle(1409, 146, 120, 40));
            dtDesdeH.MinDate = new DateTime(2000, 1, 1);

            // Fila 2: filtros.
            EtiquetaH("Estado", 351, 192);
            cmbEstadoH = Combo(new Rectangle(351, 210, 180, 36), new[] { "Todos los estados", "Pendientes", "En preparación", "Listos", "Entregados", "Cobrados", "Cancelados" });
            EtiquetaH("Tipo de pedido", 541, 192);
            cmbCanalH = Combo(new Rectangle(541, 210, 160, 36), new[] { "Todos los tipos", "Salón", "Delivery", "Recojo" });
            EtiquetaH("N.º de pedido", 711, 192);
            txtPedidoH = Texto(new Rectangle(711, 210, 120, 36), "Ej.: 25");
            txtPedidoH.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            txtPedidoH.MaxLength = 9;
            EtiquetaH("Cliente (nombre o documento)", 841, 192);
            txtClienteH = Texto(new Rectangle(841, 210, 220, 36), "Buscar cliente…");
            txtClienteH.MaxLength = 100;
            EtiquetaH("Orden", 1071, 192);
            cmbOrdenH = Combo(new Rectangle(1071, 210, 220, 36), new[] { "Más reciente primero", "Del primero al último" });
            lblFiltrosH = new Label
            {
                Location = new Point(1301, 206), Size = new Size(228, 44), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic), ForeColor = Color.DimGray
            };
            Controls.Add(lblFiltrosH);

            // Fila 3: indicadores (todos los pedidos que cumplen los filtros).
            string[] titulos = { "Pedidos registrados", "Pedidos cobrados", "Pendientes / en proceso", "Cancelados", "Monto cobrado" };
            Color[] colores = { Color.Khaki, Color.SpringGreen, Color.LightSteelBlue, Color.PaleVioletRed, Color.NavajoWhite };
            Label[] valores = new Label[5];
            for (int i = 0; i < 5; i++)
            {
                Guna2Panel p = new Guna2Panel { Bounds = new Rectangle(351 + i * 237, 258, 227, 72), BorderRadius = 12, FillColor = colores[i], BackColor = Color.Transparent };
                p.Controls.Add(new Label { Text = titulos[i], Location = new Point(12, 6), AutoSize = true, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) });
                valores[i] = new Label { Text = "-", Location = new Point(12, 30), Size = new Size(205, 32), BackColor = Color.Transparent, Font = new Font("Segoe UI", 15F, FontStyle.Bold | FontStyle.Italic) };
                p.Controls.Add(valores[i]);
                Controls.Add(p);
            }
            lblRegistradosH = valores[0]; lblCobradosH = valores[1]; lblProcesoH = valores[2]; lblCanceladosH = valores[3]; lblMontoH = valores[4];

            // Tabla (centro y derecha) y paginación.
            Guna2Panel cabecera = new Guna2Panel { Bounds = new Rectangle(571, 340, 958, 40), BorderRadius = 12, FillColor = Dorado, BackColor = Color.Transparent };
            cabecera.Controls.Add(new Label
            {
                Location = new Point(16, 9), AutoSize = true, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold), Text = "Historial general de pedidos y ventas"
            });
            Controls.Add(cabecera);
            tablaHistorial = new Guna2DataGridView
            {
                Name = "tablaHistorial", Bounds = new Rectangle(571, 386, 958, 514),
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, MultiSelect = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            Controls.Add(tablaHistorial);
            EstiloTablas.Configurar(tablaHistorial, 40, 28);
            tablaHistorial.ThemeStyle.HeaderStyle.BackColor = Dorado;
            tablaHistorial.ThemeStyle.HeaderStyle.ForeColor = Color.Black;
            tablaHistorial.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            tablaHistorial.ColumnHeadersHeight = 40;
            tablaHistorial.ThemeStyle.HeaderStyle.Height = 40;
            tablaHistorial.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) VerDetalle(); };

            btnAnteriorH = Boton("Anterior", Dorado, new Rectangle(571, 905, 110, 30));
            btnSiguienteH = Boton("Siguiente", Dorado, new Rectangle(1419, 905, 110, 30));
            lblPaginaH = new Label
            {
                Location = new Point(691, 905), Size = new Size(718, 30), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.DimGray
            };
            Controls.Add(lblPaginaH);

            for (int i = antes; i < Controls.Count; i++)
                controlesHistorial.Add(Controls[i]);
            foreach (Control c in controlesHistorial)
                c.Visible = false;

            // Eventos.
            btnTodoH.Click += (s, e) => { ModoTodo(true); CargarHistorial(0); };
            btnRangoH.Click += (s, e) => { ModoTodo(false); dtDesdeH.Focus(); };
            dtDesdeH.ValueChanged += (s, e) => { if (historialTodo) ModoTodo(false); };
            dtHastaH.ValueChanged += (s, e) => { if (historialTodo) ModoTodo(false); };
            btnGenerarH.Click += (s, e) => CargarHistorial(0);
            btnDetalleH.Click += (s, e) => VerDetalle();
            btnExcelH.Click += (s, e) => ExportarHistorial("xlsx");
            btnPdfH.Click += (s, e) => ExportarHistorial("pdf");
            btnAnteriorH.Click += (s, e) => CargarHistorial(paginaHistorial - 1);
            btnSiguienteH.Click += (s, e) => CargarHistorial(paginaHistorial + 1);
            foreach (Guna2ComboBox c in new[] { cmbEstadoH, cmbCanalH, cmbOrdenH })
                c.SelectedIndexChanged += (s, e) => { if (historialActivo && Visible) CargarHistorial(0); };
            foreach (Guna2TextBox t in new[] { txtPedidoH, txtClienteH })
                t.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CargarHistorial(0); } };
            ModoTodo(true);
        }

        private void EtiquetaH(string texto, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = texto, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Color.DimGray
            });
        }

        private Guna2TextBox Texto(Rectangle r, string ayuda)
        {
            Guna2TextBox t = new Guna2TextBox
            {
                BorderRadius = 10, BorderColor = Color.Goldenrod, Font = new Font("Segoe UI", 10F),
                ForeColor = Color.Black, PlaceholderText = ayuda
            };
            Controls.Add(t);
            t.Bounds = r;          // después de la letra: Guna reacomoda el cuadro al cambiarla
            return t;
        }

        private void ModoTodo(bool todo)
        {
            historialTodo = todo;
            btnTodoH.FillColor = todo ? Naranja : Dorado;
            btnRangoH.FillColor = todo ? Dorado : Naranja;
        }

        // Muestra u oculta el apartado (los reportes vuelven al elegir otro).
        private void VerHistorial(bool ver)
        {
            if (ver && !Autorizado())
                return;
            historialActivo = ver;
            SuspendLayout();
            foreach (Control c in controlesReporte) c.Visible = !ver;
            foreach (Control c in controlesHistorial) c.Visible = ver;
            ResumeLayout(true);
            btnHistorial.FillColor = ver ? Color.Gold : Color.Black;
            btnHistorial.ForeColor = ver ? Color.Black : Color.Gold;
            if (ver)
            {
                foreach (Guna2Button b in botonesReporte) b.FillColor = Color.White;
                CargarHistorial(0);
            }
        }

        internal FiltroHistorial FiltroH()
        {
            string[] estados = { null, "pendiente", "en_preparacion", "listo", "entregado", "cobrado", "cancelado" };
            string[] canales = { null, "salon", "delivery", "recojo" };
            DateTime d = dtDesdeH.Value.Date, h = dtHastaH.Value.Date;
            if (h < d) { DateTime x = d; d = h; h = x; }
            int numero;
            return new FiltroHistorial
            {
                TodoElHistorial = historialTodo, Desde = d, Hasta = h,
                Estado = estados[Math.Max(0, cmbEstadoH.SelectedIndex)],
                Canal = canales[Math.Max(0, cmbCanalH.SelectedIndex)],
                IdPedido = int.TryParse(txtPedidoH.Text.Trim(), out numero) ? (int?)numero : null,
                Cliente = txtClienteH.Text.Trim().Length == 0 ? null : txtClienteH.Text.Trim(),
                Ascendente = cmbOrdenH.SelectedIndex == 1
            };
        }

        private void CargarHistorial(int pagina)
        {
            if (!Autorizado())
                return;
            FiltroHistorial f = FiltroH();
            try
            {
                Cursor = Cursors.WaitCursor;
                IndicadoresHistorial ind = HistorialDatos.Indicadores(f);
                lblRegistradosH.Text = ind.Registrados.ToString("D2");
                lblCobradosH.Text = ind.Cobrados.ToString("D2");
                lblProcesoH.Text = ind.EnProceso.ToString("D2");
                lblCanceladosH.Text = ind.Cancelados.ToString("D2");
                lblMontoH.Text = "S/ " + ind.MontoCobrado.ToString("N2", Peru);
                lblFiltrosH.Text = f.Descripcion();

                totalHistorial = ind.Registrados;
                int paginas = Math.Max(1, (totalHistorial + FilasPorPagina - 1) / FilasPorPagina);
                paginaHistorial = Math.Max(0, Math.Min(pagina, paginas - 1));
                DataTable t = HistorialDatos.Consultar(f, paginaHistorial, FilasPorPagina);

                tablaHistorial.DataSource = null;
                tablaHistorial.Columns.Clear();
                tablaHistorial.AutoGenerateColumns = true;
                tablaHistorial.DataSource = t;
                Dictionary<string, float> pesos = new Dictionary<string, float>
                {
                    { "N.º pedido", 55 }, { "Fecha y hora", 95 }, { "Tipo", 60 }, { "Mesa", 42 }, { "Cliente", 110 }, { "Atendió", 110 },
                    { "Estado", 75 }, { "Subtotal (S/)", 65 }, { "Descuentos (S/)", 70 }, { "Total (S/)", 65 }, { "Método de pago", 95 }, { "Fecha de cobro", 95 }
                };
                foreach (DataGridViewColumn c in tablaHistorial.Columns)
                {
                    c.SortMode = DataGridViewColumnSortMode.NotSortable;     // el orden lo decide la consulta (todas las páginas)
                    Type tipo = t.Columns[c.DataPropertyName].DataType;
                    bool numero = tipo == typeof(decimal) || tipo == typeof(int);
                    if (tipo == typeof(decimal)) { c.DefaultCellStyle.Format = "N2"; c.DefaultCellStyle.FormatProvider = Peru; }
                    c.DefaultCellStyle.Alignment = numero ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft;
                    c.HeaderCell.Style.Alignment = c.DefaultCellStyle.Alignment;
                    c.MinimumWidth = 40;
                    float peso;
                    c.FillWeight = pesos.TryGetValue(c.HeaderText, out peso) ? peso : 80;
                }
                tablaHistorial.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                foreach (DataGridViewRow r in tablaHistorial.Rows)
                {
                    string estado = Convert.ToString(r.Cells["Estado"].Value);
                    r.Cells["Estado"].Style.ForeColor = estado == "Cobrado" ? Color.ForestGreen : estado == "Cancelado" ? Color.Firebrick : Color.DarkGoldenrod;
                }

                int desde = totalHistorial == 0 ? 0 : paginaHistorial * FilasPorPagina + 1;
                int hasta = Math.Min(totalHistorial, (paginaHistorial + 1) * FilasPorPagina);
                lblPaginaH.Text = totalHistorial == 0 ? "No hay pedidos con estos filtros."
                    : "Página " + (paginaHistorial + 1) + " de " + paginas + " · pedidos " + desde + "–" + hasta + " de " + totalHistorial +
                      " · doble clic o «Ver detalle» para ver un pedido";
                btnAnteriorH.Enabled = paginaHistorial > 0;
                btnSiguienteH.Enabled = paginaHistorial < paginas - 1;
            }
            catch (Exception ex)
            {
                Registro.Error("Reportes", ex);
                MessageBox.Show("No se pudo consultar el historial:\n" + ex.Message, "Gestión de reportes", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void HabilitarHistorial(bool ok)
        {
            if (btnHistorial == null) return;
            foreach (Control c in new Control[] { btnHistorial, btnGenerarH, btnDetalleH, btnExcelH, btnPdfH, tablaHistorial })
                c.Enabled = ok;
            if (!ok && tablaHistorial != null)
                tablaHistorial.DataSource = null;
        }

        // ------------------------------------------------------
        // DETALLE DEL PEDIDO (solo lectura)
        // ------------------------------------------------------
        private void VerDetalle()
        {
            if (!Autorizado() || tablaHistorial.CurrentRow == null)
            {
                if (tablaHistorial.CurrentRow == null)
                    MessageBox.Show("Seleccione un pedido de la tabla.", "Historial general", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int id = Convert.ToInt32(tablaHistorial.CurrentRow.Cells["N.º pedido"].Value);
            DetallePedido d;
            try { d = HistorialDatos.Detalle(id); }
            catch (Exception ex)
            {
                Registro.Error("Reportes", ex);
                MessageBox.Show("No se pudo leer el pedido:\n" + ex.Message, "Historial general", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (d == null) return;
            MostrarDetalle(d);
        }

        internal Form CrearVentanaDetalle(DetallePedido d)
        {
            DataRow c = d.Cabecera;
            Form v = new Form
            {
                Text = "Detalle del pedido (solo lectura)", ClientSize = new Size(720, 600), FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, ShowInTaskbar = false, Font = new Font("Segoe UI", 9.5F)
            };
            Guna2Panel cab = new Guna2Panel { Dock = DockStyle.Top, Height = 50, FillColor = Dorado };
            cab.Controls.Add(new Label
            {
                Text = "PEDIDO N.º " + c["N.º pedido"] + "  ·  " + Convert.ToString(c["Estado"]).ToUpperInvariant(), Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12F, FontStyle.Bold), BackColor = Color.Transparent
            });
            Label datos = new Label
            {
                Location = new Point(18, 60), Size = new Size(684, 64),
                Text = "Registrado: " + c["Fecha y hora"] + "     Tipo: " + c["Tipo"] + (Convert.ToString(c["Mesa"]) != "-" ? " · Mesa " + c["Mesa"] : "") +
                       "\nCliente: " + c["Cliente"] + "     Atendió: " + c["Atendió"]
            };
            Guna2DataGridView productos = Grilla(d.Productos, new Rectangle(18, 128, 684, 190));

            decimal subtotal = Convert.ToDecimal(c["Subtotal (S/)"]), descuentos = Convert.ToDecimal(c["Descuentos (S/)"]), total = Convert.ToDecimal(c["Total (S/)"]);
            Label resumen = new Label
            {
                Location = new Point(18, 324), Size = new Size(684, 84), Font = new Font("Segoe UI", 10F),
                Text = "Subtotal: S/ " + subtotal.ToString("N2", Peru) +
                       "     Descuentos: S/ " + descuentos.ToString("N2", Peru) +
                       (string.IsNullOrEmpty(d.Promocion) ? "" : "  (promoción: " + d.Promocion + ")") +
                       (d.Propina > 0 ? "\nPropina: S/ " + d.Propina.ToString("N2", Peru) : "") +
                       "\nTOTAL: S/ " + total.ToString("N2", Peru)
            };
            Label tituloPagos = new Label { Location = new Point(18, 410), AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Text = "Información de pago" };
            Control pagos = d.Pagos.Rows.Count == 0
                ? (Control)new Label { Location = new Point(18, 436), Size = new Size(684, 40), ForeColor = Color.DimGray, Text = "Sin pagos registrados (el pedido no está cobrado)." }
                : Grilla(d.Pagos, new Rectangle(18, 436, 684, 110));
            Guna2Button cerrar = new Guna2Button
            {
                Text = "Cerrar", Bounds = new Rectangle(562, 554, 140, 36), BorderRadius = 10, FillColor = Color.Gainsboro, ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            cerrar.Click += (s, e) => v.Close();
            v.Controls.AddRange(new Control[] { datos, productos, resumen, tituloPagos, pagos, cerrar, cab });
            v.CancelButton = null;
            v.KeyPreview = true;
            v.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) v.Close(); };
            return v;
        }

        private void MostrarDetalle(DetallePedido d)
        {
            using (Form v = CrearVentanaDetalle(d))
                v.ShowDialog(this);
        }

        private static Guna2DataGridView Grilla(DataTable t, Rectangle r)
        {
            Guna2DataGridView g = new Guna2DataGridView
            {
                Bounds = r, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, DataSource = t
            };
            EstiloTablas.Configurar(g, 32, 26);
            g.ThemeStyle.HeaderStyle.BackColor = Color.Gold;
            g.ThemeStyle.HeaderStyle.ForeColor = Color.Black;
            g.DataBindingComplete += (s, e) =>
            {
                foreach (DataGridViewColumn col in g.Columns)
                    if (col.ValueType == typeof(decimal) || col.ValueType == typeof(int))
                    {
                        if (col.ValueType == typeof(decimal)) { col.DefaultCellStyle.Format = "N2"; col.DefaultCellStyle.FormatProvider = Peru; }
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    }
            };
            return g;
        }

        // ------------------------------------------------------
        // EXPORTACIÓN: todos los pedidos que cumplen los filtros (no solo la página)
        // ------------------------------------------------------
        private void ExportarHistorial(string tipo)
        {
            if (!Autorizado())
                return;
            FiltroHistorial f = FiltroH();
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Exportar historial general";
                d.Filter = tipo == "pdf" ? "Documento PDF (*.pdf)|*.pdf" : "Libro de Excel (*.xlsx)|*.xlsx";
                d.FileName = "Historial_AlEstiloPiurano_" + (f.TodoElHistorial ? "completo" : f.Desde.ToString("yyyyMMdd") + "_" + f.Hasta.ToString("yyyyMMdd")) + "." + tipo;
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                int filas;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    filas = ExportarHistorialA(d.FileName, tipo, f);
                }
                catch (Exception ex)
                {
                    Registro.Error("Reportes", ex);
                    MessageBox.Show("No se pudo exportar:\n" + ex.Message, "Historial general", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
                if (MessageBox.Show(filas + " pedido(s) exportados en:\n" + d.FileName + "\n\n¿Abrir el archivo?", "Historial general",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo(d.FileName) { UseShellExecute = true }); } catch { }
                }
            }
        }

        // También lo usan las pruebas automáticas. Devuelve los pedidos exportados.
        internal int ExportarHistorialA(string ruta, string tipo, FiltroHistorial f)
        {
            IndicadoresHistorial ind = HistorialDatos.Indicadores(f);
            DataTable todo = HistorialDatos.Consultar(f, 0, 0);
            DataTable resumen = new DataTable();
            resumen.Columns.Add("Indicador", typeof(string));
            resumen.Columns.Add("Valor", typeof(string));
            resumen.Rows.Add("Pedidos registrados", ind.Registrados.ToString());
            resumen.Rows.Add("Pedidos cobrados", ind.Cobrados.ToString());
            resumen.Rows.Add("Pendientes / en proceso (sin cobrar)", ind.EnProceso.ToString());
            resumen.Rows.Add("Cancelados", ind.Cancelados.ToString());
            resumen.Rows.Add("Monto cobrado (S/)", ind.MontoCobrado.ToString("N2", Peru));
            List<SeccionReporte> secciones = new List<SeccionReporte>
            {
                new SeccionReporte { Titulo = "Resumen", Tabla = resumen },
                new SeccionReporte { Titulo = "Historial de pedidos", Tabla = todo }
            };
            string filtros = f.Descripcion() + " · Generado por " + (SesionActual.NombreCompleto ?? "-");
            if (tipo == "pdf")
                ExportadorReportes.Pdf(ruta, "Historial general de pedidos y ventas", filtros, secciones);
            else
                ExportadorReportes.Excel(ruta, "Historial general de pedidos y ventas", filtros, secciones);
            Registro.Info("Reportes", "Historial exportado (" + tipo + ", " + todo.Rows.Count + " pedidos) por empleado " + SesionActual.IdEmpleado + ": " + f.Descripcion());
            return todo.Rows.Count;
        }
    }
}
