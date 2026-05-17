using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class PrevisionService(LocalDbService db)
{
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();

        var inicioMes     = new DateTime(anyo, mes, 1);
        var movMes        = movimientos.Where(m => m.Fecha.Month == mes && m.Fecha.Year == anyo);
        var movAnteriores = movimientos.Where(m => m.Fecha < inicioMes);
        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        return new PrevisionMensual
        {
            Mes  = mes,
            Anyo = anyo,
            IngresosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Ingreso)
                .Sum(r => r.Importe),
            GastosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Gasto)
                .Sum(r => r.Importe),
            IngresosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Ingreso)
                .Sum(m => m.Importe),
            GastosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto)
                .Sum(m => m.Importe),
            SaldoAcumulado =
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe) -
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe),
        };
    }

    public async Task GenerarMovimientosRecurrentesAsync(int? mesFin = null, int? anyoFin = null)
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy = DateTime.Today;
        // Por defecto genera hasta 2 meses en el futuro; si se indica mes/año destino, llega hasta ahí
        var finHorizonte = (mesFin.HasValue && anyoFin.HasValue)
            ? new DateTime(anyoFin.Value, mesFin.Value, DateTime.DaysInMonth(anyoFin.Value, mesFin.Value))
            : new DateTime(hoy.Year, hoy.Month, 1).AddMonths(2).AddDays(-1);

        foreach (var rec in recurrentes.Where(r => r.Activo))
        {
            var fecha = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
            var limite = new DateTime(Math.Min(finHorizonte.Ticks, rec.FechaFin.Ticks));

            while (fecha <= limite)
            {
                var periodo = fecha.ToString("yyyy-MM");
                if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                {
                    var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                    await db.GuardarMovimientoAsync(new Movimiento
                    {
                        Concepto = rec.Concepto,
                        Importe = rec.Importe,
                        Tipo = rec.Tipo,
                        Fecha = new DateTime(fecha.Year, fecha.Month, dia),
                        CategoriaId = rec.CategoriaId,
                        CuentaId = rec.CuentaId,
                        RecurrenteId = rec.Id,
                        Periodo = periodo
                    });
                }
                fecha = fecha.AddMonths(1);
            }
        }
    }
}
