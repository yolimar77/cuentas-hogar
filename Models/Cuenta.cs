namespace HomeAccounts.Models;

public class Cuenta
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public decimal SaldoInicial { get; set; } = 0;
    public bool Activa { get; set; } = true;
}
