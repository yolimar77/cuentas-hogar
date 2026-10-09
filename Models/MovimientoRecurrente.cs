namespace HomeAccounts.Models;

// IMPORTANTE: se guarda como numero (0, 1, 2...), no como texto — ver movimientos ya
// sincronizados en Drive. Los valores nuevos SIEMPRE se añaden al final, nunca en medio,
// o los recurrentes ya guardados con un valor existente pasarian a significar otra cosa.
public enum Frecuencia { Mensual, Semanal, Anual, Bimestral, Trimestral, Cuatrimestral, Semestral }

public class MovimientoRecurrente
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public Frecuencia Frecuencia { get; set; } = Frecuencia.Mensual;
    public int DiaDelMes { get; set; } = 1;
    public DayOfWeek DiaDeSemana { get; set; } = DayOfWeek.Monday;
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime? FechaFin { get; set; } = DateTime.Today.AddYears(1);
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public DateTime ModificadoEn { get; set; } = DateTime.MinValue;

    public bool EstaActivoEnMes(int mes, int anyo)
    {
        if (!Activo) return false;
        if (new DateTime(anyo, mes, 1) < new DateTime(FechaInicio.Year, FechaInicio.Month, 1)) return false;
        if (FechaFin is not null && new DateTime(anyo, mes, 1) > new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1)) return false;

        return Frecuencia switch
        {
            Frecuencia.Anual => mes == FechaInicio.Month,
            Frecuencia.Bimestral or Frecuencia.Trimestral or Frecuencia.Cuatrimestral or Frecuencia.Semestral =>
                ((anyo - FechaInicio.Year) * 12 + (mes - FechaInicio.Month)) % IntervaloMeses(Frecuencia) == 0,
            _ => true // Mensual, Semanal
        };
    }

    // Cada cuántos meses se repite un recurrente "cada N meses". Mensual=1 y Anual=12 se tratan
    // aparte (arriba) porque ya tenían su propia lógica probada; este mapeo es solo para las
    // frecuencias nuevas, todas con el mismo mecanismo genérico.
    public static int IntervaloMeses(Frecuencia f) => f switch
    {
        Frecuencia.Bimestral => 2,
        Frecuencia.Trimestral => 3,
        Frecuencia.Cuatrimestral => 4,
        Frecuencia.Semestral => 6,
        _ => 1
    };
}
