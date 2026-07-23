using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class RecurrenteAvisoService(LocalDbService db)
{
    public async Task<List<RecurrenteVencimiento>> ObtenerPorVencerAsync()
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy = DateTime.Today;

        var resultado = new List<RecurrenteVencimiento>();
        foreach (var rec in recurrentes.Where(r => r.Activo && r.FechaFin.HasValue))
        {
            var diasRestantes = (rec.FechaFin!.Value.Date - hoy).Days;
            if (diasRestantes == 30 || (diasRestantes >= 0 && diasRestantes <= 3))
                resultado.Add(new RecurrenteVencimiento(rec.Concepto, diasRestantes));
        }

        return resultado.OrderBy(r => r.DiasRestantes).ToList();
    }
}
