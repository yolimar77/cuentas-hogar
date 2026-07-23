namespace HomeAccounts.Models;

public class Categoria
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public TipoMovimiento Tipo { get; set; }
    public string Icono { get; set; } = "💰";
    public decimal? MinimoMensual { get; set; }
    public DateTime ModificadoEn { get; set; } = DateTime.UtcNow;
}
