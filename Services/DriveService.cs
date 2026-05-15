namespace HomeAccounts.Services;

// Pendiente de implementar: autenticación OAuth con Google y llamadas a Drive API.
// Por ahora expone solo el estado de conexión para que la UI pueda mostrarlo.
public class DriveService
{
    public bool Conectado { get; private set; } = false;
    public string? Email { get; private set; }

    public Task ConectarAsync()
    {
        // TODO: iniciar flujo OAuth con Google Identity Services
        return Task.CompletedTask;
    }

    public Task DesconectarAsync()
    {
        Conectado = false;
        Email = null;
        return Task.CompletedTask;
    }
}
