namespace HomeAccounts.Models;

public class Movimiento
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public string? RecurrenteId { get; set; }
    public string? Periodo { get; set; }
}
