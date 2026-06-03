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

    // Balance del mes en curso (sin acumulado)
    public decimal Margen => IngresosReales - GastosReales;

    // Saldo total = margen del mes + lo acumulado de meses anteriores
    public decimal SaldoTotal => Margen + SaldoAcumulado;

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
