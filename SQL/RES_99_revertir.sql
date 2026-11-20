/* ==========================================================
   RESERVAS - REVERSIÓN DE llegada_registrada
   Por seguridad NO se ejecuta si hay clientes con llegada registrada
   que todavía no tienen pedido (perderían su lugar en espera).
   ========================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF COL_LENGTH('dbo.RESERVA_MESA', 'llegada_registrada') IS NULL
BEGIN
    PRINT 'La columna llegada_registrada no existe: nada que revertir.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.RESERVA_MESA R
           WHERE R.llegada_registrada IS NOT NULL AND R.estado = 'finalizada'
             AND NOT EXISTS (SELECT 1 FROM dbo.PEDIDO P WHERE P.id_reserva = R.id_reserva))
BEGIN
    RAISERROR('Hay clientes con llegada registrada esperando su pedido o su mesa: reversión cancelada.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;
ALTER TABLE dbo.RESERVA_MESA DROP COLUMN llegada_registrada;
COMMIT TRANSACTION;
GO
