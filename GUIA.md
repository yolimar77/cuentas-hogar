# Guía completa: Cuentas del Hogar

Aplicación PWA de gestión de gastos familiares con sincronización en Google Drive.  
Stack: **Blazor WebAssembly (.NET 10)** + **Bootstrap 5** + **Phosphor Icons** + **GitHub Pages**.

---

## Índice

1. [Arquitectura general](#1-arquitectura-general)
2. [Requisitos previos](#2-requisitos-previos)
3. [Crear el repositorio en GitHub](#3-crear-el-repositorio-en-github)
4. [Configurar GitHub Pages](#4-configurar-github-pages)
5. [Configurar Google Cloud Console](#5-configurar-google-cloud-console)
6. [Crear el proyecto Blazor](#6-crear-el-proyecto-blazor)
7. [Estructura de archivos](#7-estructura-de-archivos)
8. [Modelos](#8-modelos)
9. [Servicios](#9-servicios)
10. [Páginas y Layout](#10-páginas-y-layout)
11. [Archivos wwwroot](#11-archivos-wwwroot)
12. [CI/CD con GitHub Actions](#12-cicd-con-github-actions)
13. [Primer despliegue](#13-primer-despliegue)
14. [Uso y sincronización entre dispositivos](#14-uso-y-sincronización-entre-dispositivos)
15. [Notas importantes](#15-notas-importantes)

---

## 1. Arquitectura general

```
┌─────────────────────────────────────────────────────┐
│  Blazor WASM PWA (corre en el navegador/móvil)      │
│                                                     │
│  LocalDbService ──→ localStorage                    │
│    ha_movimientos, ha_recurrentes, ha_cuentas,      │
│    ha_categorias, ha_eliminados                     │
│                                                     │
│  DriveService ───→ Google Drive API v3              │
│  SyncService  ───→ merge bidireccional last-write-  │
│                    wins por ModificadoEn            │
│  PrevisionService → cálculo de previsión mensual   │
└─────────────────────────────────────────────────────┘
```

**Persistencia local**: localStorage vía JS interop.  
**Sync**: 5 archivos JSON en carpeta `CuentasHogar` de Google Drive (`movimientos.json`, `recurrentes.json`, `categorias.json`, `cuentas.json`, `deletions.json`). Patrón tombstone para propagación de eliminaciones.  
**Despliegue**: GitHub Actions publica en GitHub Pages en cada push a `main`. La versión se inyecta en compilación como `v.DDMM.HH.MM`.

---

## 2. Requisitos previos

- .NET 10 SDK instalado en local
- Git instalado y configurado
- Cuenta GitHub
- Cuenta Google (para crear el proyecto OAuth y usar Drive)

---

## 3. Crear el repositorio en GitHub

1. Ir a [github.com](https://github.com) → **New repository**
2. Nombre: `cuentas-hogar` (o el que quieras, pero debe coincidir con la `base href` del `index.html`)
3. Visibilidad: **Public** (GitHub Pages gratis solo funciona en repos públicos con plan gratuito)
4. No inicializar con README
5. Crear el repositorio

---

## 4. Configurar GitHub Pages

1. En el repositorio recién creado → **Settings** → **Pages**
2. En "Build and deployment" → Source: seleccionar **GitHub Actions**
3. Guardar

Con esto GitHub Pages espera que un workflow suba los artefactos. El workflow se crea más adelante en `.github/workflows/deploy.yml`.

---

## 5. Configurar Google Cloud Console

Este es el paso más largo. Hay que crear un proyecto OAuth para que la app pueda acceder a Google Drive.

### 5.1 Crear el proyecto

1. Ir a [console.cloud.google.com](https://console.cloud.google.com)
2. En la barra superior, clic en el selector de proyecto → **Nuevo proyecto**
3. Nombre: `cuentas-hogar` (o cualquiera)
4. Clic en **Crear**
5. Asegurarse de que el proyecto nuevo está seleccionado en el selector

### 5.2 Activar la API de Google Drive

1. En el menú lateral: **APIs y servicios** → **Biblioteca**
2. Buscar `Google Drive API`
3. Clic en el resultado → **Habilitar**

### 5.3 Crear la pantalla de consentimiento OAuth

1. **APIs y servicios** → **Pantalla de consentimiento de OAuth**
2. Tipo de usuario: **Externo** → **Crear**
3. Rellenar los campos obligatorios:
   - Nombre de la aplicación: `Cuentas del Hogar`
   - Correo de asistencia: tu correo
   - Correo de contacto del desarrollador: tu correo
4. Clic en **Guardar y continuar**
5. En "Permisos" (Scopes): clic en **Añadir o quitar permisos**
   - Buscar `drive` → seleccionar `https://www.googleapis.com/auth/drive`
   - **Actualizar** → **Guardar y continuar**
6. En "Usuarios de prueba": añadir los correos que usarán la app (el tuyo y el de tu pareja)
7. **Guardar y continuar** → **Volver al panel**

> Mientras la app esté en modo "Prueba" (no verificada), solo los usuarios de prueba pueden conectarse. Para uso familiar es suficiente y no requiere verificación de Google.

### 5.4 Crear las credenciales OAuth

1. **APIs y servicios** → **Credenciales**
2. **Crear credenciales** → **ID de cliente de OAuth**
3. Tipo de aplicación: **Aplicación web**
4. Nombre: `cuentas-hogar-web` (o cualquiera)
5. En **Orígenes autorizados de JavaScript**, añadir:
   ```
   https://TU_USUARIO.github.io
   ```
6. En **URIs de redirección autorizadas**, añadir:
   ```
   https://TU_USUARIO.github.io/cuentas-hogar/
   ```
   > La barra final es importante. Debe coincidir exactamente con el `RedirectUri` en `DriveService.cs` y con el `<base href>` del `index.html`.
7. Clic en **Crear**
8. Se mostrará el **ID de cliente** — copiarlo, tiene este formato:
   ```
   XXXXXXXXXX-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com
   ```

Este ID va en `DriveService.cs` como constante `ClientId`.

---

## 6. Crear el proyecto Blazor

```bash
dotnet new blazorwasm -n HomeAccounts --pwa
cd HomeAccounts
git init
git remote add origin https://github.com/TU_USUARIO/cuentas-hogar.git
```

Reemplazar el contenido de `HomeAccounts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
    <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.8" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.8" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ServiceWorker Include="wwwroot\service-worker.js" PublishedContent="wwwroot\service-worker.published.js" />
  </ItemGroup>
</Project>
```

Borrar los archivos de ejemplo que crea la plantilla que no se van a usar:
```bash
rm Pages/Counter.razor Pages/Weather.razor
rm wwwroot/sample-data/weather.json
```

---

## 7. Estructura de archivos

```
HomeAccounts/
├── .github/workflows/deploy.yml
├── Models/
│   ├── TipoMovimiento.cs
│   ├── Movimiento.cs
│   ├── MovimientoRecurrente.cs
│   ├── Categoria.cs
│   ├── Cuenta.cs
│   └── PrevisionMensual.cs
├── Services/
│   ├── LocalDbService.cs
│   ├── DriveService.cs
│   ├── SyncService.cs
│   └── PrevisionService.cs
├── Pages/
│   ├── Dashboard.razor
│   ├── Movimientos.razor
│   ├── Recurrentes.razor
│   ├── Configuracion.razor
│   └── NotFound.razor
├── Layout/
│   └── MainLayout.razor
├── wwwroot/
│   ├── index.html
│   ├── css/app.css
│   ├── js/storage.js
│   ├── js/gis.js
│   ├── service-worker.js
│   ├── service-worker.published.js
│   ├── manifest.webmanifest
│   ├── .nojekyll          ← archivo vacío
│   ├── 404.html           ← copia de index.html
│   ├── favicon.png
│   ├── icon-192.png
│   ├── icon-512.png
│   └── lib/bootstrap/     ← Bootstrap 5 local
├── _Imports.razor
├── App.razor
└── Program.cs
```

---

## 8. Modelos

### `Models/TipoMovimiento.cs`
```csharp
namespace HomeAccounts.Models;
public enum TipoMovimiento { Ingreso, Gasto }
```

### `Models/Movimiento.cs`
```csharp
namespace HomeAccounts.Models;

public class Movimiento
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public DateTime ModificadoEn { get; set; } = DateTime.MinValue;
    public string? RecurrenteId { get; set; }
    public string? Periodo { get; set; }
}
```

### `Models/MovimientoRecurrente.cs`
```csharp
namespace HomeAccounts.Models;

public class MovimientoRecurrente
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public int DiaDelMes { get; set; } = 1;
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime FechaFin { get; set; } = DateTime.Today.AddYears(1);
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public DateTime ModificadoEn { get; set; } = DateTime.MinValue;

    public bool EstaActivoEnMes(int mes, int anyo) =>
        Activo &&
        new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
        new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Year, FechaFin.Month, 1);
}
```

### `Models/Categoria.cs`
```csharp
namespace HomeAccounts.Models;

public class Categoria
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public TipoMovimiento Tipo { get; set; }
    public string Icono { get; set; } = "💰";
    public DateTime ModificadoEn { get; set; } = DateTime.UtcNow;
}
```

### `Models/Cuenta.cs`
```csharp
namespace HomeAccounts.Models;

public class Cuenta
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public decimal SaldoInicial { get; set; } = 0;
    public bool Activa { get; set; } = true;
    public DateTime ModificadoEn { get; set; } = DateTime.UtcNow;
}
```

### `Models/PrevisionMensual.cs`
```csharp
namespace HomeAccounts.Models;

public class PrevisionMensual
{
    public int Mes { get; set; }
    public int Anyo { get; set; }

    public decimal IngresosRecurrentes { get; set; }
    public decimal GastosRecurrentes { get; set; }
    public decimal IngresosReales { get; set; }
    public decimal GastosReales { get; set; }

    public decimal IngresosTotales => IngresosRecurrentes;
    public decimal GastosTotales   => GastosRecurrentes;
    public decimal Margen          => IngresosReales - GastosReales;

    public decimal PorcentajeGasto => IngresosReales == 0
        ? (GastosReales == 0 ? 0 : 100)
        : Math.Round(GastosReales / IngresosReales * 100, 1);

    public NivelAlerta Nivel => PorcentajeGasto switch
    {
        < 80  => NivelAlerta.Ok,
        < 100 => NivelAlerta.Aviso,
        _     => NivelAlerta.Peligro
    };
}

public enum NivelAlerta { Ok, Aviso, Peligro }
```

---

## 9. Servicios

### `Services/LocalDbService.cs`
```csharp
using HomeAccounts.Models;
using Microsoft.JSInterop;
using System.Text.Json;

namespace HomeAccounts.Services;

public class LocalDbService(IJSRuntime js)
{
    private const string KeyMovimientos = "ha_movimientos";
    private const string KeyRecurrentes = "ha_recurrentes";
    private const string KeyCuentas     = "ha_cuentas";
    private const string KeyCategorias  = "ha_categorias";
    private const string KeyEliminados  = "ha_eliminados";

    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private async Task<List<T>> CargarLista<T>(string key)
    {
        var result = await js.InvokeAsync<JsonElement?>("storage.get", key);
        if (result is null || result.Value.ValueKind == JsonValueKind.Null)
            return [];
        return JsonSerializer.Deserialize<List<T>>(result.Value.GetRawText(), _json) ?? [];
    }

    private async Task GuardarLista<T>(string key, List<T> lista) =>
        await js.InvokeVoidAsync("storage.set", key, lista);

    // --- Movimientos ---

    public Task<List<Movimiento>> ObtenerMovimientosAsync() =>
        CargarLista<Movimiento>(KeyMovimientos);

    public async Task GuardarMovimientoAsync(Movimiento mov)
    {
        var lista = await ObtenerMovimientosAsync();
        var idx = lista.FindIndex(m => m.Id == mov.Id);
        if (idx >= 0) lista[idx] = mov;
        else lista.Add(mov);
        await GuardarLista(KeyMovimientos, lista);
    }

    public async Task EliminarMovimientoAsync(string id)
    {
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
        var lista = await ObtenerRecurrentesAsync();
        var idx = lista.FindIndex(r => r.Id == rec.Id);
        if (idx >= 0) lista[idx] = rec;
        else lista.Add(rec);
        await GuardarLista(KeyRecurrentes, lista);
    }

    public async Task EliminarRecurrenteAsync(string id)
    {
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
        var lista = await ObtenerCuentasAsync();
        var idx = lista.FindIndex(c => c.Id == cuenta.Id);
        if (idx >= 0) lista[idx] = cuenta;
        else lista.Add(cuenta);
        await GuardarLista(KeyCuentas, lista);
    }

    public async Task EliminarCuentaAsync(string id)
    {
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
        var lista = await ObtenerCategoriasAsync();
        var idx = lista.FindIndex(c => c.Id == cat.Id);
        if (idx >= 0) lista[idx] = cat;
        else lista.Add(cat);
        await GuardarLista(KeyCategorias, lista);
    }

    public async Task EliminarCategoriaAsync(string id)
    {
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
        var lista = await CargarLista<string>(KeyEliminados);
        if (!lista.Contains(id))
        {
            lista.Add(id);
            await GuardarLista(KeyEliminados, lista);
        }
    }

    public async Task LimpiarEliminadoAsync(string id)
    {
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

    // --- Datos por defecto ---
    // IDs fijos para que todos los dispositivos generen los mismos IDs
    // y no haya duplicados al sincronizar

    public async Task InicializarDatosDefaultAsync()
    {
        var cuentas = await ObtenerCuentasAsync();
        if (cuentas.Count == 0)
        {
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
```

### `Services/PrevisionService.cs`
```csharp
using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class PrevisionService(LocalDbService db)
{
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos   = await db.ObtenerMovimientosAsync();
        var recurrentes   = await db.ObtenerRecurrentesAsync();
        var movMes        = movimientos.Where(m => m.Fecha.Month == mes && m.Fecha.Year == anyo);
        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        return new PrevisionMensual
        {
            Mes   = mes,
            Anyo  = anyo,
            IngresosRecurrentes = recActivosMes.Where(r => r.Tipo == TipoMovimiento.Ingreso).Sum(r => r.Importe),
            GastosRecurrentes   = recActivosMes.Where(r => r.Tipo == TipoMovimiento.Gasto).Sum(r => r.Importe),
            IngresosReales      = movMes.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe),
            GastosReales        = movMes.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe),
        };
    }

    // mesFin/anyoFin: hasta qué mes generar instancias de recurrentes.
    // Sin parámetros → hoy + 2 meses. Con parámetros → hasta ese mes (para navegar al futuro).
    public async Task GenerarMovimientosRecurrentesAsync(int? mesFin = null, int? anyoFin = null)
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy         = DateTime.Today;
        var finHorizonte = (mesFin.HasValue && anyoFin.HasValue)
            ? new DateTime(anyoFin.Value, mesFin.Value, DateTime.DaysInMonth(anyoFin.Value, mesFin.Value))
            : new DateTime(hoy.Year, hoy.Month, 1).AddMonths(2).AddDays(-1);

        foreach (var rec in recurrentes.Where(r => r.Activo))
        {
            var fecha  = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
            var limite = new DateTime(Math.Min(finHorizonte.Ticks, rec.FechaFin.Ticks));

            while (fecha <= limite)
            {
                var periodo = fecha.ToString("yyyy-MM");
                if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                {
                    var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                    await db.GuardarMovimientoAsync(new Movimiento
                    {
                        Concepto     = rec.Concepto,
                        Importe      = rec.Importe,
                        Tipo         = rec.Tipo,
                        Fecha        = new DateTime(fecha.Year, fecha.Month, dia),
                        CategoriaId  = rec.CategoriaId,
                        CuentaId     = rec.CuentaId,
                        RecurrenteId = rec.Id,
                        Periodo      = periodo
                    });
                }
                fecha = fecha.AddMonths(1);
            }
        }
    }
}
```

### `Services/DriveService.cs`
```csharp
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HomeAccounts.Services;

public class DriveService(IJSRuntime js, HttpClient http)
{
    // ⚠️  Sustituir por los valores propios de Google Cloud Console
    private const string ClientId    = "TU_CLIENT_ID.apps.googleusercontent.com";
    private const string RedirectUri = "https://TU_USUARIO.github.io/cuentas-hogar/";

    private const string ApiBase       = "https://www.googleapis.com/drive/v3";
    private const string UploadBase    = "https://www.googleapis.com/upload/drive/v3";
    private const string NombreCarpeta = "CuentasHogar";
    private const string TokenKey      = "ha_drive_token";
    private const string FolderKey     = "ha_folder_id";

    private string? _token;
    private string? _folderId;
    private DotNetObjectReference<DriveService>? _ref;
    private TaskCompletionSource<string>? _authTcs;

    public bool Conectado          => _token != null;
    public bool VieneDriveRedirect { get; set; } = false;
    public string? FolderId        => _folderId;
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

    // --- Gestión de carpeta Drive ---

    public async Task ObtenerOCrearCarpetaAsync()
    {
        if (!Conectado) return;

        if (_folderId != null)
        {
            var verResp = await EnviarAsync(HttpMethod.Get, $"{ApiBase}/files/{_folderId}?fields=id,trashed");
            if (verResp.IsSuccessStatusCode)
            {
                try
                {
                    var doc     = JsonDocument.Parse(await verResp.Content.ReadAsStringAsync());
                    var trashed = doc.RootElement.TryGetProperty("trashed", out var t) && t.GetBoolean();
                    if (!trashed) { NecesitaReconectar = false; return; }
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
            _folderId = null;
        }

        var q          = Uri.EscapeDataString($"name='{NombreCarpeta}' and mimeType='application/vnd.google-apps.folder' and trashed=false");
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
            var doc   = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync());
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

        var body = JsonSerializer.Serialize(new { name = NombreCarpeta, mimeType = "application/vnd.google-apps.folder" });
        var req  = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/files?fields=id");
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
        var req  = new HttpRequestMessage(HttpMethod.Post,
            $"{ApiBase}/files/{_folderId}/permissions?sendNotificationEmail=false");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        await http.SendAsync(req);
    }

    // --- API Drive ---

    public async Task<List<DriveFileInfo>> ListarArchivosAsync()
    {
        if (_folderId == null) return [];
        var q        = Uri.EscapeDataString($"'{_folderId}' in parents and trashed=false");
        var url      = $"{ApiBase}/files?q={q}&fields=files(id,name)&pageSize=1000";
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
                _token = null;
                try { await js.InvokeVoidAsync("storage.remove", TokenKey); } catch { }
                OnEstadoCambiado?.Invoke();
                throw new Exception("Token de Google expirado. Reconéctate en Ajustes.");
            }
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error Drive ({(int)response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);
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
        var body     = new StringBuilder();
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
        var url     = $"{UploadBase}/files/{fileId}?uploadType=media";
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
```

### `Services/SyncService.cs`
```csharp
using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreMovs   = "movimientos.json";
    private const string NombreRecs   = "recurrentes.json";
    private const string NombreCats   = "categorias.json";
    private const string NombreCuents = "cuentas.json";
    private const string NombreDels   = "deletions.json";

    public bool SincronizandoAhora        { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError            { get; private set; }
    public string? UltimoDetalle          { get; private set; }

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

            // Orden importante: eliminaciones → datos → propagación de categorías
            var eliminados    = await SincronizarEliminacionesAsync(idx);
            int cambiosMov    = await MergeMovimientosAsync(idx, eliminados);
            int cambiosRec    = await MergeRecurrentesAsync(idx, eliminados);
            int cambiosCat    = await MergeCategoriasAsync(idx, eliminados);
            int cambiosCuent  = await MergeCuentasAsync(idx, eliminados);
            int cambiosPropag = await PropagarcategoriasRecurrentesAsync();

            int totalCambios = cambiosMov + cambiosRec + cambiosCat + cambiosCuent + cambiosPropag;
            UltimaSincronizacion = DateTime.Now;
            UltimoDetalle = $"Sync OK · {totalCambios} cambios";

            if (totalCambios > 0 && OnSyncCompletado is not null)
                await OnSyncCompletado.Invoke();
        }
        catch (Exception ex) { UltimoError = ex.Message; }
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

    // Propaga CategoriaId/CuentaId del recurrente a todos sus movimientos generados.
    // Necesario cuando el recurrente se edita en otro dispositivo y se sincroniza.
    private async Task<int> PropagarcategoriasRecurrentesAsync()
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var movimientos = await db.ObtenerMovimientosAsync();
        var recById     = recurrentes.ToDictionary(r => r.Id);

        bool cambio = false;
        foreach (var mov in movimientos.Where(m => m.RecurrenteId != null))
        {
            if (!recById.TryGetValue(mov.RecurrenteId!, out var rec)) continue;
            if (mov.CategoriaId != rec.CategoriaId || mov.CuentaId != rec.CuentaId)
            {
                mov.CategoriaId = rec.CategoriaId;
                mov.CuentaId    = rec.CuentaId;
                cambio = true;
            }
        }
        if (cambio) await db.ReemplazarMovimientosAsync(movimientos);
        return cambio ? 1 : 0;
    }

    private async Task<int> MergeMovimientosAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
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

        // Si dos dispositivos generaron el mismo recurrente+periodo con IDs distintos, queda el más reciente
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

        var listaIds  = lista.Select(m => m.Id).ToHashSet();
        var localById = local.ToDictionary(m => m.Id);
        bool hayCambios = lista.Count != local.Count
            || lista.Any(m => !localById.ContainsKey(m.Id))
            || local.Any(m => !listaIds.Contains(m.Id))
            || lista.Any(m => localById.TryGetValue(m.Id, out var l) && l.ModificadoEn != m.ModificadoEn);

        foreach (var mov in lista) mov.Sincronizado = true;
        await db.ReemplazarMovimientosAsync(lista);
        return hayCambios ? 1 : 0;
    }

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
        var json  = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreRecs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreRecs, json);

        var localById = local.ToDictionary(r => r.Id);
        bool hayCambios = lista.Count != local.Count
            || lista.Any(r => !localById.ContainsKey(r.Id))
            || local.Any(r => !merged.ContainsKey(r.Id))
            || lista.Any(r => localById.TryGetValue(r.Id, out var l) && l.ModificadoEn != r.ModificadoEn);

        foreach (var rec in lista) rec.Sincronizado = true;
        await db.ReemplazarRecurrentesAsync(lista);
        return hayCambios ? 1 : 0;
    }

    private async Task<int> MergeCategoriasAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local     = await db.ObtenerCategoriasAsync();
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

        var lista    = merged.Values.ToList();
        var json     = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCats, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCats, json);

        var listaIds = lista.Select(c => c.Id).ToHashSet();
        bool hayCambios = lista.Count != local.Count
            || lista.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || lista.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn);

        await db.ReemplazarCategoriasAsync(lista);
        return hayCambios ? 1 : 0;
    }

    private async Task<int> MergeCuentasAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local     = await db.ObtenerCuentasAsync();
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

        var lista    = merged.Values.ToList();
        var json     = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCuents, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCuents, json);

        var listaIds = lista.Select(c => c.Id).ToHashSet();
        bool hayCambios = lista.Count != local.Count
            || lista.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || lista.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn);

        await db.ReemplazarCuentasAsync(lista);
        return hayCambios ? 1 : 0;
    }

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
            var categorias  = await db.ObtenerCategoriasAsync();
            var cuentas     = await db.ObtenerCuentasAsync();
            var ids         = nuevasEliminaciones.ToHashSet();
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
```

---

## 10. Páginas y Layout

### `Program.cs`
```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HomeAccounts;
using HomeAccounts.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();

await builder.Build().RunAsync();
```

### `_Imports.razor`
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using HomeAccounts
@using HomeAccounts.Layout
@using HomeAccounts.Models
@using HomeAccounts.Services
```

### `App.razor`
```razor
@using HomeAccounts.Services
@inject LocalDbService Db
@inject PrevisionService Prevision
@inject DriveService Drive
@inject SyncService Sync
@inject NavigationManager Nav
@inject IJSRuntime JS
@implements IAsyncDisposable

<Router AppAssembly="@typeof(App).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
</Router>

@code {
    private DotNetObjectReference<App>? _ref;

    protected override async Task OnInitializedAsync()
    {
        await Db.InicializarDatosDefaultAsync();
        await Prevision.GenerarMovimientosRecurrentesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _ref = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("registerVisibilityHandler", _ref);
            await Drive.InicializarAsync();

            if (Drive.VieneDriveRedirect)
            {
                Drive.VieneDriveRedirect = false;
                Nav.NavigateTo("configuracion");
            }

            if (Drive.Conectado)
            {
                await Drive.ObtenerOCrearCarpetaAsync();
                await Sync.SincronizarAsync();
            }
        }
    }

    [JSInvokable]
    public async Task OnAppVisible()
    {
        if (Drive.Conectado)
        {
            await Drive.ObtenerOCrearCarpetaAsync();
            await Sync.SincronizarAsync();
        }
    }

    public async ValueTask DisposeAsync() => _ref?.Dispose();
}
```

### `Layout/MainLayout.razor`
```razor
@inherits LayoutComponentBase
@inject NavigationManager Nav
@inject DriveService Drive
@implements IDisposable
@using HomeAccounts.Services

@if (Drive.NecesitaReconectar)
{
    <div class="bg-warning text-dark px-3 py-2 d-flex justify-content-between align-items-center" style="font-size:0.85rem">
        <span><i class="ph-bold ph-warning"></i> Permisos de Google actualizados</span>
        <a href="configuracion" class="btn btn-sm btn-dark">Reconectar</a>
    </div>
}

<main class="pb-5">@Body</main>

<nav class="navbar fixed-bottom navbar-light bg-white border-top">
    <div class="container-fluid justify-content-around">
        <a href="" class="nav-link text-center @(EsRuta("/") ? "text-primary fw-bold" : "text-secondary")">
            <div><i class="ph-bold ph-house" style="font-size:1.4rem"></i></div><small>Inicio</small>
        </a>
        <a href="movimientos" class="nav-link text-center @(EsRuta("/movimientos") ? "text-primary fw-bold" : "text-secondary")">
            <div><i class="ph-bold ph-credit-card" style="font-size:1.4rem"></i></div><small>Movimientos</small>
        </a>
        <a href="recurrentes" class="nav-link text-center @(EsRuta("/recurrentes") ? "text-primary fw-bold" : "text-secondary")">
            <div><i class="ph-bold ph-arrows-clockwise" style="font-size:1.4rem"></i></div><small>Recurrentes</small>
        </a>
        <a href="configuracion" class="nav-link text-center @(EsRuta("/configuracion") ? "text-primary fw-bold" : "text-secondary")">
            <div><i class="ph-bold ph-gear" style="font-size:1.4rem"></i></div><small>Ajustes</small>
        </a>
    </div>
</nav>

@code {
    protected override void OnInitialized() =>
        Drive.OnEstadoCambiado += OnDriveCambiado;
    private void OnDriveCambiado() => InvokeAsync(StateHasChanged);
    public void Dispose() => Drive.OnEstadoCambiado -= OnDriveCambiado;

    private bool EsRuta(string ruta)
    {
        var path     = new Uri(Nav.Uri).AbsolutePath.TrimEnd('/');
        var base_    = new Uri(Nav.BaseUri).AbsolutePath.TrimEnd('/');
        var relativa = path.StartsWith(base_) ? path[base_.Length..] : path;
        return relativa == ruta || (ruta == "/" && relativa == "");
    }
}
```

### `Pages/Dashboard.razor`
```razor
@page "/"
@using HomeAccounts.Models
@using HomeAccounts.Services
@using System.Globalization
@inject LocalDbService Db
@inject PrevisionService Prevision
@inject SyncService Sync
@inject DriveService Drive
@inject NavigationManager Nav
@implements IDisposable

<div class="container-sm py-3">

    <!-- Estado de sincronización + versión -->
    <div class="d-flex align-items-center gap-2 mb-3" style="font-size:0.82rem;min-height:22px">
        @if (Sync.SincronizandoAhora)
        {
            <i class="ph-bold ph-arrows-clockwise ph-spin text-primary" style="font-size:1rem"></i>
            <span class="text-muted">Sincronizando...</span>
        }
        else if (!Drive.Conectado)
        {
            <i class="ph-bold ph-cloud-slash" style="color:#fd7e14;font-size:1rem"></i>
            <a href="configuracion" class="text-decoration-none" style="color:#fd7e14">Sin conexión a Drive · Ir a Ajustes</a>
        }
        else if (Sync.UltimoError is not null)
        {
            <i class="ph-bold ph-warning-circle text-danger" style="font-size:1rem"></i>
            <span class="text-danger">Error de sync · <a href="configuracion" class="text-danger">Ajustes</a></span>
        }
        else if (Sync.UltimaSincronizacion is not null)
        {
            <i class="ph-bold ph-cloud-check" style="color:#198754;font-size:1rem"></i>
            <span class="text-muted">Sincronizado · @Sync.UltimaSincronizacion.Value.ToString("HH:mm")</span>
        }
        <span class="ms-auto text-muted" style="font-size:0.72rem;opacity:0.6">@_version</span>
    </div>

    <!-- Navegación de mes -->
    <div class="d-flex align-items-center justify-content-between mb-3">
        <button class="btn btn-outline-secondary btn-sm" @onclick="MesAnterior">
            <i class="ph-bold ph-caret-left"></i>
        </button>
        <h5 class="mb-0 text-capitalize">@NombreMes()</h5>
        <button class="btn btn-outline-secondary btn-sm" @onclick="MesSiguiente">
            <i class="ph-bold ph-caret-right"></i>
        </button>
    </div>

    <!-- Alerta de previsión -->
    @if (_prevision is not null)
    {
        <div class="alert @AlertClass() mb-3" role="alert">
            <strong>@AlertIcono() @AlertMensaje()</strong>
            <div class="fs-4 fw-bold mt-1">@_prevision.Margen.ToString("C")</div>
            <div class="progress mt-2" style="height:6px">
                <div class="progress-bar @BarraClass()" style="width:@(Math.Min(_prevision.PorcentajeGasto,100).ToString("F1", CultureInfo.InvariantCulture))%"></div>
            </div>
            <small class="text-muted">@_prevision.PorcentajeGasto% de los ingresos comprometido</small>
        </div>
    }

    <!-- Tarjetas de resumen -->
    <div class="row g-2 mb-4">
        <div class="col-6">
            <div class="card text-bg-success">
                <div class="card-body py-2">
                    <div class="small">Ingresos previstos</div>
                    <div class="fs-5 fw-bold">@(_prevision?.IngresosTotales.ToString("C") ?? "...")</div>
                </div>
            </div>
        </div>
        <div class="col-6">
            <div class="card text-bg-danger">
                <div class="card-body py-2">
                    <div class="small">Gastos previstos</div>
                    <div class="fs-5 fw-bold">@(_prevision?.GastosTotales.ToString("C") ?? "...")</div>
                </div>
            </div>
        </div>
        <div class="col-6">
            <div class="card">
                <div class="card-body py-2">
                    <div class="small text-muted">Ingresos reales</div>
                    <div class="fw-bold text-success">@(_prevision?.IngresosReales.ToString("C") ?? "...")</div>
                </div>
            </div>
        </div>
        <div class="col-6">
            <div class="card">
                <div class="card-body py-2">
                    <div class="small text-muted">Gastos reales</div>
                    <div class="fw-bold text-danger">@(_prevision?.GastosReales.ToString("C") ?? "...")</div>
                </div>
            </div>
        </div>
    </div>

    <!-- Gráfico + categorías: columnas en PC, apilado en móvil -->
    <div class="row g-4">

        <div class="col-12 col-md-5">
            <h6 class="text-muted mb-2">Ahorro últimos 6 meses</h6>
            <div class="d-flex align-items-end gap-1" style="height:80px">
                @foreach (var m in _historial)
                {
                    var h = _maxBalance > 0
                        ? (int)((double)Math.Abs(m.Balance) / (double)_maxBalance * 76) + 4
                        : 4;
                    <div class="flex-fill d-flex align-items-end justify-content-center" style="height:80px">
                        <div style="width:80%;max-width:36px;height:@(h)px;background:@(m.EsPositivo ? "#198754" : "#dc3545");border-radius:3px 3px 0 0"></div>
                    </div>
                }
            </div>
            <div class="d-flex gap-1 mt-1">
                @foreach (var m in _historial)
                {
                    <div class="flex-fill text-center" style="font-size:0.7rem;color:#888">@m.Etiqueta</div>
                }
            </div>
            <div class="d-flex gap-1 mt-1">
                @foreach (var m in _historial)
                {
                    <div class="flex-fill text-center" style="font-size:0.65rem;color:@(m.EsPositivo ? "#198754" : "#dc3545")">
                        @(m.EsPositivo ? "+" : "")@m.Balance.ToString("N0")
                    </div>
                }
            </div>
        </div>

        <div class="col-12 col-md-7">
            <h6 class="text-muted mb-2">Gastos por categoría</h6>
            @if (_gastosPorCategoria.Count == 0)
            {
                <p class="text-muted small">Sin gastos registrados este mes.</p>
            }
            else
            {
                @foreach (var cat in _gastosPorCategoria)
                {
                    <div class="mb-2">
                        <div class="d-flex justify-content-between align-items-center mb-1">
                            <span class="small">@cat.Icono @cat.Nombre</span>
                            <span class="small fw-bold text-danger">
                                @cat.Total.ToString("C") <span class="text-muted fw-normal">@cat.Porcentaje.ToString("F0")%</span>
                            </span>
                        </div>
                        <div class="progress" style="height:5px">
                            <div class="progress-bar bg-danger" style="width:@cat.Porcentaje.ToString("F1", CultureInfo.InvariantCulture)%"></div>
                        </div>
                    </div>
                }
            }
        </div>

    </div>

    <div class="text-end mt-3">
        <a href="movimientos" class="small">Ver todos los movimientos →</a>
    </div>

</div>

@code {
    private static readonly string _version =
        (((System.Reflection.AssemblyInformationalVersionAttribute?)
            System.Attribute.GetCustomAttribute(
                System.Reflection.Assembly.GetExecutingAssembly(),
                typeof(System.Reflection.AssemblyInformationalVersionAttribute)))
        ?.InformationalVersion ?? "dev")
        .Split('+')[0];

    private PrevisionMensual? _prevision;
    private int _mesVista;
    private int _anyoVista;

    private record MesBalance(string Etiqueta, decimal Balance, bool EsPositivo);
    private record GastoCategoria(string Nombre, string Icono, decimal Total, decimal Porcentaje);

    private List<MesBalance> _historial = [];
    private List<GastoCategoria> _gastosPorCategoria = [];
    private decimal _maxBalance = 1;

    protected override async Task OnInitializedAsync()
    {
        var hoy = DateTime.Today;
        _mesVista  = hoy.Month;
        _anyoVista = hoy.Year;
        Sync.OnSyncCompletado  += OnSyncCompletado;
        Sync.OnEstadoCambiado  += OnEstadoCambiado;
        Drive.OnEstadoCambiado += OnEstadoCambiado;
        Nav.LocationChanged    += OnLocationChanged;
        await Cargar();
    }

    private async void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        var uri = new Uri(e.Location);
        if (uri.AbsolutePath.TrimEnd('/') is "" or "/")
        {
            await Cargar();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task Cargar()
    {
        await Prevision.GenerarMovimientosRecurrentesAsync(_mesVista, _anyoVista);
        _prevision = await Prevision.CalcularAsync(_mesVista, _anyoVista);

        var todos      = await Db.ObtenerMovimientosAsync();
        var categorias = await Db.ObtenerCategoriasAsync();
        var catById    = categorias.ToDictionary(c => c.Id);
        var es         = new CultureInfo("es-ES");

        _historial = [];
        for (int i = 5; i >= 0; i--)
        {
            var fecha    = new DateTime(_anyoVista, _mesVista, 1).AddMonths(-i);
            var movMes   = todos.Where(m => m.Fecha.Month == fecha.Month && m.Fecha.Year == fecha.Year);
            var ingresos = movMes.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe);
            var gastos   = movMes.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe);
            var balance  = ingresos - gastos;
            _historial.Add(new MesBalance(fecha.ToString("MMM", es), balance, balance >= 0));
        }
        _maxBalance = _historial.Max(m => Math.Abs(m.Balance));
        if (_maxBalance == 0) _maxBalance = 1;

        var gastosMes   = todos.Where(m =>
            m.Fecha.Month == _mesVista && m.Fecha.Year == _anyoVista &&
            m.Tipo == TipoMovimiento.Gasto).ToList();
        var totalGastos = gastosMes.Sum(m => m.Importe);

        _gastosPorCategoria = gastosMes
            .GroupBy(m => catById.ContainsKey(m.CategoriaId ?? "") ? (m.CategoriaId ?? "") : "")
            .Select(g =>
            {
                var cat   = catById.TryGetValue(g.Key, out var c) ? c : null;
                var total = g.Sum(m => m.Importe);
                return new GastoCategoria(
                    cat?.Nombre ?? "Sin categoría",
                    cat?.Icono  ?? "•",
                    total,
                    totalGastos > 0 ? Math.Round(total / totalGastos * 100, 1) : 0);
            })
            .OrderByDescending(g => g.Total)
            .ToList();
    }

    private async Task MesAnterior()
    {
        var fecha  = new DateTime(_anyoVista, _mesVista, 1).AddMonths(-1);
        _mesVista  = fecha.Month;
        _anyoVista = fecha.Year;
        await Cargar();
    }

    private async Task MesSiguiente()
    {
        var fecha  = new DateTime(_anyoVista, _mesVista, 1).AddMonths(1);
        _mesVista  = fecha.Month;
        _anyoVista = fecha.Year;
        await Cargar();
    }

    private async Task OnSyncCompletado() { await Cargar(); await InvokeAsync(StateHasChanged); }
    private void OnEstadoCambiado() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Sync.OnSyncCompletado  -= OnSyncCompletado;
        Sync.OnEstadoCambiado  -= OnEstadoCambiado;
        Drive.OnEstadoCambiado -= OnEstadoCambiado;
        Nav.LocationChanged    -= OnLocationChanged;
    }

    private string NombreMes() =>
        new DateTime(_anyoVista, _mesVista, 1).ToString("MMMM yyyy", new CultureInfo("es-ES"));

    private string AlertClass() => _prevision?.Nivel switch
    {
        NivelAlerta.Aviso   => "alert-warning",
        NivelAlerta.Peligro => "alert-danger",
        _                   => "alert-success"
    };
    private string BarraClass() => _prevision?.Nivel switch
    {
        NivelAlerta.Aviso   => "bg-warning",
        NivelAlerta.Peligro => "bg-danger",
        _                   => "bg-success"
    };
    private MarkupString AlertIcono() => _prevision?.Nivel switch
    {
        NivelAlerta.Aviso   => new MarkupString("<i class=\"ph-bold ph-warning\"></i>"),
        NivelAlerta.Peligro => new MarkupString("<i class=\"ph-bold ph-x-circle\"></i>"),
        _                   => new MarkupString("<i class=\"ph-bold ph-check-circle\"></i>")
    };
    private string AlertMensaje() => _prevision?.Nivel switch
    {
        NivelAlerta.Aviso   => "Atención: superas el 80% de tus ingresos previstos",
        NivelAlerta.Peligro => $"Los gastos superan los ingresos en {Math.Abs(_prevision.Margen):C}",
        _                   => "Margen disponible este mes"
    };
}
```

### `Pages/Movimientos.razor`
```razor
@page "/movimientos"
@using HomeAccounts.Models
@using HomeAccounts.Services
@inject LocalDbService Db
@inject PrevisionService Prevision
@inject SyncService Sync
@implements IDisposable

<div class="container-sm py-3">

    <div class="d-flex justify-content-between align-items-center mb-3">
        <h5 class="mb-0">Movimientos</h5>
        <button class="btn btn-primary btn-sm" @onclick="AbrirNuevo"><i class="ph-bold ph-plus"></i> Nuevo</button>
    </div>

    <div class="d-flex gap-2 mb-3 align-items-center">
        <button class="btn btn-outline-secondary btn-sm" @onclick="MesAnterior"><i class="ph-bold ph-caret-left"></i></button>
        <span class="flex-grow-1 text-center fw-bold">@MesTexto()</span>
        <button class="btn btn-outline-secondary btn-sm" @onclick="MesSiguiente"><i class="ph-bold ph-caret-right"></i></button>
    </div>

    @if (_movimientos.Count == 0)
    {
        <p class="text-muted small">No hay movimientos en este período.</p>
    }
    else
    {
        <ul class="list-group list-group-flush">
            @foreach (var mov in _movimientos.OrderByDescending(m => m.Fecha))
            {
                <li class="list-group-item d-flex justify-content-between align-items-center px-0">
                    <div>
                        <div>@mov.Concepto @(mov.RecurrenteId is not null ? new MarkupString("<i class=\"ph-bold ph-repeat\" style=\"font-size:0.8rem;opacity:0.6\"></i>") : new MarkupString(""))</div>
                        <small class="text-muted">@mov.Fecha.ToString("dd/MM") · @NombreCategoria(mov.CategoriaId)</small>
                    </div>
                    <div class="d-flex align-items-center gap-2">
                        <span class="fw-bold @(mov.Tipo == TipoMovimiento.Ingreso ? "text-success" : "text-danger")">
                            @(mov.Tipo == TipoMovimiento.Ingreso ? "+" : "-")@mov.Importe.ToString("C")
                        </span>
                        @if (mov.RecurrenteId is null)
                        {
                            <button class="btn btn-link btn-sm text-secondary p-0" @onclick="() => AbrirEditar(mov)"><i class="ph-bold ph-pencil-simple" style="font-size:1.1rem"></i></button>
                            <button class="btn btn-link btn-sm text-danger p-0" @onclick="() => Eliminar(mov.Id)"><i class="ph-bold ph-trash" style="font-size:1.1rem"></i></button>
                        }
                    </div>
                </li>
            }
        </ul>
    }

</div>

@if (_mostrarFormulario)
{
    <div class="modal d-block" tabindex="-1" style="background:rgba(0,0,0,0.5)">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h6 class="modal-title">@(_esEdicion ? "Editar movimiento" : "Nuevo movimiento")</h6>
                    <button class="btn-close" @onclick="Cerrar"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-2">
                        <label class="form-label small">Tipo</label>
                        <div class="btn-group w-100">
                            <button class="btn @(_nuevo.Tipo == TipoMovimiento.Gasto ? "btn-danger" : "btn-outline-danger")"
                                    @onclick="() => _nuevo.Tipo = TipoMovimiento.Gasto">Gasto</button>
                            <button class="btn @(_nuevo.Tipo == TipoMovimiento.Ingreso ? "btn-success" : "btn-outline-success")"
                                    @onclick="() => _nuevo.Tipo = TipoMovimiento.Ingreso">Ingreso</button>
                        </div>
                    </div>
                    <div class="mb-2">
                        <label class="form-label small">Concepto</label>
                        <input class="form-control" @bind="_nuevo.Concepto" placeholder="Descripción" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label small">Importe (€)</label>
                        <input class="form-control" type="number" step="0.01" @bind="_nuevo.Importe" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label small">Fecha</label>
                        <input class="form-control" type="date" @bind="_nuevo.Fecha" />
                    </div>
                    <div class="mb-2">
                        <label class="form-label small">Categoría</label>
                        <select class="form-select" @bind="_nuevo.CategoriaId">
                            <option value="">Sin categoría</option>
                            @foreach (var cat in _categorias.Where(c => c.Tipo == _nuevo.Tipo))
                            {
                                <option value="@cat.Id">@cat.Icono @cat.Nombre</option>
                            }
                        </select>
                    </div>
                    <div class="mb-2">
                        <label class="form-label small">Cuenta</label>
                        <select class="form-select" @bind="_nuevo.CuentaId">
                            @foreach (var cuenta in _cuentas)
                            {
                                <option value="@cuenta.Id">@cuenta.Nombre</option>
                            }
                        </select>
                    </div>
                </div>
                <div class="modal-footer">
                    <button class="btn btn-secondary btn-sm" @onclick="Cerrar">Cancelar</button>
                    <button class="btn btn-primary btn-sm" @onclick="Guardar">Guardar</button>
                </div>
            </div>
        </div>
    </div>
}

@code {
    private List<Movimiento> _movimientos = [];
    private List<Categoria>  _categorias  = [];
    private List<Cuenta>     _cuentas     = [];
    private bool _mostrarFormulario = false;
    private bool _esEdicion = false;
    private Movimiento _nuevo = new();
    private DateTime _mesFiltro = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    protected override async Task OnInitializedAsync()
    {
        Sync.OnSyncCompletado += OnSyncCompletado;
        await Cargar();
    }

    private async Task OnSyncCompletado() { await Cargar(); await InvokeAsync(StateHasChanged); }
    public void Dispose() => Sync.OnSyncCompletado -= OnSyncCompletado;

    private async Task Cargar()
    {
        await Prevision.GenerarMovimientosRecurrentesAsync(_mesFiltro.Month, _mesFiltro.Year);
        var todos    = await Db.ObtenerMovimientosAsync();
        _movimientos = todos.Where(m => m.Fecha.Month == _mesFiltro.Month && m.Fecha.Year == _mesFiltro.Year).ToList();
        _categorias  = await Db.ObtenerCategoriasAsync();
        _cuentas     = await Db.ObtenerCuentasAsync();
    }

    private void AbrirNuevo()
    {
        _nuevo = new Movimiento { Tipo = TipoMovimiento.Gasto, Fecha = DateTime.Today, ModificadoEn = DateTime.UtcNow };
        if (_cuentas.Count > 0) _nuevo.CuentaId = _cuentas[0].Id;
        _esEdicion = false;
        _mostrarFormulario = true;
    }

    private void AbrirEditar(Movimiento mov)
    {
        _nuevo = new Movimiento
        {
            Id = mov.Id, Tipo = mov.Tipo, Concepto = mov.Concepto, Importe = mov.Importe,
            Fecha = mov.Fecha, CategoriaId = mov.CategoriaId, CuentaId = mov.CuentaId,
            Notas = mov.Notas, CreadoEn = mov.CreadoEn, Sincronizado = false, ModificadoEn = DateTime.UtcNow
        };
        _esEdicion = true;
        _mostrarFormulario = true;
    }

    private async Task Guardar()
    {
        if (string.IsNullOrWhiteSpace(_nuevo.Concepto) || _nuevo.Importe <= 0) return;
        await Db.GuardarMovimientoAsync(_nuevo);
        await Cargar();
        _mostrarFormulario = false;
        _ = Sync.SincronizarAsync();
    }

    private async Task Eliminar(string id)
    {
        await Db.EliminarMovimientoAsync(id);
        await Cargar();
        _ = Sync.SincronizarAsync();
    }

    private void Cerrar() { _mostrarFormulario = false; _esEdicion = false; }

    private async Task MesAnterior() { _mesFiltro = _mesFiltro.AddMonths(-1); await Cargar(); }
    private async Task MesSiguiente() { _mesFiltro = _mesFiltro.AddMonths(1); await Cargar(); }
    private string MesTexto() => _mesFiltro.ToString("MMMM yyyy", new System.Globalization.CultureInfo("es-ES"));
    private string NombreCategoria(string id) => _categorias.FirstOrDefault(c => c.Id == id)?.Nombre ?? "";
}
```

### `Pages/Recurrentes.razor` y `Pages/Configuracion.razor`

El código completo de estas páginas está en el repositorio. En resumen:

**Recurrentes.razor**: lista ingresos y gastos fijos, permite crear/editar/eliminar recurrentes. Al guardar llama a `GenerarMovimientosRecurrentesAsync()` para crear los movimientos. Se refresca con `OnSyncCompletado`.

**Configuracion.razor**: gestiona la conexión a Google Drive, muestra el estado de sync, permite compartir la carpeta con otro correo, pegar el código de carpeta de otra cuenta, restablecer tombstones, y hacer CRUD de cuentas y categorías.

### `Pages/NotFound.razor`
```razor
@page "/404"
<div class="container-sm py-3">
    <h5>Página no encontrada</h5>
    <a href="">Volver al inicio</a>
</div>
```

---

## 11. Archivos wwwroot

### `wwwroot/index.html`
```html
<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Cuentas del hogar</title>
    <meta name="apple-mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-status-bar-style" content="default" />
    <meta name="apple-mobile-web-app-title" content="Cuentas" />
    <base href="/cuentas-hogar/" />
    <link rel="preload" id="webassembly" />
    <link rel="stylesheet" href="lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="icon" type="image/png" href="favicon.png" />
    <link href="HomeAccounts.styles.css" rel="stylesheet" />
    <link rel="stylesheet" href="https://unpkg.com/@phosphor-icons/web@2.1.1/src/bold/style.css" />
    <link href="manifest.webmanifest" rel="manifest" />
    <link rel="apple-touch-icon" sizes="512x512" href="icon-512.png" />
    <link rel="apple-touch-icon" sizes="192x192" href="icon-192.png" />
    <script type="importmap"></script>
</head>
<body>
    <div id="app">
        <svg class="loading-progress">
            <circle r="40%" cx="50%" cy="50%" />
            <circle r="40%" cx="50%" cy="50%" />
        </svg>
        <div class="loading-progress-text"></div>
    </div>
    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">🗙</span>
    </div>
    <script src="https://accounts.google.com/gsi/client" async></script>
    <script src="js/storage.js"></script>
    <script src="js/gis.js"></script>
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
    <script>navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });</script>
</body>
</html>
```

> `<base href="/cuentas-hogar/" />` debe coincidir exactamente con el nombre del repositorio de GitHub.  
> `wwwroot/404.html` debe ser una copia exacta de este archivo — GitHub Pages lo sirve en rutas inexistentes y así Blazor puede gestionar el routing.

### `wwwroot/js/storage.js`
```js
window.storage = {
    get:    (key)        => { const v = localStorage.getItem(key); return v ? JSON.parse(v) : null; },
    set:    (key, value) => localStorage.setItem(key, JSON.stringify(value)),
    remove: (key)        => localStorage.removeItem(key)
};
```

### `wwwroot/js/gis.js`
```js
window.registerVisibilityHandler = (dotNetRef) => {
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible')
            dotNetRef.invokeMethodAsync('OnAppVisible');
    });
};

window.gis = {
    _dotNetRef: null, _popupClient: null, _clientId: null, _redirectUri: null,

    init: (clientId, redirectUri, dotNetRef) => {
        window.gis._clientId    = clientId;
        window.gis._redirectUri = redirectUri;
        window.gis._dotNetRef   = dotNetRef;
        try {
            if (typeof google !== 'undefined' && google.accounts)
                window.gis._popupClient = google.accounts.oauth2.initTokenClient({
                    client_id: clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (r) => r.error
                        ? window.gis._dotNetRef.invokeMethodAsync('OnAuthError', r.error)
                        : window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', r.access_token)
                });
        } catch (e) { console.warn('GIS popup init fallido:', e); }
    },

    connect: () => {
        const esMobil = /iPhone|iPad|iPod|Android/i.test(navigator.userAgent);
        if (esMobil) {
            const params = new URLSearchParams({
                client_id: window.gis._clientId, redirect_uri: window.gis._redirectUri,
                response_type: 'token', scope: 'https://www.googleapis.com/auth/drive'
            });
            window.location.href = 'https://accounts.google.com/o/oauth2/v2/auth?' + params.toString();
        } else {
            if (!window.gis._popupClient && typeof google !== 'undefined' && google.accounts)
                window.gis._popupClient = google.accounts.oauth2.initTokenClient({
                    client_id: window.gis._clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (r) => r.error
                        ? window.gis._dotNetRef.invokeMethodAsync('OnAuthError', r.error)
                        : window.gis._dotNetRef.invokeMethodAsync('OnAuthSuccess', r.access_token)
                });
            if (window.gis._popupClient) window.gis._popupClient.requestAccessToken();
            else console.error('Cliente OAuth no disponible');
        }
    },

    checkRedirectToken: () => {
        const hash = window.location.hash;
        if (!hash) return null;
        const token = new URLSearchParams(hash.substring(1)).get('access_token');
        if (token) history.replaceState(null, '', window.location.pathname + window.location.search);
        return token;
    },

    silentRefresh: () => {
        if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) return Promise.reject('mobile');
        return new Promise((resolve, reject) => {
            if (typeof google === 'undefined' || !google.accounts) { reject('gis_not_available'); return; }
            try {
                const timeout = setTimeout(() => reject('timeout'), 12000);
                google.accounts.oauth2.initTokenClient({
                    client_id: window.gis._clientId,
                    scope: 'https://www.googleapis.com/auth/drive',
                    callback: (r) => { clearTimeout(timeout); r.error ? reject(r.error) : resolve(r.access_token); },
                    error_callback: (e) => { clearTimeout(timeout); reject(e?.type ?? 'error'); }
                }).requestAccessToken({ prompt: '' });
            } catch (e) { reject(String(e)); }
        });
    },

    disconnect: (token) => {
        try { if (typeof google !== 'undefined' && google.accounts) google.accounts.oauth2.revoke(token, () => {}); }
        catch (e) {}
    }
};
```

### `wwwroot/service-worker.published.js`
```js
self.importScripts('./service-worker-assets.js');
self.addEventListener('install',  event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch',    event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName       = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];
const base        = "/";
const baseUrl     = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    self.skipWaiting();  // toma control inmediatamente sin esperar a que se cierren las pestañas
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(p => p.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(p => p.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    await clients.claim();  // controla todos los clientes abiertos de inmediato
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);
        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        cachedResponse = await caches.open(cacheName).then(cache => cache.match(request));
    }
    return cachedResponse || fetch(event.request);
}
```

### `wwwroot/service-worker.js` (solo desarrollo)
```js
self.addEventListener('fetch', () => { });
```

### `wwwroot/manifest.webmanifest`
```json
{
  "name": "Cuentas del hogar",
  "short_name": "Cuentas",
  "id": "./",
  "start_url": "./",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#0d6efd",
  "prefer_related_applications": false,
  "lang": "es",
  "icons": [
    { "src": "icon-512.png", "type": "image/png", "sizes": "512x512" },
    { "src": "icon-192.png", "type": "image/png", "sizes": "192x192" }
  ]
}
```

### `wwwroot/css/app.css`

Usar el `app.css` que genera la plantilla de Blazor y añadir al final:
```css
@keyframes ph-spin {
    from { transform: rotate(0deg); }
    to   { transform: rotate(360deg); }
}
.ph-spin {
    display: inline-block;
    animation: ph-spin 1s linear infinite;
}
```

---

## 12. CI/CD con GitHub Actions

### `.github/workflows/deploy.yml`
```yaml
name: Publicar en GitHub Pages

on:
  push:
    branches: [ main ]

permissions:
  contents: read
  pages: write
  id-token: write

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deploy.outputs.page_url }}

    steps:
      - name: Descargar código
        uses: actions/checkout@v4

      - name: Instalar .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Compilar y publicar
        run: |
          BUILD_VER="v.$(TZ='Europe/Madrid' date +"%d%m.%H.%M")"
          dotnet publish HomeAccounts.csproj -c Release -o publish /p:InformationalVersion="$BUILD_VER"

      - name: Copiar .nojekyll
        run: cp wwwroot/.nojekyll publish/wwwroot/.nojekyll

      - name: Configurar GitHub Pages
        uses: actions/configure-pages@v5

      - name: Subir archivos
        uses: actions/upload-pages-artifact@v3
        with:
          path: publish/wwwroot

      - name: Publicar en GitHub Pages
        id: deploy
        uses: actions/deploy-pages@v4
```

La versión `v.DDMM.HH.MM` se inyecta en compilación en hora de Madrid y se lee en el Dashboard con `AssemblyInformationalVersionAttribute`. El `.Split('+')[0]` elimina el hash de git que .NET añade automáticamente.

---

## 13. Primer despliegue

```bash
git add .
git commit -m "Initial commit"
git push -u origin main
```

El workflow se dispara solo. En 2-3 minutos la app estará en:
```
https://TU_USUARIO.github.io/cuentas-hogar/
```

Cada vez que hagas `git push` a `main` se despliega automáticamente.

---

## 14. Uso y sincronización entre dispositivos

### Primer dispositivo
1. Abrir la app → **Ajustes** → **Conectar con Google**
   - En móvil: redirige a Google y vuelve con el token en la URL (flujo redirect)
   - En PC: abre un popup de Google
2. Tras conectar se crea automáticamente la carpeta `CuentasHogar` en tu Drive
3. En **Ajustes** aparece el **código de carpeta** (el ID de la carpeta de Drive) — copiarlo

### Segundo dispositivo (para compartir con la pareja)
**Opción A — mismo propietario del Drive:**
1. Abrir la app → Ajustes → pegar el código en "¿Tienes el código de otra cuenta?"
2. Pulsar "Usar" → sincroniza

**Opción B — otro correo de Google:**
1. En el primer dispositivo: Ajustes → "Dar acceso a otro correo" → introducir el email de la pareja
2. La pareja abre la app en su dispositivo → Ajustes → Conectar con Google (con su cuenta)
3. La pareja pega el código de carpeta → "Usar" → sincroniza

### Flujo de sync
- Al arrancar la app: sync automático
- Al volver a la app desde segundo plano: sync automático (`visibilitychange`)
- Al guardar cualquier dato: sync en background (`_ = Sync.SincronizarAsync()`)
- Manual: Ajustes → "Sincronizar ahora"

### Si los datos no coinciden entre dispositivos
Ajustes → **Restablecer sincronización** → limpia los tombstones locales y el archivo `deletions.json` de Drive, luego fuerza un sync completo.

---

## 15. Notas importantes

**IDs fijos de datos por defecto**  
Las categorías y cuentas iniciales tienen IDs como `def-nomina`, `def-corriente`, etc. Son fijos para que al inicializar en dos dispositivos distintos no se creen duplicados al sincronizar. Si fueran GUIDs aleatorios, cada dispositivo crearía sus propias copias y al sincronizar habría duplicados.

**Tombstones (eliminaciones)**  
Las eliminaciones se registran en `ha_eliminados` (localStorage) y en `deletions.json` (Drive). Sin este mecanismo, un elemento eliminado en un dispositivo volvería a aparecer al sincronizar con el otro.

**Merge last-write-wins**  
El campo `ModificadoEn` (UTC) decide qué versión de un elemento prevalece cuando existe en ambos dispositivos con contenido diferente.

**PropagarcategoriasRecurrentesAsync**  
Después de cada sync, actualiza `CategoriaId` y `CuentaId` en todos los movimientos generados por recurrentes. Necesario porque si editas un recurrente en el móvil (cambiando su categoría), los movimientos ya generados en el PC siguen teniendo la categoría anterior hasta que este método los actualiza.

**Deduplicación de recurrentes generados**  
Si dos dispositivos generan el mismo recurrente para el mismo mes con IDs distintos (porque ambos generaron offline), el merge queda solo con la instancia más reciente (`GroupBy` por `RecurrenteId_Periodo`).

**Generación dinámica al navegar**  
Al navegar a cualquier mes futuro (tanto en Dashboard como en Movimientos), se llama a `GenerarMovimientosRecurrentesAsync(mes, año)` para generar los movimientos de ese mes antes de mostrarlos. Sin esto, los meses más allá del horizonte de 2 meses aparecerían vacíos.

**Service Worker con `skipWaiting` + `clients.claim`**  
Las actualizaciones de la PWA se aplican inmediatamente al instalar el nuevo service worker, sin necesidad de cerrar y reabrir la app. Si no se hiciera esto, el usuario podría estar usando una versión vieja hasta reiniciar.

**Token OAuth en móvil vs PC**  
En móvil se usa redirect flow (la librería GIS de Google no funciona bien en Safari/WebView). En PC se usa popup. El refresh silencioso (`silentRefresh`) solo funciona en PC porque requiere el popup de GIS.

**`<base href>`**  
Debe coincidir con el nombre del repositorio de GitHub. Si cambias el nombre del repo, hay que actualizar el `<base href>` en `index.html` y el `RedirectUri` en `DriveService.cs` (y actualizarlo también en Google Cloud Console).
