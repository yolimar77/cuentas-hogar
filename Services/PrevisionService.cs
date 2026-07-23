using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class PrevisionService(LocalDbService db)
{
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var categorias  = await db.ObtenerCategoriasAsync();

        var inicioMes     = new DateTime(anyo, mes, 1);
        var movMes        = movimientos.Where(m => m.Fecha.Month == mes && m.Fecha.Year == anyo);
        var movAnteriores = movimientos.Where(m => m.Fecha < inicioMes);
        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        // Agrupar por nombre resuelto para que categorías duplicadas (mismo nombre,
        // distinto Id, herencia de creación en dos dispositivos antes de sincronizar)
        // compartan un único mínimo y una única suma de gasto real.
        var detalleMinimos = new List<MinimoCategoria>();
        foreach (var grupo in categorias.Where(c => c.Tipo == TipoMovimiento.Gasto).GroupBy(c => c.Nombre))
        {
            var minimo = grupo.Select(c => c.MinimoMensual).FirstOrDefault(m => m is > 0);
            if (minimo is null) continue;

            var idsDelGrupo = grupo.Select(c => c.Id).ToHashSet();
            var gastoReal = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto && idsDelGrupo.Contains(m.CategoriaId))
                .Sum(m => m.Importe);

            var icono = grupo.Select(c => c.Icono).FirstOrDefault() ?? "•";
            detalleMinimos.Add(new MinimoCategoria(grupo.Key, icono, gastoReal, minimo.Value));
        }

        return new PrevisionMensual
        {
            Mes  = mes,
            Anyo = anyo,
            IngresosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Ingreso)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            GastosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Gasto)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            IngresosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Ingreso)
                .Sum(m => m.Importe),
            GastosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto)
                .Sum(m => m.Importe),
            DetalleMinimos = detalleMinimos,
            SaldoAcumulado =
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe) -
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe),
        };
    }

    private static int OcurrenciasEnMes(MovimientoRecurrente rec, int mes, int anyo)
    {
        if (rec.Frecuencia != Models.Frecuencia.Semanal) return 1;
        var primerDia  = new DateTime(anyo, mes, 1);
        var ultimoDia  = new DateTime(anyo, mes, DateTime.DaysInMonth(anyo, mes));
        var desde      = primerDia > rec.FechaInicio ? primerDia : rec.FechaInicio;
        var hasta      = ultimoDia < rec.FechaFin    ? ultimoDia : rec.FechaFin;
        if (desde > hasta) return 0;
        var cursor = desde;
        while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
        int n = 0;
        while (cursor <= hasta) { n++; cursor = cursor.AddDays(7); }
        return n;
    }

    public async Task GenerarMovimientosRecurrentesAsync(int? mesFin = null, int? anyoFin = null)
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy = DateTime.Today;
        var finHorizonte = (mesFin.HasValue && anyoFin.HasValue)
            ? new DateTime(anyoFin.Value, mesFin.Value, DateTime.DaysInMonth(anyoFin.Value, mesFin.Value))
            : new DateTime(hoy.Year, hoy.Month, 1).AddMonths(2).AddDays(-1);

        foreach (var rec in recurrentes.Where(r => r.Activo))
        {
            var limite = new DateTime(Math.Min(finHorizonte.Ticks, rec.FechaFin.Ticks));

            if (rec.Frecuencia == Models.Frecuencia.Semanal)
            {
                var cursor = rec.FechaInicio;
                while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
                while (cursor <= limite)
                {
                    var periodo = cursor.ToString("yyyy-MM-dd");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = cursor,
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    cursor = cursor.AddDays(7);
                }
            }
            else
            {
                var fecha = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
                while (fecha <= limite)
                {
                    var periodo = fecha.ToString("yyyy-MM");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = new DateTime(fecha.Year, fecha.Month, dia),
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    fecha = fecha.AddMonths(1);
                }
            }
        }
    }
}
