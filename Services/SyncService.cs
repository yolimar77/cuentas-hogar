using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreEliminados = "deletions.json";
    private const string NombreIndice = "index.json";

    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError { get; private set; }
    public string? UltimoDetalle { get; private set; }

    public event Func<Task>? OnSyncCompletado;

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || SincronizandoAhora) return;
        SincronizandoAhora = true;
        UltimoError = null;

        try
        {
            var archivos = await drive.ListarArchivosAsync();
            var indice = await LeerIndiceAsync(archivos);

            int subidos = await SubirPendientesAsync(archivos, indice);
            int eliminados = await SincronizarEliminacionesAsync(archivos);
            int descargados = await DescargarYActualizarAsync(archivos, indice);

            await GuardarIndiceAsync(archivos, indice);

            UltimaSincronizacion = DateTime.Now;
            UltimoDetalle = $"↑{subidos} ↓{descargados} 🗑{eliminados} | Drive: {archivos.Count} archivos";

            if (OnSyncCompletado is not null)
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

    // --- Índice de versiones en Drive ---

    private async Task<Dictionary<string, DateTime>> LeerIndiceAsync(List<DriveFileInfo> archivos)
    {
        var archivo = archivos.FirstOrDefault(f => f.Nombre == NombreIndice);
        if (archivo is null) return [];
        var contenido = await drive.DescargarArchivoAsync(archivo.Id);
        if (contenido is null) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(contenido, _json) ?? []; }
        catch { return []; }
    }

    private async Task GuardarIndiceAsync(List<DriveFileInfo> archivos, Dictionary<string, DateTime> indice)
    {
        if (indice.Count == 0) return;
        var json = JsonSerializer.Serialize(indice);
        var archivo = archivos.FirstOrDefault(f => f.Nombre == NombreIndice);
        if (archivo is not null)
            await drive.ActualizarContenidoAsync(archivo.Id, json);
        else
            await drive.SubirArchivoAsync(NombreIndice, json);
    }

    // --- Subir pendientes (crear o actualizar en Drive) ---

    private async Task<int> SubirPendientesAsync(List<DriveFileInfo> archivos, Dictionary<string, DateTime> indice)
    {
        int count = 0;
        var driveIdx = archivos.ToDictionary(f => f.Nombre, f => f.Id);

        var movimientos = await db.ObtenerMovimientosAsync();
        foreach (var mov in movimientos.Where(m => !m.Sincronizado))
        {
            var nombre = $"mov-{mov.Id}.json";
            var json = JsonSerializer.Serialize(mov);
            bool ok = driveIdx.TryGetValue(nombre, out var fileId)
                ? await drive.ActualizarContenidoAsync(fileId, json)
                : await drive.SubirArchivoAsync(nombre, json);

            if (ok)
            {
                mov.Sincronizado = true;
                await db.GuardarMovimientoAsync(mov);
                indice[$"mov-{mov.Id}"] = mov.ModificadoEn;
                count++;
            }
        }

        var recurrentes = await db.ObtenerRecurrentesAsync();
        foreach (var rec in recurrentes.Where(r => !r.Sincronizado))
        {
            var nombre = $"rec-{rec.Id}.json";
            var json = JsonSerializer.Serialize(rec);
            bool ok = driveIdx.TryGetValue(nombre, out var fileId)
                ? await drive.ActualizarContenidoAsync(fileId, json)
                : await drive.SubirArchivoAsync(nombre, json);

            if (ok)
            {
                rec.Sincronizado = true;
                await db.GuardarRecurrenteAsync(rec);
                indice[$"rec-{rec.Id}"] = rec.ModificadoEn;
                count++;
            }
        }
        return count;
    }

    // --- Descargar nuevos y actualizar existentes según el índice ---

    private async Task<int> DescargarYActualizarAsync(List<DriveFileInfo> archivos, Dictionary<string, DateTime> indice)
    {
        int count = 0;
        var movLocales = (await db.ObtenerMovimientosAsync()).ToDictionary(m => m.Id);
        var recLocales = (await db.ObtenerRecurrentesAsync()).ToDictionary(r => r.Id);
        var eliminados = await db.ObtenerEliminadosAsync();

        foreach (var archivo in archivos)
        {
            if (archivo.Nombre == NombreEliminados || archivo.Nombre == NombreIndice) continue;

            if (archivo.Nombre.StartsWith("mov-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (eliminados.Contains(guid)) continue;

                bool esNuevo = !movLocales.ContainsKey(guid);
                bool hayActualizacion = !esNuevo
                    && indice.TryGetValue($"mov-{guid}", out var driveTs)
                    && driveTs > movLocales[guid].ModificadoEn;

                if (!esNuevo && !hayActualizacion) continue;

                var contenido = await drive.DescargarArchivoAsync(archivo.Id);
                if (contenido is null) continue;
                var mov = JsonSerializer.Deserialize<Movimiento>(contenido, _json);
                if (mov is null) continue;
                mov.Sincronizado = true;
                await db.GuardarMovimientoAsync(mov);
                count++;
            }
            else if (archivo.Nombre.StartsWith("rec-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (eliminados.Contains(guid)) continue;

                bool esNuevo = !recLocales.ContainsKey(guid);
                bool hayActualizacion = !esNuevo
                    && indice.TryGetValue($"rec-{guid}", out var driveTs)
                    && driveTs > recLocales[guid].ModificadoEn;

                if (!esNuevo && !hayActualizacion) continue;

                var contenido = await drive.DescargarArchivoAsync(archivo.Id);
                if (contenido is null) continue;
                var rec = JsonSerializer.Deserialize<MovimientoRecurrente>(contenido, _json);
                if (rec is null) continue;
                rec.Sincronizado = true;
                await db.GuardarRecurrenteAsync(rec);
                count++;
            }
        }
        return count;
    }

    // --- Propagar eliminaciones entre dispositivos ---

    private async Task<int> SincronizarEliminacionesAsync(List<DriveFileInfo> archivosEnDrive)
    {
        int aplicados = 0;
        var eliminadosLocales = await db.ObtenerEliminadosAsync();

        var archivoDel = archivosEnDrive.FirstOrDefault(f => f.Nombre == NombreEliminados);
        HashSet<string> eliminadosDrive = [];
        if (archivoDel is not null)
        {
            var contenido = await drive.DescargarArchivoAsync(archivoDel.Id);
            if (contenido is not null)
            {
                var lista = JsonSerializer.Deserialize<List<string>>(contenido, _json) ?? [];
                eliminadosDrive = lista.ToHashSet();
            }
        }

        foreach (var id in eliminadosDrive)
        {
            if (!eliminadosLocales.Contains(id))
            {
                await db.EliminarMovimientoAsync(id);
                await db.EliminarRecurrenteAsync(id);
                aplicados++;
            }
        }

        var todos = eliminadosLocales.Union(eliminadosDrive).ToHashSet();
        if (todos.Count > 0)
        {
            var json = JsonSerializer.Serialize(todos.ToList());
            if (archivoDel is not null)
                await drive.ActualizarContenidoAsync(archivoDel.Id, json);
            else
                await drive.SubirArchivoAsync(NombreEliminados, json);
        }

        foreach (var archivo in archivosEnDrive)
        {
            string? guid = null;
            if (archivo.Nombre.StartsWith("mov-") && archivo.Nombre.EndsWith(".json"))
                guid = archivo.Nombre[4..^5];
            else if (archivo.Nombre.StartsWith("rec-") && archivo.Nombre.EndsWith(".json"))
                guid = archivo.Nombre[4..^5];

            if (guid is not null && todos.Contains(guid))
                await drive.EliminarArchivoAsync(archivo.Id);
        }

        return aplicados;
    }
}
