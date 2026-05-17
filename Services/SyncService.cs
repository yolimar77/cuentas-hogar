using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreMovs = "movimientos.json";
    private const string NombreRecs = "recurrentes.json";
    private const string NombreCats = "categorias.json";
    private const string NombreCuents = "cuentas.json";
    private const string NombreDels = "deletions.json";

    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError { get; private set; }
    public string? UltimoDetalle { get; private set; }

    public event Func<Task>? OnSyncCompletado;
    public event Action? OnEstadoCambiado;

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || drive.FolderId == null || SincronizandoAhora) return;
        SincronizandoAhora = true;
        UltimoError = null;
        OnEstadoCambiado?.Invoke();

        try
        {
            var archivos = await drive.ListarArchivosAsync();
            var idx = new Dictionary<string, DriveFileInfo>();
            foreach (var f in archivos) idx[f.Nombre] = f;

            // 1. Propagar eliminaciones
            var eliminados = await SincronizarEliminacionesAsync(idx);

            // 2. Merge categorías PRIMERO para obtener el mapa de IDs deduplicados
            var (cambiosCat, remapCats) = await MergeCategoriasAsync(idx, eliminados);

            // 3. Merge cuentas PRIMERO por el mismo motivo
            var (cambiosCuent, remapCuents) = await MergeCuentasAsync(idx, eliminados);

            // 4. Merge movimientos aplicando el remap de IDs
            int cambiosMov = await MergeMovimientosAsync(idx, eliminados, remapCats, remapCuents);

            // 5. Merge recurrentes aplicando el remap de IDs
            int cambiosRec = await MergeRecurrentesAsync(idx, eliminados, remapCats, remapCuents);

            int totalCambios = cambiosMov + cambiosRec + cambiosCat + cambiosCuent;
            UltimaSincronizacion = DateTime.Now;
            UltimoDetalle = $"Sync OK · {totalCambios} cambios";

            if (totalCambios > 0 && OnSyncCompletado is not null)
                await OnSyncCompletado.Invoke();
        }
        catch (Exception ex)
        {
            UltimoError = ex.Message;
        }
        finally
        {
            SincronizandoAhora = false;
            OnEstadoCambiado?.Invoke();
        }
    }

    public async Task RestablecerTombstonesAsync()
    {
        await db.LimpiarTodosEliminadosAsync();
        var archivos = await drive.ListarArchivosAsync();
        var idx = new Dictionary<string, DriveFileInfo>();
        foreach (var f in archivos) idx[f.Nombre] = f;
        if (idx.TryGetValue(NombreDels, out var arc))
            await drive.EliminarArchivoAsync(arc.Id);
    }

    // --- Categorías ---

    private async Task<(int cambios, Dictionary<string, string> remap)> MergeCategoriasAsync(
        Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local = await db.ObtenerCategoriasAsync();
        var localById = local.ToDictionary(c => c.Id);

        List<Categoria> deDrive = [];
        if (idx.TryGetValue(NombreCats, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Categoria>>(contenido, _json) ?? [];
        }

        var merged = new Dictionary<string, Categoria>(localById);
        foreach (var cat in deDrive)
        {
            if (eliminados.Contains(cat.Id)) continue;
            if (!merged.TryGetValue(cat.Id, out var localCat) || cat.ModificadoEn > localCat.ModificadoEn)
                merged[cat.Id] = cat;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Deduplicar por nombre+tipo: dos dispositivos pueden haber creado la misma
        // categoría con IDs distintos. Construimos un mapa idEliminado→idGanador para
        // reparar las referencias en movimientos y recurrentes.
        var remap = new Dictionary<string, string>();
        var lista = new List<Categoria>();
        foreach (var grupo in merged.Values.GroupBy(c => $"{c.Nombre.Trim().ToLowerInvariant()}_{(int)c.Tipo}"))
        {
            var ganador = grupo.OrderByDescending(c => c.ModificadoEn).First();
            lista.Add(ganador);
            foreach (var perdedor in grupo.Where(c => c.Id != ganador.Id))
                remap[perdedor.Id] = ganador.Id;
        }

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCats, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCats, json);

        var listaIds = lista.Select(c => c.Id).ToHashSet();
        bool hayCambios = lista.Count != local.Count
            || lista.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || lista.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn)
            || remap.Count > 0;

        await db.ReemplazarCategoriasAsync(lista);
        return (hayCambios ? 1 : 0, remap);
    }

    // --- Cuentas ---

    private async Task<(int cambios, Dictionary<string, string> remap)> MergeCuentasAsync(
        Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local = await db.ObtenerCuentasAsync();
        var localById = local.ToDictionary(c => c.Id);

        List<Cuenta> deDrive = [];
        if (idx.TryGetValue(NombreCuents, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Cuenta>>(contenido, _json) ?? [];
        }

        var merged = new Dictionary<string, Cuenta>(localById);
        foreach (var cuenta in deDrive)
        {
            if (eliminados.Contains(cuenta.Id)) continue;
            if (!merged.TryGetValue(cuenta.Id, out var localCuenta) || cuenta.ModificadoEn > localCuenta.ModificadoEn)
                merged[cuenta.Id] = cuenta;
        }
        foreach (var id in eliminados) merged.Remove(id);

        var remap = new Dictionary<string, string>();
        var lista = new List<Cuenta>();
        foreach (var grupo in merged.Values.GroupBy(c => c.Nombre.Trim().ToLowerInvariant()))
        {
            var ganador = grupo.OrderByDescending(c => c.ModificadoEn).First();
            lista.Add(ganador);
            foreach (var perdedor in grupo.Where(c => c.Id != ganador.Id))
                remap[perdedor.Id] = ganador.Id;
        }

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCuents, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCuents, json);

        var listaIds = lista.Select(c => c.Id).ToHashSet();
        bool hayCambios = lista.Count != local.Count
            || lista.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || lista.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn)
            || remap.Count > 0;

        await db.ReemplazarCuentasAsync(lista);
        return (hayCambios ? 1 : 0, remap);
    }

    // --- Movimientos ---

    private async Task<int> MergeMovimientosAsync(
        Dictionary<string, DriveFileInfo> idx,
        HashSet<string> eliminados,
        Dictionary<string, string> remapCats,
        Dictionary<string, string> remapCuents)
    {
        var local = await db.ObtenerMovimientosAsync();

        List<Movimiento> deDrive = [];
        if (idx.TryGetValue(NombreMovs, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Movimiento>>(contenido, _json) ?? [];
        }

        var merged = local.ToDictionary(m => m.Id);
        foreach (var mov in deDrive)
        {
            if (eliminados.Contains(mov.Id)) continue;
            if (!merged.TryGetValue(mov.Id, out var localMov) || mov.ModificadoEn > localMov.ModificadoEn)
                merged[mov.Id] = mov;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Reparar referencias a categorías/cuentas eliminadas por deduplicación
        foreach (var mov in merged.Values)
        {
            if (remapCats.TryGetValue(mov.CategoriaId ?? "", out var newCat)) mov.CategoriaId = newCat;
            if (remapCuents.TryGetValue(mov.CuentaId ?? "", out var newCuent)) mov.CuentaId = newCuent;
        }

        // Si dos dispositivos generaron el mismo recurrente+periodo con IDs distintos, queda uno
        var lista = merged.Values
            .GroupBy(m => m.RecurrenteId != null ? $"{m.RecurrenteId}_{m.Periodo}" : m.Id)
            .Select(g => g.OrderByDescending(m => m.ModificadoEn).First())
            .OrderBy(m => m.Fecha)
            .ToList();

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreMovs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreMovs, json);

        var listaIds = lista.Select(m => m.Id).ToHashSet();
        var localById = local.ToDictionary(m => m.Id);
        bool hayCambios = lista.Count != local.Count
            || lista.Any(m => !localById.ContainsKey(m.Id))
            || local.Any(m => !listaIds.Contains(m.Id))
            || lista.Any(m => localById.TryGetValue(m.Id, out var l) && l.ModificadoEn != m.ModificadoEn)
            || remapCats.Count > 0 || remapCuents.Count > 0;

        foreach (var mov in lista) mov.Sincronizado = true;
        await db.ReemplazarMovimientosAsync(lista);
        return hayCambios ? 1 : 0;
    }

    // --- Recurrentes ---

    private async Task<int> MergeRecurrentesAsync(
        Dictionary<string, DriveFileInfo> idx,
        HashSet<string> eliminados,
        Dictionary<string, string> remapCats,
        Dictionary<string, string> remapCuents)
    {
        var local = await db.ObtenerRecurrentesAsync();

        List<MovimientoRecurrente> deDrive = [];
        if (idx.TryGetValue(NombreRecs, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<MovimientoRecurrente>>(contenido, _json) ?? [];
        }

        var merged = local.ToDictionary(r => r.Id);
        foreach (var rec in deDrive)
        {
            if (eliminados.Contains(rec.Id)) continue;
            if (!merged.TryGetValue(rec.Id, out var localRec) || rec.ModificadoEn > localRec.ModificadoEn)
                merged[rec.Id] = rec;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Reparar referencias a categorías/cuentas eliminadas por deduplicación
        foreach (var rec in merged.Values)
        {
            if (remapCats.TryGetValue(rec.CategoriaId ?? "", out var newCat)) rec.CategoriaId = newCat;
            if (remapCuents.TryGetValue(rec.CuentaId ?? "", out var newCuent)) rec.CuentaId = newCuent;
        }

        var lista = merged.Values.ToList();

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreRecs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreRecs, json);

        var localById = local.ToDictionary(r => r.Id);
        bool hayCambios = lista.Count != local.Count
            || lista.Any(r => !localById.ContainsKey(r.Id))
            || local.Any(r => !merged.ContainsKey(r.Id))
            || lista.Any(r => localById.TryGetValue(r.Id, out var l) && l.ModificadoEn != r.ModificadoEn)
            || remapCats.Count > 0 || remapCuents.Count > 0;

        foreach (var rec in lista) rec.Sincronizado = true;
        await db.ReemplazarRecurrentesAsync(lista);
        return hayCambios ? 1 : 0;
    }

    // --- Eliminaciones ---

    private async Task<HashSet<string>> SincronizarEliminacionesAsync(Dictionary<string, DriveFileInfo> idx)
    {
        var locales = await db.ObtenerEliminadosAsync();

        HashSet<string> deDrive = [];
        if (idx.TryGetValue(NombreDels, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
            {
                var lista = JsonSerializer.Deserialize<List<string>>(contenido, _json) ?? [];
                deDrive = lista.ToHashSet();
            }
        }

        var nuevasEliminaciones = deDrive.Except(locales).ToList();
        if (nuevasEliminaciones.Count > 0)
        {
            var movimientos = await db.ObtenerMovimientosAsync();
            var recurrentes = await db.ObtenerRecurrentesAsync();
            var categorias = await db.ObtenerCategoriasAsync();
            var cuentas = await db.ObtenerCuentasAsync();

            var ids = nuevasEliminaciones.ToHashSet();
            movimientos.RemoveAll(m => ids.Contains(m.Id));
            recurrentes.RemoveAll(r => ids.Contains(r.Id));
            categorias.RemoveAll(c => ids.Contains(c.Id));
            cuentas.RemoveAll(c => ids.Contains(c.Id));

            await db.ReemplazarMovimientosAsync(movimientos);
            await db.ReemplazarRecurrentesAsync(recurrentes);
            await db.ReemplazarCategoriasAsync(categorias);
            await db.ReemplazarCuentasAsync(cuentas);

            foreach (var id in nuevasEliminaciones)
                await db.MarcarEliminadoAsync(id);
        }

        var todos = locales.Union(deDrive).ToHashSet();
        if (todos.Count > 0)
        {
            var json = JsonSerializer.Serialize(todos.ToList());
            if (idx.TryGetValue(NombreDels, out var archivoExistente))
                await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
            else
                await drive.SubirArchivoAsync(NombreDels, json);
        }

        return todos;
    }
}
