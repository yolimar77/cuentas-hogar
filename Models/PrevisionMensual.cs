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

    // Aliases para las tarjetas "previstos" del Dashboard
    public decimal IngresosTotales => IngresosRecurrentes;
    public decimal GastosTotales => GastosRecurrentes;

    // Suma de márgenes reales de todos los meses anteriores al visualizado
    public decimal SaldoAcumulado { get; set; }

    // Balance y alerta basados en lo real + saldo acumulado de meses anteriores
    public decimal Margen => IngresosReales - GastosReales + SaldoAcumulado;

    // Base disponible = ingresos reales +/- saldo acumulado (negativo si se debía dinero)
    private decimal BaseDisponible => IngresosReales + SaldoAcumulado;

    public decimal PorcentajeGasto => BaseDisponible <= 0
        ? (GastosReales == 0 ? 0 : 100)
        : Math.Round(GastosReales / BaseDisponible * 100, 1);

    public NivelAlerta Nivel => PorcentajeGasto switch
    {
        < 80 => NivelAlerta.Ok,
        < 100 => NivelAlerta.Aviso,
        _ => NivelAlerta.Peligro
    };
}

public enum NivelAlerta { Ok, Aviso, Peligro }
