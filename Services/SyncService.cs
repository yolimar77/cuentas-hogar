using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreMovs = "movimientos.json";
    private const string NombreRecs = "recurrentes.json";
    private const string NombreDels = "deletions.json";

    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError { get; private set; }
    public string? UltimoDetalle { get; private set; }

    public event Func<Task>? OnSyncCompletado;

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || drive.FolderId == null || SincronizandoAhora) return;
        SincronizandoAhora = true;
        UltimoError = null;

        try
        {
            var archivos = await drive.ListarArchivosAsync();
            // Índice por nombre, último gana si hay duplicados
            var idx = new Dictionary<string, DriveFileInfo>();
            foreach (var f in archivos) idx[f.Nombre] = f;

            // 1. Propagar eliminaciones
            var eliminados = await SincronizarEliminacionesAsync(idx);

            // 2. Merge movimientos
            int cambiosMov = await MergeMovimientosAsync(idx, eliminados);

            // 3. Merge recurrentes
            int cambiosRec = await MergeRecurrentesAsync(idx, eliminados);

            UltimaSincronizacion = DateTime.Now;
            UltimoDetalle = $"Sync OK · {cambiosMov + cambiosRec} cambios";

            if ((cambiosMov + cambiosRec) > 0 && OnSyncCompletado is not null)
                await OnSyncCompletado.Invoke();
        }
        catch (Exception ex)
        {
            UltimoError = ex.Message;
        }
        finally
        {
            SincronizandoAhora = false;
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

    // --- Movimientos ---

    private async Task<int> MergeMovimientosAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local = await db.ObtenerMovimientosAsync();

        // Descargar de Drive
        List<Movimiento> deDrive = [];
        if (idx.TryGetValue(NombreMovs, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Movimiento>>(contenido, _json) ?? [];
        }

        // Merge: gana la versión con ModificadoEn más reciente
        var merged = local.ToDictionary(m => m.Id);
        foreach (var mov in deDrive)
        {
            if (eliminados.Contains(mov.Id)) continue;
            if (!merged.TryGetValue(mov.Id, out var localMov) || mov.ModificadoEn > localMov.ModificadoEn)
                merged[mov.Id] = mov;
        }
        foreach (var id in eliminados) merged.Remove(id);

        var lista = merged.Values.OrderBy(m => m.Fecha).ToList();

        // Subir resultado a Drive
        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreMovs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreMovs, json);

        // Actualizar local con lo que vino de Drive
        int cambios = 0;
        var localDict = local.ToDictionary(m => m.Id);
        foreach (var mov in lista)
        {
            if (!localDict.TryGetValue(mov.Id, out var localMov) || mov.ModificadoEn > localMov.ModificadoEn)
            {
                mov.Sincronizado = true;
                await db.GuardarMovimientoAsync(mov);
                cambios++;
            }
        }
        return cambios;
    }

    // --- Recurrentes ---

    private async Task<int> MergeRecurrentesAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
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

        var lista = merged.Values.ToList();

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreRecs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreRecs, json);

        int cambios = 0;
        var localDict = local.ToDictionary(r => r.Id);
        foreach (var rec in lista)
        {
            if (!localDict.TryGetValue(rec.Id, out var localRec) || rec.ModificadoEn > localRec.ModificadoEn)
            {
                rec.Sincronizado = true;
                await db.GuardarRecurrenteAsync(rec);
                cambios++;
            }
        }
        return cambios;
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

        // Aplicar eliminaciones remotas en local
        foreach (var id in deDrive)
        {
            if (!locales.Contains(id))
            {
                await db.EliminarMovimientoAsync(id);
                await db.EliminarRecurrenteAsync(id);
            }
        }

        // Fusionar y subir
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
