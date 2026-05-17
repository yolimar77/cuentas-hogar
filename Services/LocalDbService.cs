using HomeAccounts.Models;
using Microsoft.JSInterop;
using System.Text.Json;

namespace HomeAccounts.Services;

public class LocalDbService(IJSRuntime js)
{
    private const string KeyMovimientos = "ha_movimientos";
    private const string KeyRecurrentes = "ha_recurrentes";
    private const string KeyCuentas = "ha_cuentas";
    private const string KeyCategorias = "ha_categorias";
    private const string KeyEliminados = "ha_eliminados";

    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // --- Genérico ---

    private async Task<List<T>> CargarLista<T>(string key)
    {
        var result = await js.InvokeAsync<JsonElement?>("storage.get", key);
        if (result is null || result.Value.ValueKind == JsonValueKind.Null)
            return [];
        return JsonSerializer.Deserialize<List<T>>(result.Value.GetRawText(), _json) ?? [];
    }

    private async Task GuardarLista<T>(string key, List<T> lista) =>
        await js.InvokeVoidAsync("storage.set", key, lista);

    // --- Movimientos ---

    public Task<List<Movimiento>> ObtenerMovimientosAsync() =>
        CargarLista<Movimiento>(KeyMovimientos);

    public async Task GuardarMovimientoAsync(Movimiento mov)
    {
        var lista = await ObtenerMovimientosAsync();
        var idx = lista.FindIndex(m => m.Id == mov.Id);
        if (idx >= 0) lista[idx] = mov;
        else lista.Add(mov);
        await GuardarLista(KeyMovimientos, lista);
    }

    public async Task EliminarMovimientoAsync(string id)
    {
        var lista = await ObtenerMovimientosAsync();
        lista.RemoveAll(m => m.Id == id);
        await GuardarLista(KeyMovimientos, lista);
        await MarcarEliminadoAsync(id);
    }

    public async Task<bool> ExisteMovimientoRecurrenteAsync(string recurrenteId, string periodo)
    {
        var lista = await ObtenerMovimientosAsync();
        return lista.Any(m => m.RecurrenteId == recurrenteId && m.Periodo == periodo);
    }

    // --- Recurrentes ---

    public Task<List<MovimientoRecurrente>> ObtenerRecurrentesAsync() =>
        CargarLista<MovimientoRecurrente>(KeyRecurrentes);

    public async Task GuardarRecurrenteAsync(MovimientoRecurrente rec)
    {
        var lista = await ObtenerRecurrentesAsync();
        var idx = lista.FindIndex(r => r.Id == rec.Id);
        if (idx >= 0) lista[idx] = rec;
        else lista.Add(rec);
        await GuardarLista(KeyRecurrentes, lista);
    }

    public async Task EliminarRecurrenteAsync(string id)
    {
        var lista = await ObtenerRecurrentesAsync();
        lista.RemoveAll(r => r.Id == id);
        await GuardarLista(KeyRecurrentes, lista);
        await MarcarEliminadoAsync(id);
    }

    // --- Cuentas ---

    public Task<List<Cuenta>> ObtenerCuentasAsync() =>
        CargarLista<Cuenta>(KeyCuentas);

    public async Task GuardarCuentaAsync(Cuenta cuenta)
    {
        var lista = await ObtenerCuentasAsync();
        var idx = lista.FindIndex(c => c.Id == cuenta.Id);
        if (idx >= 0) lista[idx] = cuenta;
        else lista.Add(cuenta);
        await GuardarLista(KeyCuentas, lista);
    }

    public async Task EliminarCuentaAsync(string id)
    {
        var lista = await ObtenerCuentasAsync();
        lista.RemoveAll(c => c.Id == id);
        await GuardarLista(KeyCuentas, lista);
        await MarcarEliminadoAsync(id);
    }

    // --- Categorías ---

    public Task<List<Categoria>> ObtenerCategoriasAsync() =>
        CargarLista<Categoria>(KeyCategorias);

    public async Task GuardarCategoriaAsync(Categoria cat)
    {
        var lista = await ObtenerCategoriasAsync();
        var idx = lista.FindIndex(c => c.Id == cat.Id);
        if (idx >= 0) lista[idx] = cat;
        else lista.Add(cat);
        await GuardarLista(KeyCategorias, lista);
    }

    public async Task EliminarCategoriaAsync(string id)
    {
        var lista = await ObtenerCategoriasAsync();
        lista.RemoveAll(c => c.Id == id);
        await GuardarLista(KeyCategorias, lista);
        await MarcarEliminadoAsync(id);
    }

    // --- Eliminados (tombstones para sync) ---

    public async Task<HashSet<string>> ObtenerEliminadosAsync()
    {
        var lista = await CargarLista<string>(KeyEliminados);
        return lista.ToHashSet();
    }

    public async Task MarcarEliminadoAsync(string id)
    {
        var lista = await CargarLista<string>(KeyEliminados);
        if (!lista.Contains(id))
        {
            lista.Add(id);
            await GuardarLista(KeyEliminados, lista);
        }
    }

    public async Task LimpiarEliminadoAsync(string id)
    {
        var lista = await CargarLista<string>(KeyEliminados);
        lista.Remove(id);
        await GuardarLista(KeyEliminados, lista);
    }

    public async Task ReemplazarMovimientosAsync(List<Movimiento> lista) =>
        await GuardarLista(KeyMovimientos, lista);

    public async Task LimpiarTodosEliminadosAsync() =>
        await GuardarLista(KeyEliminados, new List<string>());

    // --- Inicializar datos por defecto ---

    public async Task InicializarDatosDefaultAsync()
    {
        var cuentas = await ObtenerCuentasAsync();
        if (cuentas.Count == 0)
        {
            await GuardarCuentaAsync(new Cuenta { Nombre = "Cuenta corriente" });
            await GuardarCuentaAsync(new Cuenta { Nombre = "Efectivo" });
        }

        var categorias = await ObtenerCategoriasAsync();
        if (categorias.Count == 0)
        {
            var defaults = new List<Categoria>
            {
                new() { Nombre = "Nómina",        Tipo = TipoMovimiento.Ingreso, Icono = "💼" },
                new() { Nombre = "Otros ingresos", Tipo = TipoMovimiento.Ingreso, Icono = "💰" },
                new() { Nombre = "Alimentación",   Tipo = TipoMovimiento.Gasto,   Icono = "🛒" },
                new() { Nombre = "Transporte",     Tipo = TipoMovimiento.Gasto,   Icono = "🚗" },
                new() { Nombre = "Suministros",    Tipo = TipoMovimiento.Gasto,   Icono = "💡" },
                new() { Nombre = "Ocio",           Tipo = TipoMovimiento.Gasto,   Icono = "🎬" },
                new() { Nombre = "Salud",          Tipo = TipoMovimiento.Gasto,   Icono = "🏥" },
                new() { Nombre = "Préstamos",      Tipo = TipoMovimiento.Gasto,   Icono = "🏦" },
                new() { Nombre = "Otros gastos",   Tipo = TipoMovimiento.Gasto,   Icono = "📦" },
            };
            foreach (var cat in defaults)
                await GuardarCategoriaAsync(cat);
        }
    }
}
