using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HomeAccounts.Services;

public class DriveService(IJSRuntime js, HttpClient http)
{
    private const string ClientId = "871337897281-n7p8ibsf01vgi80k4fq28gqkibkh2n1i.apps.googleusercontent.com";
    private const string ApiBase = "https://www.googleapis.com/drive/v3";
    private const string UploadBase = "https://www.googleapis.com/upload/drive/v3";
    private const string NombreCarpeta = "CuentasHogar";
    private const string TokenKey = "ha_drive_token";
    private const string FolderKey = "ha_folder_id";

    private string? _token;
    private string? _folderId;
    private DotNetObjectReference<DriveService>? _ref;
    private TaskCompletionSource<string>? _authTcs;

    private const string RedirectUri = "https://yolimar77.github.io/cuentas-hogar/";

    public bool Conectado => _token != null;
    public bool VieneDriveRedirect { get; set; } = false;
    public string? FolderId => _folderId;
    public bool NecesitaReconectar { get; private set; } = false;
    public event Action? OnEstadoCambiado;

    // --- Inicialización ---

    public async Task InicializarAsync()
    {
        _ref = DotNetObjectReference.Create(this);
        try { await js.InvokeVoidAsync("gis.init", ClientId, RedirectUri, _ref); }
        catch (Exception ex) { Console.WriteLine($"GIS init error: {ex.Message}"); }

        try
        {
            var redirectToken = await js.InvokeAsync<string?>("gis.checkRedirectToken");
            if (!string.IsNullOrEmpty(redirectToken))
            {
                _token = redirectToken;
                await GuardarTokenAsync(redirectToken);
                VieneDriveRedirect = true;
                OnEstadoCambiado?.Invoke();
                await CargarFolderIdAsync();
                return;
            }
        }
        catch (Exception ex) { Console.WriteLine($"checkRedirectToken error: {ex.Message}"); }

        try
        {
            var tokenGuardado = await js.InvokeAsync<string?>("storage.get", TokenKey);
            if (!string.IsNullOrEmpty(tokenGuardado)) _token = tokenGuardado;
        }
        catch { }

        await CargarFolderIdAsync();
    }

    private async Task CargarFolderIdAsync()
    {
        try
        {
            var fid = await js.InvokeAsync<string?>("storage.get", FolderKey);
            if (!string.IsNullOrEmpty(fid)) _folderId = fid;
        }
        catch { }
    }

    // --- Autenticación ---

    public async Task ConectarAsync()
    {
        NecesitaReconectar = false;
        _authTcs = new TaskCompletionSource<string>();
        await js.InvokeVoidAsync("gis.connect");
        try
        {
            var token = await _authTcs.Task.WaitAsync(TimeSpan.FromSeconds(120));
            _token = token;
            OnEstadoCambiado?.Invoke();
        }
        catch (TimeoutException) { }
    }

    [JSInvokable]
    public async void OnAuthSuccess(string token)
    {
        _token = token;
        await GuardarTokenAsync(token);
        _authTcs?.TrySetResult(token);
    }

    private async Task GuardarTokenAsync(string token) =>
        await js.InvokeVoidAsync("storage.set", TokenKey, token);

    [JSInvokable]
    public void OnAuthError(string error) =>
        _authTcs?.TrySetException(new Exception($"Error de autenticación: {error}"));

    private async Task<bool> IntentarRefreshSilenciosoAsync()
    {
        try
        {
            var nuevoToken = await js.InvokeAsync<string>("gis.silentRefresh", TimeSpan.FromSeconds(15));
            if (!string.IsNullOrEmpty(nuevoToken))
            {
                _token = nuevoToken;
                await GuardarTokenAsync(nuevoToken);
                return true;
            }
        }
        catch { }
        return false;
    }

    public async Task DesconectarAsync()
    {
        if (_token != null)
            await js.InvokeVoidAsync("gis.disconnect", _token);
        _token = null;
        NecesitaReconectar = false;
        await js.InvokeVoidAsync("storage.remove", TokenKey);
        OnEstadoCambiado?.Invoke();
    }

    // --- Gestión de carpeta compartida ---

    public async Task ObtenerOCrearCarpetaAsync()
    {
        if (!Conectado) return;

        // Verificar folder ID existente
        if (_folderId != null)
        {
            var verResp = await EnviarAsync(HttpMethod.Get, $"{ApiBase}/files/{_folderId}?fields=id,trashed");
            if (verResp.IsSuccessStatusCode)
            {
                try
                {
                    var doc = JsonDocument.Parse(await verResp.Content.ReadAsStringAsync());
                    var trashed = doc.RootElement.TryGetProperty("trashed", out var t) && t.GetBoolean();
                    if (!trashed)
                    {
                        NecesitaReconectar = false;
                        return; // carpeta accesible y válida
                    }
                }
                catch { }
            }
            else if (verResp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                     verResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (await IntentarRefreshSilenciosoAsync()) { _folderId = null; await ObtenerOCrearCarpetaAsync(); return; }
                NecesitaReconectar = true;
                OnEstadoCambiado?.Invoke();
                return;
            }
            // Carpeta borrada o no accesible, buscar/crear de nuevo
            _folderId = null;
        }

        // Buscar carpeta existente por nombre
        var q = Uri.EscapeDataString($"name='{NombreCarpeta}' and mimeType='application/vnd.google-apps.folder' and trashed=false");
        var searchResp = await EnviarAsync(HttpMethod.Get, $"{ApiBase}/files?q={q}&fields=files(id,name)&orderBy=createdTime");

        if (searchResp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            searchResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await IntentarRefreshSilenciosoAsync()) { await ObtenerOCrearCarpetaAsync(); return; }
            NecesitaReconectar = true;
            OnEstadoCambiado?.Invoke();
            return;
        }

        if (searchResp.IsSuccessStatusCode)
        {
            var searchJson = await searchResp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(searchJson);
            var files = doc.RootElement.GetProperty("files").EnumerateArray().ToList();
            if (files.Count > 0)
            {
                _folderId = files[0].GetProperty("id").GetString()!;
                await js.InvokeVoidAsync("storage.set", FolderKey, _folderId);
                NecesitaReconectar = false;
                OnEstadoCambiado?.Invoke();
                return;
            }
        }

        // Crear carpeta nueva
        var body = JsonSerializer.Serialize(new { name = NombreCarpeta, mimeType = "application/vnd.google-apps.folder" });
        var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/files?fields=id");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await http.SendAsync(req);

        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            NecesitaReconectar = true;
            OnEstadoCambiado?.Invoke();
            return;
        }

        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadAsStringAsync();
            _folderId = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;
            await js.InvokeVoidAsync("storage.set", FolderKey, _folderId);
            NecesitaReconectar = false;
            OnEstadoCambiado?.Invoke();
        }
    }

    public async Task EstablecerCarpetaExternaAsync(string folderId)
    {
        _folderId = folderId.Trim();
        await js.InvokeVoidAsync("storage.set", FolderKey, _folderId);
    }

    public async Task CompartirConAsync(string email)
    {
        if (_folderId == null || !Conectado) return;
        var body = JsonSerializer.Serialize(new { role = "writer", type = "user", emailAddress = email });
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"{ApiBase}/files/{_folderId}/permissions?sendNotificationEmail=false");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        await http.SendAsync(req);
    }

    // --- API de Drive ---

    public async Task<List<DriveFileInfo>> ListarArchivosAsync()
    {
        if (_folderId == null) return [];

        var q = Uri.EscapeDataString($"'{_folderId}' in parents and trashed=false");
        var url = $"{ApiBase}/files?q={q}&fields=files(id,name)&pageSize=1000";
        var response = await EnviarAsync(HttpMethod.Get, url);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await IntentarRefreshSilenciosoAsync())
            {
                response = await EnviarAsync(HttpMethod.Get, url);
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Error Drive ({(int)response.StatusCode})");
            }
            else
            {
                NecesitaReconectar = true;
                OnEstadoCambiado?.Invoke();
                throw new Exception("Token de Google expirado. Reconéctate.");
            }
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error Drive ({(int)response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(f => new DriveFileInfo(
                f.GetProperty("id").GetString()!,
                f.GetProperty("name").GetString()!))
            .ToList();
    }

    public async Task SubirArchivoAsync(string nombre, string contenidoJson)
    {
        if (_folderId == null) throw new Exception("Sin carpeta de Drive configurada.");

        const string boundary = "ha_boundary_xyz";
        var metadata = JsonSerializer.Serialize(new { name = nombre, parents = new[] { _folderId } });

        var body = new StringBuilder();
        body.Append($"--{boundary}\r\n");
        body.Append("Content-Type: application/json; charset=UTF-8\r\n\r\n");
        body.Append(metadata);
        body.Append($"\r\n--{boundary}\r\n");
        body.Append("Content-Type: application/json\r\n\r\n");
        body.Append(contenidoJson);
        body.Append($"\r\n--{boundary}--");

        var response = await EnviarUploadAsync(HttpMethod.Post, $"{UploadBase}/files?uploadType=multipart", body.ToString(), boundary);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error al subir '{nombre}' ({(int)response.StatusCode})");
    }

    public async Task<string?> DescargarArchivoAsync(string fileId)
    {
        var response = await EnviarAsync(HttpMethod.Get, $"{ApiBase}/files/{fileId}?alt=media");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task EliminarArchivoAsync(string fileId) =>
        await EnviarAsync(HttpMethod.Delete, $"{ApiBase}/files/{fileId}");

    public async Task ActualizarContenidoAsync(string fileId, string contenidoJson)
    {
        var url = $"{UploadBase}/files/{fileId}?uploadType=media";
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(contenidoJson, Encoding.UTF8, "application/json");
        var response = await http.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await IntentarRefreshSilenciosoAsync())
            {
                var retry = new HttpRequestMessage(HttpMethod.Patch, url);
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                retry.Content = new StringContent(contenidoJson, Encoding.UTF8, "application/json");
                response = await http.SendAsync(retry);
            }
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error al actualizar archivo Drive ({(int)response.StatusCode})");
    }

    private async Task<HttpResponseMessage> EnviarUploadAsync(HttpMethod method, string url, string body, string boundary)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/related")
        {
            Parameters = { new NameValueHeaderValue("boundary", boundary) }
        };
        var response = await http.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await IntentarRefreshSilenciosoAsync())
            {
                var retry = new HttpRequestMessage(method, url);
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                retry.Content = new StringContent(body, Encoding.UTF8);
                retry.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/related")
                {
                    Parameters = { new NameValueHeaderValue("boundary", boundary) }
                };
                response = await http.SendAsync(retry);
            }
        }

        return response;
    }

    private Task<HttpResponseMessage> EnviarAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return http.SendAsync(request);
    }
}

public record DriveFileInfo(string Id, string Nombre);
