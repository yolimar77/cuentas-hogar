namespace HomeAccounts.Models;

public record CategoriaDestacada(
    string Nombre,
    string Icono,
    decimal TotalActual,
    decimal MediaAnterior,
    decimal DiferenciaImporte,
    decimal? DiferenciaPorcentaje)
{
    public bool EsAumento => DiferenciaImporte > 0;
}
