namespace HomeAccounts.Models;

public class PrevisionMensual
{
    public int Mes { get; set; }
    public int Anyo { get; set; }

    public decimal IngresosRecurrentes { get; set; }
    public decimal IngresosReales { get; set; }
    public decimal IngresosTotales => IngresosRecurrentes + IngresosReales;

    public decimal GastosRecurrentes { get; set; }
    public decimal GastosReales { get; set; }
    public decimal GastosTotales => GastosRecurrentes + GastosReales;

    public decimal Margen => IngresosTotales - GastosTotales;

    public decimal PorcentajeGasto => IngresosTotales == 0 ? 100
        : Math.Round(GastosTotales / IngresosTotales * 100, 1);

    public NivelAlerta Nivel => PorcentajeGasto switch
    {
        < 80 => NivelAlerta.Ok,
        < 100 => NivelAlerta.Aviso,
        _ => NivelAlerta.Peligro
    };
}

public enum NivelAlerta { Ok, Aviso, Peligro }
