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

    private string? _token;

    public bool Conectado => _token != null;

    public async Task ConectarAsync()
    {
        _token = await js.InvokeAsync<string>("gis.requestTokenAsync", ClientId);
    }

    public async Task DesconectarAsync()
    {
        if (_token != null)
            await js.InvokeVoidAsync("gis.revokeToken", _token);
        _token = null;
    }

    // --- Listar archivos ---

    public async Task<List<DriveFileInfo>> ListarArchivosAsync()
    {
        var url = $"{ApiBase}/files?spaces=appDataFolder&fields=files(id,name)&pageSize=1000";
        var response = await EnviarAsync(HttpMethod.Get, url);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(f => new DriveFileInfo(
                f.GetProperty("id").GetString()!,
                f.GetProperty("name").GetString()!))
            .ToList();
    }

    // --- Subir archivo ---

    public async Task<bool> SubirArchivoAsync(string nombre, string contenidoJson)
    {
        const string boundary = "ha_boundary_xyz";
        var metadata = JsonSerializer.Serialize(new { name = nombre, parents = new[] { "appDataFolder" } });

        var body = new StringBuilder();
        body.Append($"--{boundary}\r\n");
        body.Append("Content-Type: application/json; charset=UTF-8\r\n\r\n");
        body.Append(metadata);
        body.Append($"\r\n--{boundary}\r\n");
        body.Append("Content-Type: application/json\r\n\r\n");
        body.Append(contenidoJson);
        body.Append($"\r\n--{boundary}--");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadBase}/files?uploadType=multipart");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(body.ToString(), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue($"multipart/related")
        {
            Parameters = { new NameValueHeaderValue("boundary", boundary) }
        };

        var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    // --- Descargar archivo ---

    public async Task<string?> DescargarArchivoAsync(string fileId)
    {
        var response = await EnviarAsync(HttpMethod.Get, $"{ApiBase}/files/{fileId}?alt=media");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    // --- Eliminar archivo ---

    public async Task EliminarArchivoAsync(string fileId)
    {
        await EnviarAsync(HttpMethod.Delete, $"{ApiBase}/files/{fileId}");
    }

    // --- Helper ---

    private Task<HttpResponseMessage> EnviarAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return http.SendAsync(request);
    }
}

public record DriveFileInfo(string Id, string Nombre);
