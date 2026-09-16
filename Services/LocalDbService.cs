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

    // Serializa las secuencias leer-modificar-escribir sobre el almacenamiento local. Sin esto,
    // una sincronización en curso (que lee, tarda segundos hablando con Drive, y luego sobrescribe
    // la lista completa) puede pisar con una foto vieja un movimiento guardado mientras tanto,
    // borrándolo sin más. Reentrante vía AsyncLocal: si el flujo async actual ya tiene el bloqueo
    // (p.ej. SincronizarAsync llamando a GenerarMovimientosRecurrentesAsync), no vuelve a esperar.
    private readonly SemaphoreSlim _bloqueo = new(1, 1);
    private static readonly AsyncLocal<bool> _bloqueoActivo = new();
    private static readonly IDisposable _bloqueoReentrante = new BloqueoNulo();

    public async Task<IDisposable> BloqueoAsync()
    {
        if (_bloqueoActivo.Value) return _bloqueoReentrante;
        await _bloqueo.WaitAsync();
        _bloqueoActivo.Value = true;
        return new Liberador(this);
    }

    private sealed class BloqueoNulo : IDisposable { public void Dispose() { } }

    private sealed class Liberador(LocalDbService owner) : IDisposable
    {
        public void Dispose()
        {
            _bloqueoActivo.Value = false;
            owner._bloqueo.Release();
        }
    }

    // --- Genérico ---

    private async Task<List<T>> CargarLista<T>(string key)
    {
        var result = await js.InvokeAsync<JsonElement?>("storage.get", key);
        if (result is null || result.Value.ValueKind == JsonValueKind.Null)
            return [];
        return JsonSerializer.Deserialize<List<T>>(result.Value.GetRawText(), _json) ?? [];
    }

    private async Task GuardarLista<T>(string key, List<T> lista)
    {
        await js.InvokeVoidAsync("storage.set", key, lista);

        // No fiarse a ciegas de que localStorage.setItem ha funcionado: releer y confirmar que lo
        // guardado coincide en número de elementos. Si no coincide (cuota llena, modo privado del
        // navegador, o cualquier fallo silencioso), fallar alto en vez de dejar creer al usuario
        // que su movimiento se guardó cuando en realidad se perdió.
        var guardado = await CargarLista<T>(key);
        if (guardado.Count != lista.Count)
            throw new InvalidOperationException(
                $"No se pudo confirmar el guardado en el dispositivo ({key}): se esperaban {lista.Count} elementos y hay {guardado.Count}. " +
                "Puede que el almacenamiento del navegador esté lleno o bloqueado (p.ej. modo privado).");
    }

    // --- Movimientos ---

    public Task<List<Movimiento>> ObtenerMovimientosAsync() =>
        CargarLista<Movimiento>(KeyMovimientos);

    public async Task GuardarMovimientoAsync(Movimiento mov)
    {
        using var _ = await BloqueoAsync();
        var lista = await ObtenerMovimientosAsync();
        var idx = lista.FindIndex(m => m.Id == mov.Id);
        if (idx >= 0) lista[idx] = mov;
        else lista.Add(mov);
        await GuardarLista(KeyMovimientos, lista);
    }

    public async Task EliminarMovimientoAsync(string id)
    {
        using var _ = await BloqueoAsync();
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
        using var _ = await BloqueoAsync();
        var lista = await ObtenerRecurrentesAsync();
        var idx = lista.FindIndex(r => r.Id == rec.Id);
        if (idx >= 0) lista[idx] = rec;
        else lista.Add(rec);
        await GuardarLista(KeyRecurrentes, lista);
    }

    public async Task EliminarRecurrenteAsync(string id)
    {
        using var _ = await BloqueoAsync();
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
        using var _ = await BloqueoAsync();
        var lista = await ObtenerCuentasAsync();
        var idx = lista.FindIndex(c => c.Id == cuenta.Id);
        if (idx >= 0) lista[idx] = cuenta;
        else lista.Add(cuenta);
        await GuardarLista(KeyCuentas, lista);
    }

    public async Task EliminarCuentaAsync(string id)
    {
        using var _ = await BloqueoAsync();
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
        using var _ = await BloqueoAsync();
        var lista = await ObtenerCategoriasAsync();
        var idx = lista.FindIndex(c => c.Id == cat.Id);
        if (idx >= 0) lista[idx] = cat;
        else lista.Add(cat);
        await GuardarLista(KeyCategorias, lista);
    }

    public async Task EliminarCategoriaAsync(string id)
    {
        using var _ = await BloqueoAsync();
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
        using var _ = await BloqueoAsync();
        var lista = await CargarLista<string>(KeyEliminados);
        if (!lista.Contains(id))
        {
            lista.Add(id);
            await GuardarLista(KeyEliminados, lista);
        }
    }

    public async Task LimpiarEliminadoAsync(string id)
    {
        using var _ = await BloqueoAsync();
        var lista = await CargarLista<string>(KeyEliminados);
        lista.Remove(id);
        await GuardarLista(KeyEliminados, lista);
    }

    public async Task ReemplazarMovimientosAsync(List<Movimiento> lista) =>
        await GuardarLista(KeyMovimientos, lista);

    public async Task ReemplazarRecurrentesAsync(List<MovimientoRecurrente> lista) =>
        await GuardarLista(KeyRecurrentes, lista);

    public async Task ReemplazarCategoriasAsync(List<Categoria> lista) =>
        await GuardarLista(KeyCategorias, lista);

    public async Task ReemplazarCuentasAsync(List<Cuenta> lista) =>
        await GuardarLista(KeyCuentas, lista);

    public async Task LimpiarTodosEliminadosAsync() =>
        await GuardarLista(KeyEliminados, new List<string>());

    // --- Inicializar datos por defecto ---

    public async Task InicializarDatosDefaultAsync()
    {
        var cuentas = await ObtenerCuentasAsync();
        if (cuentas.Count == 0)
        {
            // IDs fijos para que todos los dispositivos generen los mismos IDs y no haya duplicados al sincronizar
            await GuardarCuentaAsync(new Cuenta { Id = "def-corriente", Nombre = "Cuenta corriente" });
            await GuardarCuentaAsync(new Cuenta { Id = "def-efectivo",  Nombre = "Efectivo" });
        }

        var categorias = await ObtenerCategoriasAsync();
        if (categorias.Count == 0)
        {
            var defaults = new List<Categoria>
            {
                new() { Id = "def-nomina",     Nombre = "Nómina",         Tipo = TipoMovimiento.Ingreso, Icono = "💼" },
                new() { Id = "def-otros-ing",  Nombre = "Otros ingresos", Tipo = TipoMovimiento.Ingreso, Icono = "💰" },
                new() { Id = "def-aliment",    Nombre = "Alimentación",   Tipo = TipoMovimiento.Gasto,   Icono = "🛒" },
                new() { Id = "def-transp",     Nombre = "Transporte",     Tipo = TipoMovimiento.Gasto,   Icono = "🚗" },
                new() { Id = "def-sumin",      Nombre = "Suministros",    Tipo = TipoMovimiento.Gasto,   Icono = "💡" },
                new() { Id = "def-ocio",       Nombre = "Ocio",           Tipo = TipoMovimiento.Gasto,   Icono = "🎬" },
                new() { Id = "def-salud",      Nombre = "Salud",          Tipo = TipoMovimiento.Gasto,   Icono = "🏥" },
                new() { Id = "def-prestamos",  Nombre = "Préstamos",      Tipo = TipoMovimiento.Gasto,   Icono = "🏦" },
                new() { Id = "def-otros-gast", Nombre = "Otros gastos",   Tipo = TipoMovimiento.Gasto,   Icono = "📦" },
            };
            foreach (var cat in defaults)
                await GuardarCategoriaAsync(cat);
        }
    }
}
