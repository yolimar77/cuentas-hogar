namespace HomeAccounts.Models;

public class PrevisionMensual
{
    public int Mes { get; set; }
    public int Anyo { get; set; }

    // Plan mensual basado en recurrentes configurados
    public decimal IngresosRecurrentes { get; set; }
    public decimal GastosRecurrentes { get; set; }

    // Lo que realmente ha ocurrido este mes (todos los movimientos)
    public decimal IngresosReales { get; set; }
    public decimal GastosReales { get; set; }

    // Categorías de gasto variable con un mínimo mensual configurado
    public List<MinimoCategoria> DetalleMinimos { get; set; } = [];

    // Aliases para las tarjetas "previstos" del Dashboard
    public decimal IngresosTotales => IngresosRecurrentes;
    public decimal GastosTotales => GastosRecurrentes + DetalleMinimos.Sum(d => d.Objetivo);

    // Suma de márgenes reales de todos los meses anteriores al visualizado
    public decimal SaldoAcumulado { get; set; }

    // Diferencia no cubierta por el gasto real en categorías con mínimo (0 si ya lo alcanzaron)
    public decimal GastosMinimoAjuste => DetalleMinimos.Sum(d => Math.Max(0, d.Minimo - d.GastoReal));

    // Balance real del mes sin restar la previsión pendiente de los mínimos
    public decimal BalanceSinAjuste => IngresosReales - GastosReales;

    // Balance del mes en curso (sin acumulado)
    public decimal Margen => IngresosReales - GastosReales - GastosMinimoAjuste;

    // Saldo total = margen del mes + lo acumulado de meses anteriores
    public decimal SaldoTotal => Margen + SaldoAcumulado;

    // Dinero real disponible ahora mismo: lo que ha sobrado este mes (sin restar
    // la previsión pendiente) más lo acumulado de meses anteriores
    public decimal SaldoRealDisponible => BalanceSinAjuste + SaldoAcumulado;

    // Nombres de las categorías que aún no han alcanzado su mínimo mensual,
    // para el aviso de previsión pendiente ("Alimentación y Transporte")
    public string NombresPendientes
    {
        get
        {
            var nombres = DetalleMinimos.Where(d => !d.Alcanzado).Select(d => d.Nombre).ToList();
            return nombres.Count switch
            {
                0 => "",
                1 => nombres[0],
                _ => string.Join(", ", nombres.Take(nombres.Count - 1)) + " y " + nombres[^1]
            };
        }
    }

    public decimal PorcentajeGasto => IngresosReales == 0
        ? (GastosReales == 0 ? 0 : 100)
        : Math.Round(GastosReales / IngresosReales * 100, 1);

    public NivelAlerta Nivel => PorcentajeGasto switch
    {
        < 80 => NivelAlerta.Ok,
        < 100 => NivelAlerta.Aviso,
        _ => NivelAlerta.Peligro
    };
}

public enum NivelAlerta { Ok, Aviso, Peligro }

public record MinimoCategoria(string Nombre, string Icono, decimal GastoReal, decimal Minimo)
{
    public decimal Objetivo => Math.Max(GastoReal, Minimo);
    public bool Alcanzado => GastoReal >= Minimo;
}
