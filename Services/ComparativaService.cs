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
        if (mesesDisponibles == 0) return [];

        var gastosMesActual = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha.Month == mes && m.Fecha.Year == anyo)
            .ToList();

        var inicioVentana = inicioMesVisto.AddMonths(-mesesDisponibles);
        var gastosAnteriores = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= inicioVentana && m.Fecha < inicioMesVisto)
            .ToList();

        var categoriaIds = gastosMesActual.Select(m => m.CategoriaId)
            .Union(gastosAnteriores.Select(m => m.CategoriaId))
            .Distinct();

        var resultado = new List<CategoriaDestacada>();

        foreach (var categoriaId in categoriaIds)
        {
            catById.TryGetValue(categoriaId, out var categoria);
            var nombre = categoria?.Nombre ?? "Sin categoría";
            var icono = categoria?.Icono ?? "•";

            var totalActual = gastosMesActual.Where(m => m.CategoriaId == categoriaId).Sum(m => m.Importe);
            var totalAnterior = gastosAnteriores.Where(m => m.CategoriaId == categoriaId).Sum(m => m.Importe);
            var mediaAnterior = totalAnterior / mesesDisponibles;

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
