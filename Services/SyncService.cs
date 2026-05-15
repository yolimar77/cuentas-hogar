using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreEliminados = "deletions.json";

    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError { get; private set; }

    public event Func<Task>? OnSyncCompletado;

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || SincronizandoAhora) return;
        SincronizandoAhora = true;
        UltimoError = null;

        try
        {
            var archivosEnDrive = await drive.ListarArchivosAsync();
            await SubirPendientesAsync();
            await SincronizarEliminacionesAsync(archivosEnDrive);
            await DescargarNuevosAsync(archivosEnDrive);
            UltimaSincronizacion = DateTime.Now;
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

    // --- Subir lo que tenemos en local y no está en Drive ---

    private async Task SubirPendientesAsync()
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        foreach (var mov in movimientos.Where(m => !m.Sincronizado))
        {
            var nombre = $"mov-{mov.Id}.json";
            var json = JsonSerializer.Serialize(mov);
            if (await drive.SubirArchivoAsync(nombre, json))
            {
                mov.Sincronizado = true;
                await db.GuardarMovimientoAsync(mov);
            }
        }

        var recurrentes = await db.ObtenerRecurrentesAsync();
        foreach (var rec in recurrentes.Where(r => !r.Sincronizado))
        {
            var nombre = $"rec-{rec.Id}.json";
            var json = JsonSerializer.Serialize(rec);
            if (await drive.SubirArchivoAsync(nombre, json))
            {
                rec.Sincronizado = true;
                await db.GuardarRecurrenteAsync(rec);
            }
        }
    }

    // --- Propagar eliminaciones entre dispositivos ---

    private async Task SincronizarEliminacionesAsync(List<DriveFileInfo> archivosEnDrive)
    {
        var eliminadosLocales = await db.ObtenerEliminadosAsync();

        // Leer deletions.json de Drive si existe
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

        // Aplicar en local las eliminaciones que vinieron de Drive
        foreach (var id in eliminadosDrive)
        {
            if (!eliminadosLocales.Contains(id))
            {
                await db.EliminarMovimientoAsync(id);
                await db.EliminarRecurrenteAsync(id);
            }
        }

        // Fusionar y subir la lista actualizada a Drive si hay cambios
        var todos = eliminadosLocales.Union(eliminadosDrive).ToList();
        if (todos.Count > 0)
        {
            var json = JsonSerializer.Serialize(todos);
            if (archivoDel is not null)
                await drive.ActualizarContenidoAsync(archivoDel.Id, json);
            else
                await drive.SubirArchivoAsync(NombreEliminados, json);
        }

        // Borrar de Drive los archivos individuales de los elementos eliminados
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
    }

    // --- Descargar lo que hay en Drive y no tenemos en local ---

    private async Task DescargarNuevosAsync(List<DriveFileInfo> archivosEnDrive)
    {
        var movimientosLocales = await db.ObtenerMovimientosAsync();
        var recurrentesLocales = await db.ObtenerRecurrentesAsync();
        var eliminados = await db.ObtenerEliminadosAsync();

        var idsMovLocal = movimientosLocales.Select(m => m.Id).ToHashSet();
        var idsRecLocal = recurrentesLocales.Select(r => r.Id).ToHashSet();

        foreach (var archivo in archivosEnDrive)
        {
            if (archivo.Nombre == NombreEliminados) continue;

            if (archivo.Nombre.StartsWith("mov-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (eliminados.Contains(guid) || idsMovLocal.Contains(guid)) continue;

                var contenido = await drive.DescargarArchivoAsync(archivo.Id);
                if (contenido is null) continue;
                var mov = JsonSerializer.Deserialize<Movimiento>(contenido, _json);
                if (mov is null) continue;
                mov.Sincronizado = true;
                await db.GuardarMovimientoAsync(mov);
            }
            else if (archivo.Nombre.StartsWith("rec-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (eliminados.Contains(guid) || idsRecLocal.Contains(guid)) continue;

                var contenido = await drive.DescargarArchivoAsync(archivo.Id);
                if (contenido is null) continue;
                var rec = JsonSerializer.Deserialize<MovimientoRecurrente>(contenido, _json);
                if (rec is null) continue;
                rec.Sincronizado = true;
                await db.GuardarRecurrenteAsync(rec);
            }
        }
    }
}
