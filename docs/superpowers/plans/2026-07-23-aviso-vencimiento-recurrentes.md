# Aviso de vencimiento para pagos recurrentes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar un banner discreto e independiente del de reconexión con Drive, avisando cuando un pago recurrente con fecha de fin está a punto de vencer (30 días antes, y en los últimos 4 días: 3, 2, 1 y el propio día).

**Architecture:** Nuevo servicio de responsabilidad única, `RecurrenteAvisoService` (mismo patrón que `ComparativaService`), que calcula en vivo (sin estado persistido) qué recurrentes cumplen la condición hoy. `Layout/MainLayout.razor` lo invoca y pinta un banner nuevo, separado del de Drive.

**Tech Stack:** Blazor WebAssembly (.NET), C# records.

## Global Constraints

- Solo recurrentes activos (`Activo == true`) con `FechaFin` no nulo. Los indefinidos nunca generan aviso.
- Condición exacta: `diasRestantes == 30` o `diasRestantes` entre 0 y 3 (ambos inclusive). Fuera de esos valores, no se avisa.
- Sin estado persistido ni sincronizado — se recalcula desde `DateTime.Today` cada vez.
- Se muestran TODOS los recurrentes que cumplan la condición ese día, por nombre — nunca se resume ni se oculta ninguno.
- El banner de vencimiento es independiente del banner de reconexión con Drive (`@if (Drive.NecesitaReconectar)` ya existente) — ambos pueden mostrarse a la vez, apilados, sin compartir marcado ni lógica.
- No hay proyecto de tests automatizados — verificación manual únicamente.

---

### Task 1: Modelo `RecurrenteVencimiento`

**Files:**
- Create: `Models/RecurrenteVencimiento.cs`

**Interfaces:**
- Produces: `record RecurrenteVencimiento(string Concepto, int DiasRestantes)`.

- [ ] **Step 1: Crear el record**

```csharp
namespace HomeAccounts.Models;

public record RecurrenteVencimiento(string Concepto, int DiasRestantes);
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Models/RecurrenteVencimiento.cs
git commit -m "feat: añadir modelo RecurrenteVencimiento"
```

---

### Task 2: `RecurrenteAvisoService`

**Files:**
- Create: `Services/RecurrenteAvisoService.cs`

**Interfaces:**
- Consumes: `LocalDbService.ObtenerRecurrentesAsync() : Task<List<MovimientoRecurrente>>` (ya existe); `MovimientoRecurrente.Activo/FechaFin/Concepto` (`FechaFin` ya es `DateTime?` desde la mejora anterior).
- Produces: `RecurrenteAvisoService(LocalDbService db)` con `Task<List<RecurrenteVencimiento>> ObtenerPorVencerAsync()`, usado por `Layout/MainLayout.razor` en la Tarea 4.

- [ ] **Step 1: Crear el servicio con el cálculo completo**

```csharp
using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class RecurrenteAvisoService(LocalDbService db)
{
    public async Task<List<RecurrenteVencimiento>> ObtenerPorVencerAsync()
    {
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var hoy = DateTime.Today;

        var resultado = new List<RecurrenteVencimiento>();
        foreach (var rec in recurrentes.Where(r => r.Activo && r.FechaFin.HasValue))
        {
            var diasRestantes = (rec.FechaFin!.Value.Date - hoy).Days;
            if (diasRestantes == 30 || (diasRestantes >= 0 && diasRestantes <= 3))
                resultado.Add(new RecurrenteVencimiento(rec.Concepto, diasRestantes));
        }

        return resultado.OrderBy(r => r.DiasRestantes).ToList();
    }
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Services/RecurrenteAvisoService.cs
git commit -m "feat: añadir RecurrenteAvisoService con calculo de vencimientos"
```

---

### Task 3: Registrar el servicio en DI

**Files:**
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `RecurrenteAvisoService` (Tarea 2).

- [ ] **Step 1: Añadir el registro junto a los demás servicios**

Cambiar:

```csharp
builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<ComparativaService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();
```

por:

```csharp
builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<ComparativaService>();
builder.Services.AddScoped<RecurrenteAvisoService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat: registrar RecurrenteAvisoService en el contenedor de DI"
```

---

### Task 4: Banner de vencimiento en `MainLayout.razor`

**Files:**
- Modify: `Layout/MainLayout.razor`

**Interfaces:**
- Consumes: `RecurrenteAvisoService.ObtenerPorVencerAsync() : Task<List<RecurrenteVencimiento>>` (Tarea 2); `RecurrenteVencimiento.Concepto/DiasRestantes` (Tarea 1); `SyncService.OnSyncCompletado` (ya existe, usado en otras páginas de la app con la misma firma `event Func<Task>` o similar — seguir el mismo patrón de suscripción que ya usan `Dashboard.razor`/`Recurrentes.razor`).

- [ ] **Step 1: Inyectar los servicios nuevos**

En `Layout/MainLayout.razor`, cambiar:

```razor
@inherits LayoutComponentBase
@inject NavigationManager Nav
@inject DriveService Drive
@implements IDisposable
@using HomeAccounts.Services
```

por:

```razor
@inherits LayoutComponentBase
@inject NavigationManager Nav
@inject DriveService Drive
@inject SyncService Sync
@inject RecurrenteAvisoService Avisos
@implements IDisposable
@using HomeAccounts.Services
@using HomeAccounts.Models
```

- [ ] **Step 2: Añadir el banner de vencimiento, independiente del de Drive**

Cambiar:

```razor
@if (Drive.NecesitaReconectar)
{
    <div class="bg-warning text-dark px-3 py-2 d-flex flex-wrap justify-content-between align-items-center gap-2" style="font-size:0.85rem">
        <span><i class="ph-bold ph-warning"></i> Permisos de Google actualizados</span>
        <a href="configuracion" class="btn btn-sm btn-dark flex-shrink-0">Reconectar</a>
    </div>
}
```

por:

```razor
@if (Drive.NecesitaReconectar)
{
    <div class="bg-warning text-dark px-3 py-2 d-flex flex-wrap justify-content-between align-items-center gap-2" style="font-size:0.85rem">
        <span><i class="ph-bold ph-warning"></i> Permisos de Google actualizados</span>
        <a href="configuracion" class="btn btn-sm btn-dark flex-shrink-0">Reconectar</a>
    </div>
}

@if (_recurrentesPorVencer.Count > 0)
{
    <div class="bg-warning text-dark px-3 py-2" style="font-size:0.85rem">
        <div class="d-flex flex-wrap justify-content-between align-items-center gap-2">
            <span><i class="ph-bold ph-warning"></i> Pagos recurrentes a punto de vencer:</span>
            <a href="recurrentes" class="btn btn-sm btn-dark flex-shrink-0">Revisar</a>
        </div>
        <ul class="mb-0 mt-1 ps-3">
            @foreach (var r in _recurrentesPorVencer)
            {
                <li>@r.Concepto · @TextoVencimiento(r.DiasRestantes)</li>
            }
        </ul>
    </div>
}
```

- [ ] **Step 3: Cargar los datos y reaccionar a sincronización, en el bloque `@code`**

Cambiar:

```csharp
@code {
    protected override void OnInitialized() =>
        Drive.OnEstadoCambiado += OnDriveCambiado;

    private void OnDriveCambiado() => InvokeAsync(StateHasChanged);

    public void Dispose() => Drive.OnEstadoCambiado -= OnDriveCambiado;

    private bool EsRuta(string ruta)
    {
        var path = new Uri(Nav.Uri).AbsolutePath.TrimEnd('/');
        var base_ = new Uri(Nav.BaseUri).AbsolutePath.TrimEnd('/');
        var relativa = path.StartsWith(base_) ? path[base_.Length..] : path;
        return relativa == ruta || (ruta == "/" && relativa == "");
    }
}
```

por:

```csharp
@code {
    private List<RecurrenteVencimiento> _recurrentesPorVencer = [];

    protected override async Task OnInitializedAsync()
    {
        Drive.OnEstadoCambiado += OnDriveCambiado;
        Sync.OnSyncCompletado += OnSyncCompletado;
        await Cargar();
    }

    private async Task Cargar()
    {
        _recurrentesPorVencer = await Avisos.ObtenerPorVencerAsync();
    }

    private async Task OnSyncCompletado()
    {
        await Cargar();
        await InvokeAsync(StateHasChanged);
    }

    private void OnDriveCambiado() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Drive.OnEstadoCambiado -= OnDriveCambiado;
        Sync.OnSyncCompletado -= OnSyncCompletado;
    }

    private static string TextoVencimiento(int dias) => dias switch
    {
        0 => "vence hoy",
        1 => "vence mañana",
        _ => $"vence en {dias} días"
    };

    private bool EsRuta(string ruta)
    {
        var path = new Uri(Nav.Uri).AbsolutePath.TrimEnd('/');
        var base_ = new Uri(Nav.BaseUri).AbsolutePath.TrimEnd('/');
        var relativa = path.StartsWith(base_) ? path[base_.Length..] : path;
        return relativa == ruta || (ruta == "/" && relativa == "");
    }
}
```

(Nota: `OnInitialized` pasa a `OnInitializedAsync` porque ahora hay una carga async al arrancar. `SyncService.OnSyncCompletado` es `event Func<Task>?` (`Services/SyncService.cs:20`), la misma firma que ya consumen `Dashboard.razor` y `Recurrentes.razor` suscribiendo un método `private async Task OnSyncCompletado()` — el código de arriba ya sigue ese patrón exacto.)

- [ ] **Step 4: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 5: Commit**

```bash
git add Layout/MainLayout.razor
git commit -m "feat: mostrar banner de vencimiento de recurrentes en MainLayout"
```

---

### Task 5: Verificación manual funcional

**Files:** ninguno (solo ejecución de la app)

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run` (o `dotnet watch run`).

- [ ] **Step 2: Crear un recurrente con vencimiento a 30 días**

En Recurrentes, crear/editar uno con `FechaFin` = hoy + 30 días. Comprobar que aparece el banner con "vence en 30 días".

- [ ] **Step 3: Comprobar los últimos 4 días**

Editar la fecha de fin a hoy + 3, +2, +1 y +0 días sucesivamente (guardando cada vez) y comprobar los textos "vence en 3 días", "vence en 2 días", "vence mañana", "vence hoy".

- [ ] **Step 4: Comprobar que no aparece fuera de rango**

Con `FechaFin` a 29, 15 o 5 días, comprobar que el banner no se muestra para ese recurrente.

- [ ] **Step 5: Recurrente indefinido**

Un recurrente marcado "Sin fecha de fin" nunca debe aparecer en este banner, sin importar su antigüedad.

- [ ] **Step 6: Varios a la vez**

Configurar dos recurrentes distintos dentro del rango de aviso a la vez y comprobar que ambos aparecen listados por nombre, ordenados por urgencia (menos días primero).

- [ ] **Step 7: Ambos banners a la vez**

Si es posible (o simulando `Drive.NecesitaReconectar = true` temporalmente para la prueba), comprobar que el banner de Drive y el de vencimiento pueden mostrarse juntos, uno debajo del otro, sin interferir.

- [ ] **Step 8: Botón "Revisar"**

Pulsar el botón "Revisar" del banner de vencimiento y confirmar que lleva a la página de Recurrentes.
