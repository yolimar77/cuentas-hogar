namespace HomeAccounts.Models;

public class MovimientoRecurrente
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public int DiaDelMes { get; set; } = 1;
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime FechaFin { get; set; } = DateTime.Today.AddYears(1);
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public DateTime ModificadoEn { get; set; } = DateTime.MinValue;

    public bool EstaActivoEnMes(int mes, int anyo) =>
        Activo &&
        new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
        new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Year, FechaFin.Month, 1);
}
