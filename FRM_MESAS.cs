
using Guna.Charts.WinForms;
using Guna.UI2.WinForms;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace PROYECTO_RESTAURANTE
{
    public partial class FRM_MESAS : Form

    {

        // Guarda el número de mesa que seleccionamos.
        private int mesaSeleccionada = 0;
        private string filtroActual = "todas";

        // La tabla de reservas crea sus columnas una sola vez.
        private bool historialConfigurado = false;
        private readonly string cadenaConexion =
    BaseDatos.CadenaConexion;

        // Aviso discreto de reserva futura (mesa aún libre): borde dorado y ayuda.
        private readonly ToolTip ayudaReservas = new ToolTip();
        private Color? bordeEstadoOriginal;
        // Reserva de hoy de la mesa seleccionada (para «Confirmar llegada»).
        private ControlReservasAutomaticas.ReservaVigente reservaLlegada;
        // Llegada confirmada de la mesa que aún no tiene pedido.
        private ControlReservasAutomaticas.ReservaVigente llegadaPendiente;
        // Reserva elegida en la tabla de reservas pendientes (se cancela por su id).
        private int? reservaElegidaTabla;
        private string textoLiberarOriginal;
        // Mesa ocupada con reserva en conflicto: «Reservar» pasa a «Confirmar
        // llegada» (cliente aún no registrado) o «Reasignar mesa» (en espera).
        private ControlReservasAutomaticas.ReservaVigente conflictoMesa;
        private string textoReservarOriginal;
        private int grosorEstadoOriginal;

        public FRM_MESAS()
        {


            InitializeComponent();
            // «Buscar mesa...» filtra al escribir (la conexión se perdió en el diseñador).
            txtBuscarMesa.TextChanged += (s, args) => FiltrarMesas();
            this.VisibleChanged += FRM_MESAS_VisibleChanged;
            this.FormClosed += (s, args) =>
                ControlReservasAutomaticas.MesasActualizadas -= RefrescarMesasAutomaticamente;
            this.FormClosed += (s, args) => ayudaReservas.Dispose();

            // Conectar el botón Liberar mesa.
            btnLiberarMesa.Click -= btnLiberarMesa_Click;
            btnLiberarMesa.Click += btnLiberarMesa_Click;
            // abrir mesa
            btnAbrirPedido.Click -= btnAbrirPedido_Click;
            btnAbrirPedido.Click += btnAbrirPedido_Click;
            // El Designer no vinculaba «Reservar» del panel de la mesa.
            btnReservar.Click -= btnReservar_Click;
            btnReservar.Click += btnReservar_Click;
            // Tabla de reservas: solo pendientes (confirmadas en su tolerancia).
            guna2HtmlLabel90.Text = "Reservas pendientes";
            textoLiberarOriginal = btnLiberarMesa.Text;
            textoReservarOriginal = btnReservar.Text;
            dgvHistorialMesas.CellClick += ElegirReservaDeTabla;
            ReenviarClicDeIconos();

            // Configuración de la ventana.
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered = true;

            ConfigurarBotonesIniciales();
            PrepararSeleccionMesas();

            // Los datos (contadores, tarjetas, reservas y filtro) se cargan
            // al mostrarse la ventana (FRM_MESAS_VisibleChanged). Antes se
            // cargaban aquí Y otra vez al mostrarse: consultas duplicadas.

            // Diseño común: se adapta a la pantalla (1366x768 a 1920x1080 y escalado
            // de Windows) y el encabezado muestra la sesión (el reloj ya es propio).
            DisenoVentanas.Adaptar(this);
            DisenoVentanas.Encabezado(this, false);
        }


        private void FRM_MESAS_VisibleChanged(object sender, EventArgs e)
        {
            ControlReservasAutomaticas.MesasActualizadas -= RefrescarMesasAutomaticamente;

            // El reloj solo trabaja mientras Mesas está visible.
            timerReloj.Enabled = this.Visible;

            if (this.Visible)
            {
                ActualizarReloj();

                // Al volver a mostrarse (el Navegador reutiliza esta
                // ventana) se recargan mesas, contadores y reservas.
                ControlReservasAutomaticas.MesasActualizadas += RefrescarMesasAutomaticamente;
                // Estado al día aunque el monitor aún no haya pasado (por
                // ejemplo, al abrir el sistema después de la hora de apartado).
                try { ControlReservasAutomaticas.SincronizarMesas(); }
                catch (Exception ex) { Registro.Error("Reservas", ex); }
                RefrescarMesasAutomaticamente(null, EventArgs.Empty);
            }
        }

        private void RefrescarMesasAutomaticamente(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.Visible) return;
            CargarContadoresMesas();
            CargarEstadosMesas();
            CargarHistorialMesas();
            if (mesaSeleccionada > 0)
                CargarDetalleMesa();
            FiltrarMesas();
        }




        private void btnLiberarMesa_Click(
            object sender, EventArgs e)
        {
            // Reserva elegida en la tabla: se cancela por su id_reserva, aunque
            // la mesa esté libre u ocupada (no libera una mesa ocupada).
            if (reservaElegidaTabla.HasValue)
            {
                CancelarReservaElegida(reservaElegidaTabla.Value);
                return;
            }

            // =====================================
            // 1. VALIDAR MESA SELECCIONADA
            // =====================================

            if (mesaSeleccionada == 0)
            {
                MessageBox.Show(
                    "Primero seleccione una mesa.",
                    "Gestión de mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            // Si la mesa está ocupada, abrir primero el cobro.
            if (lblDetalleEstado.Text.Trim().ToLower() == "ocupada")
            {
                CobrarYLiberarMesaOcupada();
                return;
            }

            // Cliente que llegó y se retiró sin pedido: se anula su llegada.
            if (llegadaPendiente != null && lblDetalleEstado.Text.Trim().ToLower() == "reservada")
            {
                if (MessageBox.Show(
                        "La Mesa " + mesaSeleccionada + " está apartada para el cliente que ya llegó:" + "\n\n" +
                        "Reserva N.º " + llegadaPendiente.IdReserva + " · " + llegadaPendiente.Cliente + " · " +
                        llegadaPendiente.FechaHora.ToString("HH:mm") + "\n\n" +
                        "Si se retiró sin pedir, se anula la llegada (la reserva queda cancelada) " +
                        "y la mesa se libera. ¿Continuar?",
                        "Anular llegada", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                try
                {
                    bool libre = ControlReservasAutomaticas.AnularLlegadaSinPedido(llegadaPendiente.IdReserva);
                    ActualizarTodo();
                    MessageBox.Show("Llegada anulada." + (libre ? "\nMesa " + mesaSeleccionada + " disponible." : ""),
                        "Gestión de mesas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Registro.Error("Mesas", ex);
                    ActualizarTodo();
                    MessageBox.Show("No se pudo anular la llegada:\n" + ex.Message,
                        "Gestión de mesas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            // =====================================
            // 2. RESERVA: CANCELACIÓN EXPLÍCITA
            // =====================================
            // Antes este botón solo cambiaba MESA a 'libre' y la
            // reserva quedaba 'confirmada' para siempre (por ejemplo,
            // la reserva N.º 6 de la Mesa 8). Ahora se identifica la
            // reserva por su id_reserva y se cancela junto con la mesa
            // en UNA transacción; la reserva queda en el historial.

            ControlReservasAutomaticas.ReservaVigente reserva;
            try
            {
                reserva = ControlReservasAutomaticas.ObtenerReservaConfirmada(mesaSeleccionada);
            }
            catch (Exception ex)
            {
                Registro.Error("Mesas", ex);
                MessageBox.Show("No se pudo consultar la reserva:\n" + ex.Message,
                    "Gestión de mesas", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (reserva == null)
            {
                MessageBox.Show(
                    "La Mesa " + mesaSeleccionada + " no tiene reservas confirmadas.\n\n" +
                    (lblDetalleEstado.Text.Trim().ToLower() == "libre"
                        ? "Ya está libre."
                        : "Las mesas ocupadas se liberan al registrar el cobro."),
                    "Liberar mesa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                ActualizarTodo();
                return;
            }

            DialogResult respuesta = MessageBox.Show(
                "Se cancelará la reserva:\n\n" +
                "Reserva N.º " + reserva.IdReserva + "\n" +
                "Mesa: " + reserva.NumeroMesa + "\n" +
                "Cliente: " + reserva.Cliente + "\n" +
                "Fecha y hora: " + reserva.FechaHora.ToString("dd/MM/yyyy HH:mm") + "\n\n" +
                "La reserva quedará registrada como cancelada en el historial.\n" +
                "¿Desea cancelarla?",
                "Cancelar reserva",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (respuesta != DialogResult.Yes)
                return;

            try
            {
                bool liberada = ControlReservasAutomaticas.CancelarReserva(reserva.IdReserva);

                ActualizarTodo();

                MessageBox.Show(
                    "Reserva N.º " + reserva.IdReserva + " cancelada." +
                    (liberada
                        ? "\nMesa " + reserva.NumeroMesa + " disponible."
                        : "\nEl estado de la mesa no cambió (tiene otra reserva o un pedido activo)."),
                    "Gestión de mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Registro.Error("Mesas", ex);
                ActualizarTodo();
                MessageBox.Show(
                    "No se pudo cancelar la reserva:\n" + ex.Message,
                    "Gestión de mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // Fila de la tabla de reservas: el botón pasa a «Cancelar reserva» para
        // esa reserva (por su id), sin importar el estado de la mesa.
        private void ElegirReservaDeTabla(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvHistorialMesas.Rows.Count)
                return;
            DataRowView fila = dgvHistorialMesas.Rows[e.RowIndex].DataBoundItem as DataRowView;
            if (fila == null)
                return;
            reservaElegidaTabla = Convert.ToInt32(fila["Numero"]);
            MostrarBotonCancelarReserva();
        }

        private void MostrarBotonCancelarReserva()
        {
            btnLiberarMesa.Text = "       Cancelar reserva";
            btnLiberarMesa.Enabled = true;
        }

        private void OlvidarReservaDeTabla()
        {
            if (!reservaElegidaTabla.HasValue)
                return;
            reservaElegidaTabla = null;
            btnLiberarMesa.Text = textoLiberarOriginal;
            string estado = lblDetalleEstado.Text.Trim().ToLower();
            btnLiberarMesa.Enabled = mesaSeleccionada > 0 && (estado == "ocupada" || estado == "reservada");
            dgvHistorialMesas.ClearSelection();
        }

        private void CancelarReservaElegida(int idReserva)
        {
            ControlReservasAutomaticas.ReservaVigente r;
            try { r = ControlReservasAutomaticas.ObtenerReservaVigentePorId(idReserva); }
            catch (Exception ex)
            {
                Registro.Error("Mesas", ex);
                MessageBox.Show("No se pudo consultar la reserva:\n" + ex.Message, "Cancelar reserva",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (r == null)
            {
                MessageBox.Show("La reserva N.º " + idReserva + " ya no está pendiente.", "Cancelar reserva",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                OlvidarReservaDeTabla();
                ActualizarTodo();
                return;
            }
            if (MessageBox.Show(
                    "Se cancelará la reserva:\n\n" +
                    "Reserva N.º " + r.IdReserva + "\n" +
                    "Mesa: " + r.NumeroMesa + "\n" +
                    "Cliente: " + r.Cliente + "\n" +
                    "Fecha y hora: " + r.FechaHora.ToString("dd/MM/yyyy HH:mm") + "\n" +
                    (r.Llego ? "Situación: el cliente llegó y espera mesa (se cancela su espera).\n" : "") + "\n" +
                    "Las demás reservas y el pedido activo de la mesa no cambian.\n" +
                    "¿Desea cancelarla?",
                    "Cancelar reserva", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                bool liberada = r.Llego
                    ? ControlReservasAutomaticas.AnularLlegadaSinPedido(r.IdReserva)
                    : ControlReservasAutomaticas.CancelarReserva(r.IdReserva);
                OlvidarReservaDeTabla();
                ActualizarTodo();
                MessageBox.Show("Reserva N.º " + r.IdReserva + " cancelada." +
                    (liberada ? "\nMesa " + r.NumeroMesa + " disponible." : ""),
                    "Gestión de mesas", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Registro.Error("Mesas", ex);
                OlvidarReservaDeTabla();
                ActualizarTodo();
                MessageBox.Show("No se pudo cancelar la reserva:\n" + ex.Message, "Gestión de mesas",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Refresca tarjetas, contadores, detalle, filtros y reservas.
        private void ActualizarTodo()
        {
            CargarContadoresMesas();
            CargarEstadosMesas();
            CargarHistorialMesas();
            CargarDetalleMesa();
        }


        private void CargarContadoresMesas()
        {
            string consulta = @"
        SELECT
            COUNT(*) AS TotalMesas,

            COUNT(CASE
                WHEN LOWER(LTRIM(RTRIM(M.estado))) =
                    'libre'
                THEN 1
            END) AS MesasLibres,

            COUNT(CASE
                WHEN LOWER(LTRIM(RTRIM(M.estado))) =
                    'ocupada'
                THEN 1
            END) AS MesasOcupadas,

            -- Mismo dato que las tarjetas y el filtro Reservada
            -- (antes se contaba RESERVA_MESA y no coincidía con MESA).
            COUNT(CASE
                WHEN LOWER(LTRIM(RTRIM(M.estado))) =
                    'reservada'
                THEN 1
            END) AS MesasReservadas

        FROM dbo.MESA M

        WHERE M.id_sucursal = @Sucursal;";

            try
            {
                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                using (SqlCommand comando =
                    new SqlCommand(consulta, conexion))
                {
                    comando.Parameters.Add(
                        "@Sucursal",
                        SqlDbType.Int).Value = 1;

                    conexion.Open();

                    using (SqlDataReader lector =
                        comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            lblMesasTotales.Text =
                                Convert.ToInt32(
                                    lector["TotalMesas"]).ToString("D2");

                            lblMesasLibres.Text =
                                Convert.ToInt32(
                                    lector["MesasLibres"]).ToString("D2");

                            lblMesasOcupadas.Text =
                                Convert.ToInt32(
                                    lector["MesasOcupadas"]).ToString("D2");

                            lblMesasReservadas.Text =
                                Convert.ToInt32(
                                    lector["MesasReservadas"]).ToString("D2");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error al cargar los contadores:\n" +
                    ex.Message,
                    "Gestión de Mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void but_Cerrar_Click(object sender, EventArgs e)
        {
            // "Cerrar Sesión": libera los módulos y vuelve al inicio de sesión.
            Navegador.CerrarSesion();
        }




        private void guna2Button2_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRMS_PEDIDOS>(this);
        }

        private void guna2Button1_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_MESAS>(this);
        }

        private void guna2Button5_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_COCINA>(this);
        }

        private void guna2Button3_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_EMPLEADOS>(this);
        }

        private void guna2Button9_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_MENU>(this);
        }

        private void guna2Button8_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_INVENTARIO>(this);
        }

        private void guna2Button7_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_PROMOCIONES>(this);
        }

        private void guna2Button6_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_REPORTES>(this);
        }
        // mostrar la fecha y hora 

        private void timerReloj_Tick(object sender, EventArgs e)
        {
            ActualizarReloj();
        }
        private void ActualizarReloj()
        {
            DateTime ahora = DateTime.Now;

            lblHora.Text = ahora.ToString("HH:mm:ss");

            string[] dias =
            {
        "Dom.", "Lun.", "Mar.", "Mié.",
        "Jue.", "Vier.", "Sáb."
    };

            string[] meses =
            {
        "Ene", "Feb", "Mar", "Abr",
        "May", "Jun", "Jul", "Ago",
        "Sep", "Oct", "Nov", "Dic"
    };

            lblFecha.Text =
                dias[(int)ahora.DayOfWeek] + " " +
                ahora.Day.ToString("00") + " " +
                meses[ahora.Month - 1] + " " +
                ahora.Year;
        }



        // actualizar el flow de tarjetas de mesa 1 al 10


        // Ayuda emergente en el cuadro de estado (y en su texto interno).
        private void AyudaEstado(Control estado, string texto)
        {
            ayudaReservas.SetToolTip(estado, texto);
            foreach (Control c in estado.Controls)
                ayudaReservas.SetToolTip(c, texto);
        }

        private void CargarEstadosMesas()
        {
            // ==========================================
            // 1. ESTADOS DE LAS 10 MESAS
            // ==========================================

            Guna2TextBox[] estados =
            {
        lblEstadoMesa1, lblEstadoMesa2,
        lblEstadoMesa3, lblEstadoMesa4,
        lblEstadoMesa5, lblEstadoMesa6,
        lblEstadoMesa7, lblEstadoMesa8,
        lblEstadoMesa9, lblEstadoMesa10
    };

            // ==========================================
            // 2. PANELES DE PEDIDO Y MESERO
            // ==========================================

            Control[] panelesPedido =
            {
        pnlPedidoMesero1, pnlPedidoMesero2,
        pnlPedidoMesero3, pnlPedidoMesero4,
        pnlPedidoMesero5, pnlPedidoMesero6,
        pnlPedidoMesero7, pnlPedidoMesero8,
        pnlPedidoMesero9, pnlPedidoMesero10
    };

            // ==========================================
            // 3. NÚMEROS DE PEDIDO
            // ==========================================

            Guna2HtmlLabel[] pedidos =
            {
        lblPedidoMesa1, lblPedidoMesa2,
        lblPedidoMesa3, lblPedidoMesa4,
        lblPedidoMesa5, lblPedidoMesa6,
        lblPedidoMesa7, lblPedidoMesa8,
        lblPedidoMesa9, lblPedidoMesa10
    };

            // ==========================================
            // 4. NOMBRES DE MESEROS
            // ==========================================

            Guna2HtmlLabel[] meseros =
            {
        lblMeseroMesa1, lblMeseroMesa2,
        lblMeseroMesa3, lblMeseroMesa4,
        lblMeseroMesa5, lblMeseroMesa6,
        lblMeseroMesa7, lblMeseroMesa8,
        lblMeseroMesa9, lblMeseroMesa10
    };

            // ==========================================
            // 5. CONSULTAR MESAS EN SQL SERVER
            // ==========================================

            string consulta = ReservaReglas.SqlAhora + @"
    SELECT
        M.[número] AS NumeroMesa,
        M.estado AS EstadoMesa,
        P.id_pedido AS NumeroPedido,
        RX.fecha_hora AS ProximaReserva,
        RX.id_reserva AS NumeroReserva,
        RC.fecha_hora AS ConflictoHora,
        RC.nombre_cliente AS ConflictoCliente,
        RC.llego AS ConflictoLlego,

        LTRIM(RTRIM(
            ISNULL(E.nombre, '') + ' ' +
            ISNULL(E.apellidos, '')
        )) AS NombreMesero

    FROM dbo.MESA M

    OUTER APPLY
    (
        SELECT TOP (1)
            PD.id_pedido,
            PD.id_mesero
        FROM dbo.PEDIDO PD
        WHERE PD.id_mesa = M.id_mesa
          AND PD.id_sucursal = M.id_sucursal
          AND PD.canal = 'salon'
        AND PD.estado IN
(
    'pendiente',
    'en_preparacion',
    'listo',
    'entregado'
)
        ORDER BY PD.fecha_hora DESC,
                 PD.id_pedido DESC
    ) P

    LEFT JOIN dbo.EMPLEADO E
        ON E.id_empleado = P.id_mesero

    OUTER APPLY
    (
        SELECT TOP (1) R.fecha_hora, R.id_reserva
        FROM dbo.RESERVA_MESA R
        WHERE R.id_mesa = M.id_mesa
          AND R.estado = 'confirmada'
          AND R.fecha_hora > @Ahora
          AND CAST(R.fecha_hora AS DATE) = CAST(@Ahora AS DATE)
        ORDER BY R.fecha_hora, R.id_reserva
    ) RX

    -- Conflicto de una mesa ocupada: cliente que ya llegó y espera, o
    -- reserva confirmada que ya debe apartarla (regla de ReservaReglas).
    OUTER APPLY
    (
        SELECT TOP (1) R.fecha_hora, R.nombre_cliente,
               CASE WHEN R.estado = 'finalizada' THEN 1 ELSE 0 END AS llego
        FROM dbo.RESERVA_MESA R
        WHERE R.id_mesa = M.id_mesa
          AND ((" + ReservaReglas.SqlLlegadaPendiente + @")
               OR (R.estado = 'confirmada' AND " + ReservaReglas.SqlDebeEstarApartada + @"))
        ORDER BY CASE WHEN R.estado = 'finalizada' THEN 0 ELSE 1 END, R.fecha_hora
    ) RC

    WHERE M.id_sucursal = @Sucursal
      AND M.[número] BETWEEN 1 AND 10

    ORDER BY M.[número];";


            try
            {
                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                {
                    conexion.Open();

                    using (SqlCommand comando =
                        new SqlCommand(consulta, conexion))
                    {
                        comando.Parameters.Add(
                            "@Sucursal", SqlDbType.Int).Value = 1;
                        ReservaReglas.AgregarParametros(comando);

                        using (SqlDataReader lector =
                            comando.ExecuteReader())
                        {
                            // ==================================
                            // 6. LIMPIAR LAS 10 TARJETAS
                            // ==================================

                            for (int i = 0; i < 10; i++)
                            {
                                panelesPedido[i].Visible = false;

                                pedidos[i].Text = "";
                                meseros[i].Text = "";

                                estados[i].ReadOnly = true;
                                estados[i].TabStop = false;

                                estados[i].TextAlign =
                                    HorizontalAlignment.Center;

                                estados[i].ForeColor = Color.Black;

                                estados[i].Text = "Sin datos";
                                estados[i].FillColor = Color.LightGray;

                                // Sin aviso de reserva futura hasta leer los datos.
                                if (!bordeEstadoOriginal.HasValue)
                                {
                                    bordeEstadoOriginal = estados[i].BorderColor;
                                    grosorEstadoOriginal = estados[i].BorderThickness;
                                }
                                estados[i].BorderColor = bordeEstadoOriginal.Value;
                                estados[i].BorderThickness = grosorEstadoOriginal;
                                AyudaEstado(estados[i], null);
                            }

                            // ==================================
                            // 7. LEER LOS DATOS DE SQL SERVER
                            // ==================================

                            while (lector.Read())
                            {
                                int numeroMesa =
                                    Convert.ToInt32(
                                        lector["NumeroMesa"]);

                                if (numeroMesa < 1 ||
                                    numeroMesa > 10)
                                {
                                    continue;
                                }

                                // Mesa 1 = posición 0.
                                int posicion = numeroMesa - 1;

                                string estado =
                                    Convert.ToString(
                                        lector["EstadoMesa"])
                                        .Trim()
                                        .ToLower();

                                string numeroPedido = "";
                                string nombreMesero = "";

                                // ==============================
                                // 8. OBTENER NÚMERO DE PEDIDO
                                // ==============================

                                if (lector["NumeroPedido"] !=
                                    DBNull.Value)
                                {
                                    int idPedido =
                                        Convert.ToInt32(
                                            lector["NumeroPedido"]);

                                    numeroPedido =
                                        idPedido.ToString("D4");
                                }

                                // ==============================
                                // 9. OBTENER NOMBRE DEL MESERO
                                // ==============================

                                if (lector["NombreMesero"] !=
                                    DBNull.Value)
                                {
                                    nombreMesero =
                                        Convert.ToString(
                                            lector["NombreMesero"]);
                                }

                                // ==============================
                                // 10. MESA LIBRE - VERDE
                                // ==============================

                                if (estado == "libre")
                                {
                                    estados[posicion].Text =
                                        "Libre";

                                    estados[posicion].FillColor =
                                        Color.FromArgb(
                                            0, 235, 133);

                                    panelesPedido[posicion].Visible =
                                        false;

                                    // Reserva futura de hoy (aún no apartada): la mesa
                                    // sigue libre; se indica con un borde dorado y ayuda.
                                    if (lector["ProximaReserva"] != DBNull.Value)
                                    {
                                        DateTime hora = Convert.ToDateTime(lector["ProximaReserva"]);
                                        estados[posicion].BorderColor = Color.Goldenrod;
                                        estados[posicion].BorderThickness = 3;
                                        AyudaEstado(estados[posicion],
                                            "Reserva N.º " + lector["NumeroReserva"] + " hoy a las " + hora.ToString("HH:mm") +
                                            ".\nLa mesa se apartará " + ReservaReglas.TextoApartado(hora) + ".");
                                    }
                                }

                                // ==============================
                                // 11. MESA RESERVADA - AMARILLO
                                // ==============================

                                else if (estado == "reservada")
                                {
                                    estados[posicion].Text = "Reservada";
                                    estados[posicion].FillColor =
                                        Color.FromArgb(242, 230, 139);

                                    // No mostrar detalle en la tarjeta.
                                    panelesPedido[posicion].Visible = false;
                                }

                                // ==============================
                                // 12. MESA OCUPADA - ROJO
                                // ==============================

                                else if (estado == "ocupada")
                                {
                                    estados[posicion].Text = "Ocupada";
                                    estados[posicion].FillColor =
                                        Color.FromArgb(250, 128, 120);

                                    // IMPORTANTE: NO mostrar pedido ni mesero
                                    // en la tarjeta antes de pulsar Detalle pedido.
                                    pedidos[posicion].Text = "";
                                    meseros[posicion].Text = "";
                                    panelesPedido[posicion].Visible = false;

                                    // Reserva próxima / en espera con la mesa aún ocupada:
                                    // sigue «Ocupada» (su pedido no cambia) con borde dorado
                                    // y el aviso del conflicto al pasar el mouse.
                                    if (lector["ConflictoHora"] != DBNull.Value)
                                    {
                                        DateTime hora = Convert.ToDateTime(lector["ConflictoHora"]);
                                        estados[posicion].BorderColor = Color.Goldenrod;
                                        estados[posicion].BorderThickness = 3;
                                        AyudaEstado(estados[posicion],
                                            ReservaReglas.TextoConflicto(hora, Convert.ToInt32(lector["ConflictoLlego"]) == 1, ReservaReglas.AhoraServidor()) +
                                            "\nReserva " + hora.ToString("HH:mm") + " · " + Convert.ToString(lector["ConflictoCliente"]));
                                    }
                                }

                                // ==============================
                                // 13. MESA INACTIVA
                                // ==============================

                                else
                                {
                                    estados[posicion].Text =
                                        "Inactiva";

                                    estados[posicion].FillColor =
                                        Color.LightGray;

                                    panelesPedido[posicion].Visible =
                                        false;
                                }
                            }
                        }
                    }
                }

                // =====================================
                // 14. ACTUALIZAR FILTROS DE MESAS
                // =====================================

                FiltrarMesas();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error al cargar los estados de las mesas:\n" +
                    ex.Message,
                    "Gestión de Mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }



        // filtrar mesas con el buton 

        private void FiltrarMesas()
        {
            // 1. Guardar las 10 tarjetas.
            Control[] tarjetas =
            {
        pnlMesa1,
        pnlMesa2,
        pnlMesa3,
        pnlMesa4,
        pnlMesa5,
        pnlMesa6,
        pnlMesa7,
        pnlMesa8,
        pnlMesa9,
        pnlMesa10
    };

            // 2. Guardar los estados.
            Guna2TextBox[] estados =
            {
        lblEstadoMesa1,
        lblEstadoMesa2,
        lblEstadoMesa3,
        lblEstadoMesa4,
        lblEstadoMesa5,
        lblEstadoMesa6,
        lblEstadoMesa7,
        lblEstadoMesa8,
        lblEstadoMesa9,
        lblEstadoMesa10
    };

            // 3. Obtener el texto de búsqueda.
            string busqueda =
                txtBuscarMesa.Text.Trim().ToLower();

            busqueda = busqueda.Replace("mesa", "").Trim();

            // 4. Suspender el diseño mientras filtramos.
            flowLayoutPanel1.SuspendLayout();

            // 5. Recorrer las 10 mesas.
            for (int i = 0; i < 10; i++)
            {
                int numeroMesa = i + 1;

                string estado =
                    estados[i].Text.Trim().ToLower();

                bool mostrar = true;

                // Verificar el filtro de estado.
                if (filtroActual != "todas")
                {
                    if (estado != filtroActual)
                    {
                        mostrar = false;
                    }
                }

                // Verificar el número buscado.
                if (busqueda != "")
                {
                    if (numeroMesa.ToString() != busqueda)
                    {
                        mostrar = false;
                    }
                }

                // Mostrar u ocultar la tarjeta.
                tarjetas[i].Visible = mostrar;
            }

            // 6. Reorganizar las tarjetas visibles.
            flowLayoutPanel1.ResumeLayout(true);
            flowLayoutPanel1.PerformLayout();

            // 7. Regresar la barra al inicio.
            flowLayoutPanel1.AutoScrollPosition = new Point(0, 0);
        }

        private void btnTodas_Click(object sender, EventArgs e)
        {
            filtroActual = "todas";
            FiltrarMesas();
        }

        private void btnLibres_Click(object sender, EventArgs e)
        {
            filtroActual = "libre";
            FiltrarMesas();
        }

        private void btnOcupadas_Click(object sender, EventArgs e)
        {
            filtroActual = "ocupada";
            FiltrarMesas();
        }


        private void btnReservadas_Click(object sender, EventArgs e)
        {
            filtroActual = "reservada";
            FiltrarMesas();
        }




        private void btnNuevaReserva_Click(
            object sender, EventArgs e)
        {
            AbrirNuevaReserva(0);
        }

        // numeroMesa = 0: el usuario elige la mesa en la ventana.
        private void AbrirNuevaReserva(int numeroMesa)
        {
            using (RESERVAR_MESA ventana =
                new RESERVAR_MESA(numeroMesa))
            {
                if (ventana.ShowDialog(this) ==
                    DialogResult.OK)
                {
                    // Actualizar reservas registradas.
                    CargarHistorialMesas();

                    // Actualizar estado actual de las mesas.
                    CargarContadoresMesas();
                    CargarEstadosMesas();
                    FiltrarMesas();
                    if (mesaSeleccionada > 0)
                        CargarDetalleMesa();
                }
            }
        }








        private void SeleccionarMesa(object sender, EventArgs e)
        {
            Control control = sender as Control;

            if (control == null || control.Tag == null)
                return;

            int numeroMesa;

            if (!int.TryParse(control.Tag.ToString(), out numeroMesa))
                return;

            mesaSeleccionada = numeroMesa;
            OlvidarReservaDeTabla();

            // Solo datos basicos, estado y botones.
            // Los productos se muestran al pulsar Detalle pedido.
            CargarDetalleMesa();
        }

        private void CargarDetalleMesa()
        {
            // ====================================================
            // 1. VERIFICAR LA MESA SELECCIONADA
            // ====================================================

            if (mesaSeleccionada == 0)
            {
                return;
            }

            // ====================================================
            // 2. CONSULTAR LOS DATOS EN SQL SERVER
            // ====================================================


            string consulta = @"
    SELECT
        M.[número] AS NumeroMesa,
        M.capacidad AS Capacidad,
        M.estado AS EstadoMesa,

        P.id_pedido AS NumeroPedido,
        P.fecha_hora AS FechaPedido,
        P.nombre_contacto AS Cliente,

        LTRIM(RTRIM(
            ISNULL(E.nombre, '') + ' ' +
            ISNULL(E.apellidos, '')
        )) AS NombreMesero

    FROM dbo.MESA M

    OUTER APPLY
    (
        SELECT TOP (1)
            PD.id_pedido,
            PD.fecha_hora,
            PD.nombre_contacto,
            PD.id_mesero

        FROM dbo.PEDIDO PD

        WHERE PD.id_mesa = M.id_mesa
          AND PD.id_sucursal = M.id_sucursal
          AND PD.canal = 'salon'
          AND PD.estado IN
          (
              'pendiente',
              'en_preparacion',
              'listo',
              'entregado'
          )

        ORDER BY PD.fecha_hora DESC,
                 PD.id_pedido DESC
    ) P

    LEFT JOIN dbo.EMPLEADO E
        ON E.id_empleado = P.id_mesero

    WHERE M.id_sucursal = @Sucursal
      AND M.[número] = @NumeroMesa;";


            try
            {
                // ====================================================
                // 3. CONECTAR CON SQL SERVER
                // ====================================================

                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                {
                    conexion.Open();

                    using (SqlCommand comando =
                        new SqlCommand(consulta, conexion))
                    {
                        comando.Parameters.Add(
                            "@NumeroMesa",
                            SqlDbType.Int
                        ).Value = mesaSeleccionada;
                        comando.Parameters.Add(
    "@Sucursal",
    SqlDbType.Int).Value = 1;

                        using (SqlDataReader lector =
                            comando.ExecuteReader())
                        {
                            if (lector.Read())
                            {
                                // ========================================
                                // 4. OBTENER LOS DATOS PRINCIPALES
                                // ========================================

                                int numeroMesa =
                                    Convert.ToInt32(
                                        lector["NumeroMesa"]);

                                int capacidad =
                                    Convert.ToInt32(
                                        lector["Capacidad"]);

                                string estado =
                                    lector["EstadoMesa"]
                                    .ToString()
                                    .Trim()
                                    .ToLower();

                                // ========================================
                                // 5. MOSTRAR NÚMERO Y CAPACIDAD
                                // ========================================

                                lblTituloDetalleMesa.Text =
                                 "Mesa " + numeroMesa + " de 10";

                                lblDetalleNumero.Text =
                                    numeroMesa.ToString();

                                lblDetalleCapacidad.Text =
                                    capacidad + " personas";

                                // ========================================
                                // 6. LIMPIAR LA INFORMACIÓN ANTERIOR
                                // ========================================

                                lblDetallePedido.Text = "Ninguno";
                                lblDetalleCliente.Text = "Ninguno";
                                lblDetalleMesero.Text = "Ninguno";
                                lblDetalleTiempo.Text = "Ninguno";

                                // ========================================
                                // 7. VERIFICAR SI EXISTE UN PEDIDO
                                // ========================================

                                bool tienePedido = false;

                                if (lector["NumeroPedido"] != DBNull.Value)
                                {
                                    tienePedido = true;
                                }

                                // ========================================
                                // 8. ACTUALIZAR LOS DOS ESTADOS
                                // ========================================

                                // MESA LIBRE - VERDE
                                if (estado == "libre")
                                {
                                    lblDetalleEstado.Text = "Libre";

                                    textEstadoDetalleMesa.Text = "Libre";

                                    textEstadoDetalleMesa.FillColor =
                                        Color.FromArgb(0, 235, 133);
                                }

                                // MESA OCUPADA - ROJO
                                else if (estado == "ocupada")
                                {
                                    lblDetalleEstado.Text = "Ocupada";
                                    textEstadoDetalleMesa.Text = "Ocupada";
                                    textEstadoDetalleMesa.FillColor =
                                        Color.FromArgb(250, 128, 120);
                                }

                                // MESA RESERVADA - AMARILLO PASTEL
                                else if (estado == "reservada")
                                {
                                    lblDetalleEstado.Text = "Reservada";

                                    textEstadoDetalleMesa.Text = "Reservada";

                                    textEstadoDetalleMesa.FillColor =
                                        Color.FromArgb(242, 230, 139);
                                }

                                // MESA INACTIVA
                                else
                                {
                                    lblDetalleEstado.Text = "Inactiva";

                                    textEstadoDetalleMesa.Text = "Inactiva";

                                    textEstadoDetalleMesa.FillColor =
                                        Color.LightGray;
                                }

                                // Configuración del indicador superior.
                                textEstadoDetalleMesa.ForeColor = Color.Black;

                                textEstadoDetalleMesa.TextAlign =
                                    HorizontalAlignment.Center;

                                textEstadoDetalleMesa.ReadOnly = true;

                                // ========================================
                                // 9. MOSTRAR PEDIDO Y MESERO
                                // ========================================
                                // Mostrar información básica al seleccionar mesa.
                                if (estado == "ocupada")
                                {
                                    lblDetallePedido.Text = "En curso";
                                    lblDetalleCliente.Text = "Consultar detalle";
                                    lblDetalleMesero.Text = "Consultar detalle";
                                    lblDetalleTiempo.Text = "Consultar detalle";
                                }
                                else
                                {
                                    lblDetallePedido.Text = "Ninguno";
                                    lblDetalleCliente.Text = "-";
                                    lblDetalleMesero.Text = "-";
                                    lblDetalleTiempo.Text = "-";
                                }

                                // No mostrar los detalles todavía.
                                lblDetalleCliente.Text = "-";
                                lblDetalleMesero.Text = "-";
                                lblDetalleTiempo.Text = "-";


                                // ========================================
                                // 11. CONFIGURAR BOTONES SEGÚN EL ESTADO
                                // ========================================



                                // Reserva confirmada de hoy: el cliente puede llegar
                                // aunque la tarjeta aún figure libre (falta > 15 min).
                                reservaLlegada = null;
                                llegadaPendiente = null;
                                if (estado == "reservada")
                                {
                                    try { llegadaPendiente = ControlReservasAutomaticas.ObtenerLlegadaPendiente(numeroMesa); }
                                    catch (Exception ex) { Registro.Error("Reservas", ex); }
                                    if (llegadaPendiente != null)
                                        lblDetalleCliente.Text = "Llegó: reserva " + llegadaPendiente.FechaHora.ToString("HH:mm") +
                                                                 " · " + llegadaPendiente.Cliente;
                                }
                                if (llegadaPendiente == null && (estado == "libre" || estado == "reservada"))
                                {
                                    try { reservaLlegada = ControlReservasAutomaticas.ObtenerReservaDeHoy(numeroMesa); }
                                    catch (Exception ex) { Registro.Error("Reservas", ex); }
                                    if (reservaLlegada != null)
                                        lblDetalleCliente.Text = "Reserva " + reservaLlegada.FechaHora.ToString("HH:mm") +
                                                                 " · " + reservaLlegada.Cliente;
                                }

                                if (estado == "libre")
                                {
                                    btnAbrirPedido.Text = reservaLlegada != null ? "Confirmar llegada" : "Nuevo pedido";
                                    btnAbrirPedido.Enabled = true;
                                    btnReservar.Enabled = true;
                                    btnLiberarMesa.Enabled = false;
                                }
                                else if (estado == "ocupada")
                                {
                                    btnAbrirPedido.Text = "Detalle pedido";
                                    btnAbrirPedido.Enabled = tienePedido;
                                    btnReservar.Enabled = false;
                                    btnLiberarMesa.Enabled = true;
                                }

                                else if (estado == "reservada")
                                {
                                    btnAbrirPedido.Text = llegadaPendiente != null ? "Abrir pedido" : "Confirmar llegada";
                                    btnAbrirPedido.Enabled = true;
                                    btnReservar.Enabled = false;
                                    btnLiberarMesa.Enabled = true;
                                }
                                else
                                {
                                    btnAbrirPedido.Text = "No disponible";
                                    btnAbrirPedido.Enabled = false;
                                    btnReservar.Enabled = false;
                                    btnLiberarMesa.Enabled = false;
                                }

                                // Mesa ocupada: el cliente de la reserva puede llegar y
                                // esperar, o trasladarse a otra mesa libre.
                                conflictoMesa = null;
                                btnReservar.Text = textoReservarOriginal;
                                if (estado == "ocupada")
                                {
                                    ControlReservasAutomaticas.ReservaVigente hoy = null;
                                    try
                                    {
                                        conflictoMesa = ControlReservasAutomaticas.ObtenerConflictoMesaOcupada(numeroMesa);
                                        if (conflictoMesa == null || !conflictoMesa.Llego)
                                            hoy = ControlReservasAutomaticas.ObtenerReservaDeHoy(numeroMesa);
                                    }
                                    catch (Exception ex) { Registro.Error("Reservas", ex); }

                                    if (conflictoMesa != null)
                                    {
                                        string cliente = conflictoMesa.Cliente.Length > 18 ? conflictoMesa.Cliente.Substring(0, 17) + "…" : conflictoMesa.Cliente;
                                        string aviso = ReservaReglas.TextoConflicto(conflictoMesa.FechaHora, conflictoMesa.Llego, ReservaReglas.AhoraServidor());
                                        lblDetalleCliente.Text = (conflictoMesa.Llego ? "Llegó, esperando · " : "Reserva ") +
                                                                 conflictoMesa.FechaHora.ToString("HH:mm") + " · " + cliente;
                                        ayudaReservas.SetToolTip(textEstadoDetalleMesa, aviso);
                                        ayudaReservas.SetToolTip(lblDetalleCliente, aviso);
                                    }
                                    if (conflictoMesa != null && conflictoMesa.Llego)
                                    {
                                        btnReservar.Text = "       Reasignar mesa";
                                        btnReservar.Enabled = true;
                                    }
                                    else if (hoy != null)
                                    {
                                        conflictoMesa = conflictoMesa ?? hoy;
                                        btnReservar.Text = "       Confirmar llegada";
                                        btnReservar.Enabled = true;
                                    }
                                }
                                else
                                {
                                    ayudaReservas.SetToolTip(textEstadoDetalleMesa, null);
                                    ayudaReservas.SetToolTip(lblDetalleCliente, null);
                                }

                                // Una reserva elegida en la tabla conserva «Cancelar reserva».
                                if (reservaElegidaTabla.HasValue)
                                    MostrarBotonCancelarReserva();

                                // ========================================
                                // 12. CAMBIAR EL FILLCOLOR DE LOS BOTONES
                                // ========================================

                                Guna2Button[] botones =
                                {
                            btnAbrirPedido,
                            btnReservar,
                            btnLiberarMesa
                        };

                                foreach (Guna2Button boton in botones)
                                {
                                    if (boton.Enabled == true)
                                    {
                                        // Botón habilitado: dorado.
                                        boton.FillColor = Color.Gold;
                                    }
                                    else
                                    {
                                        // Botón deshabilitado: gris.
                                        boton.FillColor = Color.Gray;
                                    }

                                    // Guna UI2 utiliza DisabledState
                                    // cuando Enabled es false.
                                    boton.DisabledState.FillColor =
                                        Color.Gray;

                                    boton.DisabledState.ForeColor =
                                        Color.White;
                                }
                            }
                            else
                            {
                                MessageBox.Show(
                                    "No se encontró la mesa seleccionada.",
                                    "Gestión de Mesas",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error al cargar el detalle de la mesa:\n" +
                    ex.Message,
                    "Gestión de Mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }




        private void PrepararSeleccionMesas()
        {
            // Guardar los diez paneles principales.
            Control[] tarjetas =
            {
        pnlMesa1, pnlMesa2, pnlMesa3,
        pnlMesa4, pnlMesa5, pnlMesa6,
        pnlMesa7, pnlMesa8, pnlMesa9,
        pnlMesa10
    };

            for (int i = 0; i < tarjetas.Length; i++)
            {
                // Número de la mesa.
                int numero = i + 1;

                // Asignar el número al panel.
                tarjetas[i].Tag = numero;

                // Conectar el clic del panel.
                tarjetas[i].Click -= SeleccionarMesa;
                tarjetas[i].Click += SeleccionarMesa;

                // Conectar los controles que están dentro.
                foreach (Control hijo in tarjetas[i].Controls)
                {
                    hijo.Tag = numero;

                    hijo.Click -= SeleccionarMesa;
                    hijo.Click += SeleccionarMesa;

                    // Incluye controles dentro de otros paneles.
                    foreach (Control nieto in hijo.Controls)
                    {
                        nieto.Tag = numero;

                        nieto.Click -= SeleccionarMesa;
                        nieto.Click += SeleccionarMesa;
                    }
                }
            }
        }
        // Los íconos del panel «Detalle de la mesa» están encima de los
        // botones y se quedaban con el clic: lo reenvían al botón de abajo.
        private void ReenviarClicDeIconos()
        {
            Guna2Button[] botones = { btnAbrirPedido, btnReservar, btnLiberarMesa };
            foreach (Control control in guna2Panel9.Controls)
            {
                PictureBox icono = control as PictureBox;
                if (icono == null)
                    continue;

                foreach (Guna2Button boton in botones)
                {
                    if (!boton.Bounds.IntersectsWith(icono.Bounds))
                        continue;

                    Guna2Button destino = boton;
                    icono.Cursor = Cursors.Hand;
                    icono.Click += (s, e) =>
                    {
                        if (destino.Enabled && destino.Visible)
                            destino.PerformClick();
                    };
                    break;
                }
            }
        }

        private void ConfigurarBotonesIniciales()
        {
            // No hay ninguna mesa seleccionada.
            mesaSeleccionada = 0;

            // Deshabilitar los tres botones.
            btnAbrirPedido.Enabled = false;
            btnReservar.Enabled = false;
            btnLiberarMesa.Enabled = false;

            // Color gris cuando están deshabilitados.
            Guna2Button[] botones =
            {
        btnAbrirPedido,
        btnReservar,
        btnLiberarMesa
    };

            foreach (Guna2Button boton in botones)
            {
                boton.FillColor = Color.Gray;

                boton.DisabledState.FillColor = Color.Gray;
                boton.DisabledState.ForeColor = Color.White;
            }

            // Información inicial del panel derecho.
            lblTituloDetalleMesa.Text = "Elija una mesa";

            lblDetalleNumero.Text = "-";
            lblDetalleCapacidad.Text = "-";
            lblDetalleEstado.Text = "-";
            lblDetallePedido.Text = "Ninguno";
            lblDetalleCliente.Text = "Ninguno";
            lblDetalleMesero.Text = "Ninguno";
            lblDetalleTiempo.Text = "Ninguno";

            textEstadoDetalleMesa.Text = "Sin seleccionar";
            textEstadoDetalleMesa.FillColor = Color.LightGray;
        }


        private void CargarHistorialMesas()
        {
            try
            {
                // =====================================
                // 1. CONSULTAR RESERVAS EN SQL SERVER
                // =====================================

                string sql = ReservaReglas.SqlAhora + @"
            SELECT
                R.id_reserva AS Numero,

                'Mesa ' +
                CONVERT(VARCHAR(10), M.[número])
                    AS Mesa,

                R.nombre_cliente AS Cliente,

                CONVERT(VARCHAR(10),
                    R.fecha_hora, 103) AS Fecha,

                CONVERT(VARCHAR(5),
                    R.fecha_hora, 108) AS Hora,

                CASE
                    WHEN R.estado = 'confirmada'
                        THEN 'Confirmada'
                    WHEN M.estado = 'ocupada'
                        THEN 'Llegó · esperando mesa'
                    ELSE 'Llegó · mesa apartada'
                END AS Estado,

                ISNULL(
                    NULLIF(R.observacion, ''),
                    'Sin observación'
                ) AS Observacion

            FROM dbo.RESERVA_MESA R

            INNER JOIN dbo.MESA M
                ON R.id_mesa = M.id_mesa

            WHERE M.id_sucursal = @Sucursal
              AND (
                    -- Sin llegada: dentro de la tolerancia de llegada (30 min).
                    (R.estado = 'confirmada'
                     AND DATEADD(MINUTE, @Tolerancia, R.fecha_hora) > @Ahora)
                    -- Con llegada registrada y aún sin pedido (sin vencimiento).
                    OR (" + ReservaReglas.SqlLlegadaPendiente + @")
                  )

            ORDER BY
                R.fecha_hora ASC,
                R.id_reserva ASC;";

                DataTable tabla = new DataTable();

                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                using (SqlCommand comando =
                    new SqlCommand(sql, conexion))
                {
                    comando.Parameters.Add(
                        "@Sucursal", SqlDbType.Int
                    ).Value = 1;
                    ReservaReglas.AgregarParametros(comando);

                    using (SqlDataAdapter adaptador =
                        new SqlDataAdapter(comando))
                    {
                        adaptador.Fill(tabla);
                    }
                }

                // =====================================
                // 2. PREPARAR TABLA
                // =====================================

                dgvHistorialMesas.DataSource = null;
                // Columnas, anchos y estilos se crean UNA sola vez.
                // Antes se borraban y recreaban en cada refresco
                // (al mostrarse Mesas y con cada aviso de reservas).
                if (!historialConfigurado)
                {
                    dgvHistorialMesas.AutoGenerateColumns = false;
                    dgvHistorialMesas.Columns.Clear();

                    dgvHistorialMesas.Columns.Add(
                        "colNumero", "N°");

                    dgvHistorialMesas.Columns.Add(
                        "colMesa", "MESA");

                    dgvHistorialMesas.Columns.Add(
                        "colCliente", "CLIENTE");

                    dgvHistorialMesas.Columns.Add(
                        "colFecha", "FECHA");

                    dgvHistorialMesas.Columns.Add(
                        "colHora", "HORA");

                    dgvHistorialMesas.Columns.Add(
                        "colEstado", "ESTADO");

                    dgvHistorialMesas.Columns.Add(
                        "colObservacion", "OBSERVACIÓN");

                    // =====================================
                    // 3. RELACIONAR COLUMNAS
                    // =====================================

                    dgvHistorialMesas.Columns[0]
                        .DataPropertyName = "Numero";

                    dgvHistorialMesas.Columns[1]
                        .DataPropertyName = "Mesa";

                    dgvHistorialMesas.Columns[2]
                        .DataPropertyName = "Cliente";

                    dgvHistorialMesas.Columns[3]
                        .DataPropertyName = "Fecha";

                    dgvHistorialMesas.Columns[4]
                        .DataPropertyName = "Hora";

                    dgvHistorialMesas.Columns[5]
                        .DataPropertyName = "Estado";

                    dgvHistorialMesas.Columns[6]
                        .DataPropertyName = "Observacion";

                    // =====================================
                    // 4. TAMAÑO DE COLUMNAS
                    // =====================================

                    dgvHistorialMesas.AutoSizeColumnsMode =
                        DataGridViewAutoSizeColumnsMode.Fill;

                    dgvHistorialMesas.Columns[0].FillWeight = 7;
                    dgvHistorialMesas.Columns[1].FillWeight = 12;
                    dgvHistorialMesas.Columns[2].FillWeight = 22;
                    dgvHistorialMesas.Columns[3].FillWeight = 15;
                    dgvHistorialMesas.Columns[4].FillWeight = 10;
                    dgvHistorialMesas.Columns[5].FillWeight = 15;
                    dgvHistorialMesas.Columns[6].FillWeight = 19;

                    dgvHistorialMesas.Columns[6]
                        .DefaultCellStyle.WrapMode =
                            DataGridViewTriState.True;

                    // =====================================
                    // 5. CONFIGURAR TABLA
                    // =====================================

                    dgvHistorialMesas.ReadOnly = true;

                    dgvHistorialMesas.AllowUserToAddRows = false;

                    dgvHistorialMesas.AllowUserToDeleteRows = false;

                    dgvHistorialMesas.RowHeadersVisible = false;

                    dgvHistorialMesas.SelectionMode =
                        DataGridViewSelectionMode.FullRowSelect;

                    dgvHistorialMesas.AutoSizeRowsMode =
                        DataGridViewAutoSizeRowsMode.AllCells;

                    dgvHistorialMesas.RowTemplate.Height = 35;

                    dgvHistorialMesas.BackgroundColor =
                        Color.White;

                    dgvHistorialMesas.BorderStyle =
                        BorderStyle.None;

                    // =====================================
                    // 6. COLORES DE LAS FILAS
                    // =====================================

                    dgvHistorialMesas.DefaultCellStyle.BackColor =
                        Color.White;

                    dgvHistorialMesas.DefaultCellStyle.ForeColor =
                        Color.Black;

                    dgvHistorialMesas.AlternatingRowsDefaultCellStyle
                        .BackColor = Color.WhiteSmoke;

                    dgvHistorialMesas.AlternatingRowsDefaultCellStyle
                        .ForeColor = Color.Black;

                    dgvHistorialMesas.DefaultCellStyle
                        .SelectionBackColor =
                            Color.FromArgb(255, 245, 170);

                    dgvHistorialMesas.DefaultCellStyle
                        .SelectionForeColor = Color.Black;

                    // =====================================
                    // 7. ENCABEZADO AMARILLO
                    // =====================================

                    dgvHistorialMesas.EnableHeadersVisualStyles =
                        false;

                    dgvHistorialMesas.ColumnHeadersDefaultCellStyle
                        .BackColor = Color.Gold;

                    dgvHistorialMesas.ColumnHeadersDefaultCellStyle
                        .ForeColor = Color.Black;

                    dgvHistorialMesas.ColumnHeadersDefaultCellStyle
                        .Font = new Font(
                            "Segoe UI", 9, FontStyle.Bold);

                    dgvHistorialMesas.ColumnHeadersHeight = 38;

                    historialConfigurado = true;
                }

                // =====================================
                // 8. MOSTRAR RESERVAS
                // =====================================

                dgvHistorialMesas.DataSource = tabla;

                dgvHistorialMesas.ClearSelection();

                // Conservar la reserva elegida tras un refresco automático;
                // si ya no está pendiente (cancelada o vencida), se olvida.
                if (reservaElegidaTabla.HasValue)
                {
                    bool sigue = false;
                    foreach (DataGridViewRow fila in dgvHistorialMesas.Rows)
                    {
                        DataRowView datos = fila.DataBoundItem as DataRowView;
                        if (datos != null && Convert.ToInt32(datos["Numero"]) == reservaElegidaTabla.Value)
                        {
                            fila.Selected = true;
                            sigue = true;
                            break;
                        }
                    }
                    if (sigue) MostrarBotonCancelarReserva();
                    else OlvidarReservaDeTabla();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error al cargar reservas:\n" +
                    ex.Message,
                    "Gestión de Mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private void btnAbrirPedido_Click(object sender, EventArgs e)
        {
            if (mesaSeleccionada <= 0)
            {
                MessageBox.Show("Seleccione una mesa.");
                return;
            }

            // Cliente que ya llegó (sin pedido aún): Pedidos con su reserva exacta.
            bool reservada = lblDetalleEstado.Text.Trim().ToLower() == "reservada";
            if (reservada && llegadaPendiente != null)
            {
                Navegador.AbrirPedidosConMesa(this, mesaSeleccionada, llegadaPendiente.IdReserva);
                return;
            }

            // Mesa reservada, o libre con una reserva de hoy: se muestran los
            // datos de la reserva para verificar al cliente antes de confirmar.
            int? reservaParaPedido = null;
            if (reservada || btnAbrirPedido.Text == "Confirmar llegada")
            {
                ControlReservasAutomaticas.ReservaVigente r;
                try { r = ControlReservasAutomaticas.ObtenerReservaDeHoy(mesaSeleccionada); }
                catch (Exception ex) { MessageBox.Show("No se pudo consultar la reserva:\n" + ex.Message); return; }

                if (r == null && reservada)
                {
                    MessageBox.Show("La Mesa " + mesaSeleccionada + " no tiene una reserva confirmada para hoy.",
                        "Confirmar llegada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefrescarMesasAutomaticamente(null, EventArgs.Empty);
                    return;
                }

                if (r != null)
                {
                    bool antes = r.FechaHora > ReservaReglas.AhoraLocal().AddMinutes(ReservaReglas.ApartadoPrevioMinutos);
                    DialogResult respuesta = MessageBox.Show(
                        "Verifique al cliente de la reserva:\n\n" +
                        "Reserva N.º " + r.IdReserva + "\n" +
                        "Mesa: " + r.NumeroMesa + "\n" +
                        "Cliente: " + r.Cliente + "\n" +
                        "Teléfono: " + (r.Telefono.Length == 0 ? "-" : r.Telefono) + "\n" +
                        "Hora reservada: " + r.FechaHora.ToString("HH:mm") + "\n" +
                        "Personas: " + r.Personas +
                        (r.Observacion.Length > 0 ? "\nObservación: " + r.Observacion : "") + "\n\n" +
                        (antes ? "Llega antes de su horario (confirmación anticipada).\n\n" : "") +
                        "Sí: es el cliente de la reserva (confirmar llegada y abrir su pedido).\n" +
                        (reservada ? "No: no es el cliente (la mesa sigue apartada)."
                                   : "No: es otro cliente (pedido normal, sujeto a la reserva próxima)."),
                        "Confirmar llegada",
                        reservada ? MessageBoxButtons.YesNo : MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);

                    if (respuesta == DialogResult.Cancel || (reservada && respuesta != DialogResult.Yes))
                        return;

                    if (respuesta == DialogResult.Yes)
                    {
                        try
                        {
                            if (ControlReservasAutomaticas.ConfirmarLlegada(r.IdReserva))
                            {
                                ActualizarTodo();
                                MessageBox.Show("La mesa se ocupó: el cliente queda «esperando mesa».",
                                    "Confirmar llegada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }
                            reservaParaPedido = r.IdReserva;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Confirmar llegada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            RefrescarMesasAutomaticamente(null, EventArgs.Empty);
                            return;
                        }
                    }
                }
            }

            // Abrir pedidos con la mesa seleccionada (y la reserva exacta si
            // se confirmó la llegada). El Navegador descarta un Pedidos anterior
            // (avisando si tiene productos sin guardar) y crea uno para esta mesa.
            Navegador.AbrirPedidosConMesa(this, mesaSeleccionada, reservaParaPedido);
        }

        // «Reservar» del panel de la mesa: abre la MISMA ventana que «Nueva
        // reserva», con la mesa seleccionada ya elegida. La validación y el
        // guardado son los de RESERVAR_MESA (no hay un segundo sistema).
        private void btnReservar_Click(object sender, EventArgs e)
        {
            if (mesaSeleccionada <= 0)
            {
                MessageBox.Show(
                    "Primero seleccione una mesa.",
                    "Gestión de Mesas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Mesa ocupada con reserva en conflicto (texto del botón).
            string accion = btnReservar.Text.Trim();
            if (conflictoMesa != null && accion == "Confirmar llegada")
            {
                RegistrarLlegadaConMesaOcupada(conflictoMesa);
                return;
            }
            if (conflictoMesa != null && accion == "Reasignar mesa")
            {
                ReasignarReservaAMesaLibre(conflictoMesa.IdReserva);
                return;
            }

            AbrirNuevaReserva(mesaSeleccionada);
        }

        // El cliente de la reserva llegó y la mesa sigue ocupada: se registra
        // «Cliente llegó, esperando mesa» sin tocar el pedido activo.
        private void RegistrarLlegadaConMesaOcupada(ControlReservasAutomaticas.ReservaVigente r)
        {
            if (MessageBox.Show(
                    "Verifique al cliente de la reserva:\n\n" +
                    "Reserva N.º " + r.IdReserva + "\n" +
                    "Cliente: " + r.Cliente + "\n" +
                    "Teléfono: " + (r.Telefono.Length == 0 ? "-" : r.Telefono) + "\n" +
                    "Hora reservada: " + r.FechaHora.ToString("HH:mm") + " · Personas: " + r.Personas + "\n\n" +
                    "La Mesa " + r.NumeroMesa + " sigue ocupada por el cliente anterior: su pedido no cambia.\n" +
                    "Se registrará «Cliente llegó, esperando mesa» (la espera no vence a los 30 minutos).\n\n" +
                    "¿Registrar la llegada?",
                    "Confirmar llegada", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                ControlReservasAutomaticas.ConfirmarLlegada(r.IdReserva);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Confirmar llegada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ActualizarTodo();
                return;
            }
            ActualizarTodo();
            if (MessageBox.Show(
                    "Llegada registrada: el cliente espera mesa.\n\n" +
                    "¿Desea trasladarlo ahora a otra mesa libre?",
                    "Cliente esperando mesa", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                ReasignarReservaAMesaLibre(r.IdReserva);
        }

        // Traslada la reserva a una mesa libre elegida (validada de nuevo en la
        // transacción de ReasignarReserva). Sin mesas, el cliente sigue en espera.
        private void ReasignarReservaAMesaLibre(int idReserva)
        {
            List<int> mesas;
            ControlReservasAutomaticas.ReservaVigente r;
            try
            {
                r = ControlReservasAutomaticas.ObtenerReservaVigentePorId(idReserva);
                mesas = r == null ? new List<int>() : ControlReservasAutomaticas.MesasParaReasignar(idReserva);
            }
            catch (Exception ex)
            {
                Registro.Error("Mesas", ex);
                MessageBox.Show("No se pudieron consultar las mesas:\n" + ex.Message, "Reasignar mesa",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (r == null)
            {
                MessageBox.Show("La reserva N.º " + idReserva + " ya no está vigente.", "Reasignar mesa",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                ActualizarTodo();
                return;
            }
            if (mesas.Count == 0)
            {
                MessageBox.Show(
                    "No hay mesas libres con capacidad para " + r.Personas + " personas y sin otra reserva cercana.\n\n" +
                    "El cliente de la reserva N.º " + r.IdReserva + " (" + r.Cliente + ") sigue esperando la Mesa " + r.NumeroMesa + ".\n" +
                    "No se abre un pedido sobre la mesa ocupada.",
                    "Sin mesas disponibles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int? destino = ElegirMesaDestino(r, mesas);
            if (!destino.HasValue)
                return;
            try
            {
                ControlReservasAutomaticas.ReasignarReserva(idReserva, destino.Value);
            }
            catch (Exception ex)
            {
                ActualizarTodo();
                MessageBox.Show("No se pudo reasignar la reserva:\n" + ex.Message + "\n\nEl cliente sigue en espera.",
                    "Reasignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ActualizarTodo();
            if (r.Llego)
            {
                if (MessageBox.Show(
                        "Reserva N.º " + r.IdReserva + " trasladada a la Mesa " + destino.Value + ", reservada para " + r.Cliente + ".\n\n" +
                        "¿Abrir su pedido ahora?",
                        "Reasignar mesa", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Navegador.AbrirPedidosConMesa(this, destino.Value, r.IdReserva);
            }
            else
            {
                MessageBox.Show("Reserva N.º " + r.IdReserva + " trasladada a la Mesa " + destino.Value + ".",
                    "Reasignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Lista sencilla de mesas destino (ventana en código, sin diseño nuevo).
        private int? ElegirMesaDestino(ControlReservasAutomaticas.ReservaVigente r, List<int> mesas)
        {
            using (Form f = new Form())
            {
                f.Text = "Reasignar reserva N.º " + r.IdReserva;
                f.ClientSize = new Size(360, 170);
                f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Color.White;
                f.ShowInTaskbar = false;
                Label texto = new Label
                {
                    Text = r.Cliente + " · " + r.Personas + " personas\nMesa libre de destino:",
                    Bounds = new Rectangle(16, 12, 330, 40), Font = new Font("Segoe UI", 10F)
                };
                ComboBox lista = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(16, 60, 328, 28),
                    Font = new Font("Segoe UI", 10F)
                };
                foreach (int m in mesas) lista.Items.Add("Mesa " + m);
                lista.SelectedIndex = 0;
                Guna2Button aceptar = new Guna2Button { Text = "Reasignar", BorderRadius = 10, FillColor = Color.Gold, ForeColor = Color.Black, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Bounds = new Rectangle(190, 112, 154, 40) };
                Guna2Button cancelar = new Guna2Button { Text = "Cancelar", BorderRadius = 10, FillColor = Color.Gainsboro, ForeColor = Color.Black, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Bounds = new Rectangle(16, 112, 154, 40) };
                aceptar.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                cancelar.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                f.Controls.AddRange(new Control[] { texto, lista, aceptar, cancelar });
                if (f.ShowDialog(this) != DialogResult.OK)
                    return null;
                return mesas[lista.SelectedIndex];
            }
        }

        // true si el pedido quedó cobrado y su mesa ya está libre.
        private static bool MesaLiberadaPorCobro(
            SqlConnection conexion, SqlTransaction transaccion, int idPedido)
        {
            using (SqlCommand cmd = new SqlCommand(@"
                SELECT COUNT(*)
                FROM dbo.PEDIDO P
                INNER JOIN dbo.MESA M ON M.id_mesa = P.id_mesa
                WHERE P.id_pedido = @Pedido
                  AND P.estado = 'cobrado'
                  AND M.estado IN ('libre', 'reservada');", conexion, transaccion))
            {
                cmd.Parameters.Add("@Pedido", SqlDbType.Int).Value = idPedido;
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private void CobrarYLiberarMesaOcupada()
        {
            try
            {
                int idPedido = 0;

                // 1. Buscar el pedido activo de la mesa.
                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                {
                    conexion.Open();

                    string sql = @"
                SELECT TOP (2) P.id_pedido
                FROM dbo.PEDIDO P
                INNER JOIN dbo.MESA M
                    ON P.id_mesa = M.id_mesa
                WHERE M.[número] = @Mesa
                  AND M.id_sucursal = 1
                  AND M.estado = 'ocupada'
                  AND P.canal = 'salon'
                  AND P.estado IN
                  (
                      'pendiente',
                      'en_preparacion',
                      'listo',
                      'entregado'
                  )
                ORDER BY P.fecha_hora DESC;";

                    using (SqlCommand cmd =
                        new SqlCommand(sql, conexion))
                    {
                        cmd.Parameters.Add(
                            "@Mesa", SqlDbType.Int).Value =
                            mesaSeleccionada;

                        using (SqlDataReader lector =
                            cmd.ExecuteReader())
                        {
                            if (!lector.Read())
                            {
                                MessageBox.Show(
                                    "No se encontró un pedido activo. " +
                                    "Revise el estado de la mesa.");
                                return;
                            }

                            idPedido = Convert.ToInt32(
                                lector["id_pedido"]);

                            if (lector.Read())
                            {
                                MessageBox.Show(
                                    "La mesa tiene más de un pedido activo. " +
                                    "Revise los pedidos antes de cobrar.");
                                return;
                            }
                        }
                    }
                }

                // 2. Solicitar confirmación.
                DialogResult respuesta = MessageBox.Show(
                    "Mesa " + mesaSeleccionada +
                    "\nPedido #" + idPedido.ToString("D4") +
                    "\n\n¿Desea abrir el cobro para liberar la mesa?",
                    "Cobrar pedido",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (respuesta != DialogResult.Yes)
                    return;

                // 3. Abrir el formulario de cobro existente.
                using (Generar_Boleta boleta =
                    new Generar_Boleta())
                {
                    boleta.Tag = idPedido;
                    boleta.ShowDialog(this);
                }

                // 4. Liberar solamente si el sistema
                // registró el cobro según el flujo actual.
                using (SqlConnection conexion =
                    new SqlConnection(cadenaConexion))
                {
                    conexion.Open();

                    using (SqlTransaction transaccion =
                        conexion.BeginTransaction(
                            IsolationLevel.Serializable))
                    {
                        string sql = ReservaReglas.SqlAhora + @"
                    UPDATE M
                    SET estado = " + ReservaReglas.SqlEstadoAlLiberar + @"
                    FROM dbo.MESA M
                    WHERE M.[número] = @Mesa
                      AND M.id_sucursal = 1
                      AND M.estado = 'ocupada'

                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.PEDIDO P
                          WHERE P.id_pedido = @Pedido
                            AND P.id_mesa = M.id_mesa
                            AND P.canal = 'salon'
                            AND P.estado = 'cobrado'
                            -- El cobro se registra en PAGO (antes solo
                            -- se aceptaba VENTA_INTERNA, que nunca se
                            -- escribe, y la mesa no se liberaba jamás).
                            AND
                            (
                                EXISTS (SELECT 1 FROM dbo.PAGO PG
                                        WHERE PG.id_pedido = P.id_pedido)
                                OR EXISTS (SELECT 1 FROM dbo.VENTA_INTERNA V
                                           WHERE V.id_pedido = P.id_pedido)
                            )
                      )

                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.PEDIDO P
                          WHERE P.id_mesa = M.id_mesa
                            AND P.canal = 'salon'
                            AND P.estado IN
                            (
                                'pendiente',
                                'en_preparacion',
                                'listo',
                                'entregado'
                            )
                      );";

                        using (SqlCommand cmd =
                            new SqlCommand(
                                sql, conexion, transaccion))
                        {
                            cmd.Parameters.Add(
                                "@Mesa", SqlDbType.Int).Value =
                                mesaSeleccionada;

                            cmd.Parameters.Add(
                                "@Pedido", SqlDbType.Int).Value =
                                idPedido;
                            ReservaReglas.AgregarParametros(cmd);

                            int filas = cmd.ExecuteNonQuery();

                            // El cobro ya libera la mesa en su propia
                            // transacción: si quedó libre, todo correcto.
                            if (filas != 1 && MesaLiberadaPorCobro(conexion, transaccion, idPedido))
                                filas = 1;

                            if (filas != 1)
                            {
                                transaccion.Rollback();

                                MessageBox.Show(
                                    "La mesa continúa ocupada.\n\n" +
                                    "El cobro no quedó registrado " +
                                    "o existe otro pedido activo.",
                                    "Cobro pendiente",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);

                                return;
                            }
                        }

                        transaccion.Commit();
                    }
                }

                // 5. Actualizar la interfaz.
                CargarContadoresMesas();
                CargarEstadosMesas();
                CargarDetalleMesa();
                FiltrarMesas();

                MessageBox.Show(
                    "Pedido #" + idPedido.ToString("D4") +
                    " cobrado según el registro actual.\n\n" +
                    "Mesa " + mesaSeleccionada +
                    " liberada correctamente.",
                    "Mesa disponible",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "No se pudo completar la operación:\n" +
                    ex.Message,
                    "Liberar mesa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        private void ButtonINICIO_Click(object sender, EventArgs e)
        {
            Navegador.Ir<FRM_INICIO>(this);
        }
    }
}







