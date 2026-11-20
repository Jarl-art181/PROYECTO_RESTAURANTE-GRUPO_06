/* ==========================================================
   RESERVAS - LLEGADA REGISTRADA (cliente en espera de mesa)
   Agrega dbo.RESERVA_MESA.llegada_registrada (DATETIME2 NULL): fecha y
   hora en que el personal confirmó la llegada del cliente.

   Por qué: el estado 'finalizada' por sí solo no distingue una llegada
   que todavía espera su pedido (o su mesa) de una reserva antigua
   finalizada sin pedido. Con esta columna:
     llegada pendiente / en espera = estado 'finalizada'
                                     + llegada_registrada NO nula
                                     + sin pedido vinculado
   y la espera no vence sola: se resuelve al abrir su pedido, al
   reasignarla a otra mesa o al cancelarla explícitamente.

   * No modifica ni borra filas: las reservas existentes quedan con la
     columna en NULL (no cuentan como clientes en espera).
   * Idempotente. Reversión: RES_99_revertir.sql

   Ejecutar (primero en AL_ESTILO_PIURA_PRUEBAS):
     sqlcmd -S LAPTOP-GH63852S\SQLEXPRESS -E -C -I -f 65001 -d <base> -i RES_01_llegada_registrada.sql
   ========================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.RESERVA_MESA', 'llegada_registrada') IS NULL
    ALTER TABLE dbo.RESERVA_MESA ADD llegada_registrada DATETIME2 NULL;

COMMIT TRANSACTION;
GO
