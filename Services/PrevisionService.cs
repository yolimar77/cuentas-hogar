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
        var hasta      = rec.FechaFin is null || ultimoDia < rec.FechaFin.Value ? ultimoDia : rec.FechaFin.Value;
        if (desde > hasta) return 0;
        var cursor = desde;
        while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
        int n = 0;
        while (cursor <= hasta) { n++; cursor = cursor.AddDays(7); }
        return n;
    }

    public async Task GenerarMovimientosRecurrentesAsync(int? mesFin = null, int? anyoFin = null)
    {
        using var _ = await db.BloqueoAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();

        // Leer los movimientos una sola vez y comprobar "¿ya existe este periodo?" en memoria, en
        // vez de una llamada a ExisteMovimientoRecurrenteAsync/GuardarMovimientoAsync por cada mes
        // de cada recurrente. Además de más rápido, evita que GuardarMovimientoAsync vuelva a pedir
        // este mismo bloqueo desde dentro — eso era lo que dejaba la app colgada para siempre: la
        // primera vez que hacía falta crear un movimiento nuevo (no solo comprobar que ya existía),
        // la petición anidada del bloqueo se quedaba esperando a que se soltara uno que ella misma
        // tenía cogido.
        var movimientos = await db.ObtenerMovimientosAsync();
        var existentes = movimientos
            .Where(m => m.RecurrenteId != null)
            .Select(m => (m.RecurrenteId!, m.Periodo))
            .ToHashSet();
        var nuevos = new List<Movimiento>();

        var hoy = DateTime.Today;
        var finHorizonte = (mesFin.HasValue && anyoFin.HasValue)
            ? new DateTime(anyoFin.Value, mesFin.Value, DateTime.DaysInMonth(anyoFin.Value, mesFin.Value))
            : new DateTime(hoy.Year, hoy.Month, 1).AddMonths(2).AddDays(-1);

        // Límite inferior del horizonte a comprobar: el mes pedido (o el actual si no se
        // especifica). Sin esto, cada llamada recorría TODOS los meses desde que se creó cada
        // recurrente para volver a preguntar "¿ya existe?" de meses que llevan generados desde
        // hace tiempo — trabajo que crece sin límite con la antigüedad de la cuenta y que se repite
        // en cada arranque de la app. Los meses que el usuario se salte (no abrir la app un tiempo)
        // se rellenan solos en el momento en que Dashboard/Movimientos los consulten, porque ambos
        // llaman a este método acotado al mes que están mostrando.
        var inicioHorizonte = (mesFin.HasValue && anyoFin.HasValue)
            ? new DateTime(anyoFin.Value, mesFin.Value, 1)
            : new DateTime(hoy.Year, hoy.Month, 1);

        foreach (var rec in recurrentes.Where(r => r.Activo))
        {
            var limite = rec.FechaFin is null || finHorizonte <= rec.FechaFin.Value
                ? finHorizonte
                : rec.FechaFin.Value;

            if (rec.Frecuencia == Models.Frecuencia.Semanal)
            {
                var cursor = rec.FechaInicio > inicioHorizonte ? rec.FechaInicio : inicioHorizonte;
                while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
                while (cursor <= limite)
                {
                    var periodo = cursor.ToString("yyyy-MM-dd");
                    if (existentes.Add((rec.Id, periodo)))
                    {
                        nuevos.Add(new Movimiento
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
            else if (rec.Frecuencia == Models.Frecuencia.Anual)
            {
                var anyo = Math.Max(rec.FechaInicio.Year, inicioHorizonte.Year);
                if (new DateTime(anyo, rec.FechaInicio.Month, 1) < inicioHorizonte) anyo++;
                while (new DateTime(anyo, rec.FechaInicio.Month, 1) <= limite)
                {
                    var periodo = $"{anyo:0000}-{rec.FechaInicio.Month:00}";
                    if (existentes.Add((rec.Id, periodo)))
                    {
                        var dia = Math.Min(rec.FechaInicio.Day, DateTime.DaysInMonth(anyo, rec.FechaInicio.Month));
                        nuevos.Add(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = new DateTime(anyo, rec.FechaInicio.Month, dia),
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    anyo++;
                }
            }
            else
            {
                var inicioRec = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
                var fecha = inicioRec > inicioHorizonte ? inicioRec : inicioHorizonte;
                while (fecha <= limite)
                {
                    var periodo = fecha.ToString("yyyy-MM");
                    if (existentes.Add((rec.Id, periodo)))
                    {
                        var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                        nuevos.Add(new Movimiento
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

        if (nuevos.Count > 0)
        {
            movimientos.AddRange(nuevos);
            await db.ReemplazarMovimientosAsync(movimientos);
        }
    }

    // Se llama al consultar un mes (actual o futuro) para que los movimientos generados
    // reflejen la configuración vigente del recurrente, incluso si ese cambio llegó por
    // sync desde otro dispositivo (MergeRecurrentesAsync no toca movimientos ya generados).
    // Los meses ya pasados nunca se tocan.
    public async Task ReconciliarMesAsync(int mes, int anyo)
    {
        var hoy = DateTime.Today;
        if (new DateTime(anyo, mes, 1) < new DateTime(hoy.Year, hoy.Month, 1))
            return;

        using var _ = await db.BloqueoAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var recById = recurrentes.ToDictionary(r => r.Id);
        var todos = await db.ObtenerMovimientosAsync();

        var obsoletos = todos
            .Where(m => m.RecurrenteId != null && m.Fecha.Month == mes && m.Fecha.Year == anyo)
            .Where(m => EsObsoleto(m, recById.GetValueOrDefault(m.RecurrenteId!), mes, anyo))
            .Select(m => m.Id)
            .ToHashSet();

        if (obsoletos.Count > 0)
            await db.ReemplazarMovimientosAsync(todos.Where(m => !obsoletos.Contains(m.Id)).ToList());
    }

    // Un movimiento generado deja de ser fiel a su recurrente si este ya no existe, ya no
    // está activo ese mes (p.ej. FechaFin se acortó vía sync), o si Importe/Categoría/Cuenta/
    // Tipo/Concepto/día difieren de la configuración vigente (cambio llegado por sync, sin
    // pasar por la limpieza que ya hace Recurrentes.razor al editar localmente).
    private static bool EsObsoleto(Movimiento m, MovimientoRecurrente? rec, int mes, int anyo)
    {
        if (rec is null || !rec.EstaActivoEnMes(mes, anyo))
            return true;

        if (m.Importe != rec.Importe || m.CategoriaId != rec.CategoriaId ||
            m.CuentaId != rec.CuentaId || m.Tipo != rec.Tipo || m.Concepto != rec.Concepto)
            return true;

        if (rec.Frecuencia == Models.Frecuencia.Mensual)
        {
            var diaEsperado = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(anyo, mes));
            if (m.Fecha.Day != diaEsperado) return true;
        }

        return false;
    }
}
