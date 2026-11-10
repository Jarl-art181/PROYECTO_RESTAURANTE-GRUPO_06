/* ==========================================================
   FACTURA ACADÉMICA - REVERSIÓN
   Quita dbo.COMPROBANTE.direccion_cliente.
   Por seguridad NO se ejecuta si alguna factura ya guardó una
   dirección fiscal (se perdería en sus reimpresiones). En ese caso,
   haga un respaldo y decida antes de continuar.
   ========================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF COL_LENGTH('dbo.COMPROBANTE', 'direccion_cliente') IS NULL
BEGIN
    PRINT 'La columna direccion_cliente no existe: nada que revertir.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.COMPROBANTE WHERE direccion_cliente IS NOT NULL)
BEGIN
    RAISERROR('Hay facturas con dirección fiscal guardada: reversión cancelada.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;
ALTER TABLE dbo.COMPROBANTE DROP COLUMN direccion_cliente;
COMMIT TRANSACTION;
GO
