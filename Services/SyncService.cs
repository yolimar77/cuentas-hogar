using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

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
            await SubirPendientesAsync();
            await DescargarNuevosAsync();
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

    // --- Descargar lo que hay en Drive y no tenemos en local ---

    private async Task DescargarNuevosAsync()
    {
        var archivosEnDrive = await drive.ListarArchivosAsync();
        var movimientosLocales = await db.ObtenerMovimientosAsync();
        var recurrentesLocales = await db.ObtenerRecurrentesAsync();

        var idsMovLocal = movimientosLocales.Select(m => m.Id).ToHashSet();
        var idsRecLocal = recurrentesLocales.Select(r => r.Id).ToHashSet();

        foreach (var archivo in archivosEnDrive)
        {
            if (archivo.Nombre.StartsWith("mov-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (!idsMovLocal.Contains(guid))
                {
                    var contenido = await drive.DescargarArchivoAsync(archivo.Id);
                    if (contenido is null) continue;
                    var mov = JsonSerializer.Deserialize<Movimiento>(contenido, _json);
                    if (mov is null) continue;
                    mov.Sincronizado = true;
                    await db.GuardarMovimientoAsync(mov);
                }
            }
            else if (archivo.Nombre.StartsWith("rec-") && archivo.Nombre.EndsWith(".json"))
            {
                var guid = archivo.Nombre[4..^5];
                if (!idsRecLocal.Contains(guid))
                {
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
}
