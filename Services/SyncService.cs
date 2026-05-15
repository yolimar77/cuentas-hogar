namespace HomeAccounts.Services;

// Pendiente de implementar: lógica de sincronización con Google Drive.
// Subirá movimientos no sincronizados y descargará los que falten en local.
public class SyncService(DriveService drive, LocalDbService db)
{
    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || SincronizandoAhora) return;
        SincronizandoAhora = true;
        try
        {
            // TODO: implementar subida y bajada de archivos JSON en Drive
            await Task.Delay(500);
            UltimaSincronizacion = DateTime.Now;
        }
        finally
        {
            SincronizandoAhora = false;
        }
    }
}
