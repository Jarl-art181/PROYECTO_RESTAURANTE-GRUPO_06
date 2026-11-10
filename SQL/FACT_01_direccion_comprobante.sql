/* ==========================================================
   FACTURA ACADÉMICA - DIRECCIÓN FISCAL EN EL COMPROBANTE
   Aprobado por el propietario el 2026-11-09.

   * Agrega dbo.COMPROBANTE.direccion_cliente (VARCHAR(255) NULL):
     la dirección fiscal que figura en una factura, para que su
     reimpresión conserve los datos originales.
   * No modifica ni borra ninguna fila: los comprobantes existentes
     quedan con la columna en NULL.
   * Idempotente: se puede ejecutar más de una vez.
   * Reversión: FACT_99_revertir.sql

   Ejecutar (primero en AL_ESTILO_PIURA_PRUEBAS):
     sqlcmd -S LAPTOP-GH63852S\SQLEXPRESS -E -C -I -f 65001 -d <base> -i FACT_01_direccion_comprobante.sql
   ========================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.COMPROBANTE', 'direccion_cliente') IS NULL
    ALTER TABLE dbo.COMPROBANTE ADD direccion_cliente VARCHAR(255) NULL;

COMMIT TRANSACTION;
GO
