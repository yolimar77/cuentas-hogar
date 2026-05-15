using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class PrevisionService(LocalDbService db)
{
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();

        var movMes = movimientos.Where(m =>
            m.Fecha.Month == mes && m.Fecha.Year == anyo && m.RecurrenteId is null);

        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        return new PrevisionMensual
        {
            Mes = mes,
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
        };
    }

    public async Task GenerarMovimientosRecurrentesAsync()
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy = DateTime.Today;

        foreach (var rec in recurrentes.Where(r => r.Activo))
        {
            var fecha = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
            var limite = new DateTime(Math.Min(hoy.Ticks, rec.FechaFin.Ticks));

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
