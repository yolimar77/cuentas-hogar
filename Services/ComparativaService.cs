using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class ComparativaService(LocalDbService db)
{
    private const decimal PorcentajeMinimo = 25m;
    private const decimal ImporteMinimo = 20m;

    public async Task<List<CategoriaDestacada>> ObtenerDestacadosAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var categorias = await db.ObtenerCategoriasAsync();
        var catById = categorias.ToDictionary(c => c.Id);

        var inicioMesVisto = new DateTime(anyo, mes, 1);

        if (movimientos.Count == 0) return [];

        var primerMovimiento = movimientos.Min(m => m.Fecha);
        var inicioPrimerMes = new DateTime(primerMovimiento.Year, primerMovimiento.Month, 1);

        var mesesDisponibles = 0;
        for (int i = 1; i <= 6; i++)
        {
            var inicioMesAnterior = inicioMesVisto.AddMonths(-i);
            if (inicioMesAnterior < inicioPrimerMes) break;
            mesesDisponibles++;
        }

        var gastosMesActual = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha.Month == mes && m.Fecha.Year == anyo)
            .ToList();

        var inicioVentana = inicioMesVisto.AddMonths(-mesesDisponibles);
        var gastosAnteriores = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= inicioVentana && m.Fecha < inicioMesVisto)
            .ToList();

        // Agrupar por nombre resuelto para que IDs duplicados con el mismo nombre
        // (herencia de la inicialización multi-dispositivo anterior) queden en un único grupo
        string NombreDe(Movimiento m) => catById.TryGetValue(m.CategoriaId, out var c) ? c.Nombre : "Sin categoría";

        var nombresCategorias = gastosMesActual.Select(NombreDe)
            .Union(gastosAnteriores.Select(NombreDe))
            .Distinct();

        var resultado = new List<CategoriaDestacada>();

        foreach (var nombre in nombresCategorias)
        {
            var movimientosActual = gastosMesActual.Where(m => NombreDe(m) == nombre).ToList();
            var movimientosAnterior = gastosAnteriores.Where(m => NombreDe(m) == nombre).ToList();

            var icono = movimientosActual.Concat(movimientosAnterior)
                .Select(m => catById.TryGetValue(m.CategoriaId, out var c) ? c.Icono : null)
                .FirstOrDefault(i => i != null) ?? "•";

            var totalActual = movimientosActual.Sum(m => m.Importe);
            var totalAnterior = movimientosAnterior.Sum(m => m.Importe);
            var mediaAnterior = mesesDisponibles > 0 ? totalAnterior / mesesDisponibles : 0m;

            var diferenciaImporte = totalActual - mediaAnterior;
            decimal? diferenciaPorcentaje = mediaAnterior > 0
                ? diferenciaImporte / mediaAnterior * 100
                : null;

            var incluir = mediaAnterior > 0
                ? Math.Abs(diferenciaPorcentaje!.Value) >= PorcentajeMinimo && Math.Abs(diferenciaImporte) >= ImporteMinimo
                : totalActual >= ImporteMinimo;

            if (!incluir) continue;

            resultado.Add(new CategoriaDestacada(
                nombre, icono, totalActual, mediaAnterior, diferenciaImporte, diferenciaPorcentaje));
        }

        return resultado.OrderByDescending(c => Math.Abs(c.DiferenciaImporte)).ToList();
    }
}
