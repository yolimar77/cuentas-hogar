# Comparativa mensual por categoría — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir al Dashboard una sección "Lo más destacado este mes" que compare el gasto de cada categoría este mes contra su media de los 6 meses anteriores, destacando desviaciones significativas.

**Architecture:** Nuevo servicio `ComparativaService` (mismo patrón que `PrevisionService`/`SyncService`/`DriveService`) que calcula la comparativa a partir de los datos ya persistidos en `LocalDbService`. `Dashboard.razor` lo invoca dentro de su método `Cargar()` existente y pinta el resultado en una sección nueva.

**Tech Stack:** Blazor WebAssembly (.NET), C# records, LINQ sobre listas en memoria (sin base de datos relacional).

## Global Constraints

- Solo categorías de tipo Gasto (no Ingresos) — spec, sección "Alcance".
- Umbral de inclusión: `MediaAnterior > 0` requiere `|% desviación| ≥ 25` y `|desviación en €| ≥ 20€`; `MediaAnterior == 0` requiere `TotalActual ≥ 20€` — spec, sección "Flujo de datos".
- Ventana de comparación: hasta 6 meses anteriores al mes visto, recortada al historial real disponible desde el primer movimiento de la app — spec, sección "Flujo de datos".
- Sin límite de elementos mostrados; orden por `|DiferenciaImporte|` descendente — spec, sección "Flujo de datos".
- No hay proyecto de tests automatizados en el repo — verificación manual únicamente (spec, sección "Testing").
- No se añade configuración de umbrales en Ajustes; valores fijos en código (spec, sección "Alcance").

---

### Task 1: Modelo `CategoriaDestacada`

**Files:**
- Create: `Models/CategoriaDestacada.cs`

**Interfaces:**
- Produces: `record CategoriaDestacada(string Nombre, string Icono, decimal TotalActual, decimal MediaAnterior, decimal DiferenciaImporte, decimal? DiferenciaPorcentaje)` con propiedad calculada `bool EsAumento`.

- [ ] **Step 1: Crear el record**

```csharp
namespace HomeAccounts.Models;

public record CategoriaDestacada(
    string Nombre,
    string Icono,
    decimal TotalActual,
    decimal MediaAnterior,
    decimal DiferenciaImporte,
    decimal? DiferenciaPorcentaje)
{
    public bool EsAumento => DiferenciaImporte > 0;
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores (el tipo no se usa todavía en ningún sitio, solo debe compilar).

- [ ] **Step 3: Commit**

```bash
git add Models/CategoriaDestacada.cs
git commit -m "feat: añadir modelo CategoriaDestacada"
```

---

### Task 2: `ComparativaService`

**Files:**
- Create: `Services/ComparativaService.cs`

**Interfaces:**
- Consumes: `LocalDbService.ObtenerMovimientosAsync() : Task<List<Movimiento>>`, `LocalDbService.ObtenerCategoriasAsync() : Task<List<Categoria>>` (mismos métodos que ya usa `PrevisionService`); `Movimiento.CategoriaId : string`, `Movimiento.Tipo : TipoMovimiento`, `Movimiento.Importe : decimal`, `Movimiento.Fecha : DateTime`; `Categoria.Id`, `Categoria.Nombre`, `Categoria.Icono`.
- Produces: `ComparativaService(LocalDbService db)` con `Task<List<CategoriaDestacada>> ObtenerDestacadosAsync(int mes, int anyo)`, usado por `Dashboard.razor` en la Tarea 4.

- [ ] **Step 1: Crear el servicio con el cálculo completo**

```csharp
using HomeAccounts.Models;

namespace HomeAccounts.Services;

public class ComparativaService(LocalDbService db)
{
    private const decimal PorcentajeMinimo = 25m;
    private const decimal ImporteMinimo = 20m;

    public async Task<List<CategoriaDestacada>> ObtenerDestacadosAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var categorias = await db.ObtenerCategoriasAsync();
        var catById = categorias.ToDictionary(c => c.Id);

        var inicioMesVisto = new DateTime(anyo, mes, 1);

        if (movimientos.Count == 0) return [];

        var primerMovimiento = movimientos.Min(m => m.Fecha);
        var inicioPrimerMes = new DateTime(primerMovimiento.Year, primerMovimiento.Month, 1);

        var mesesDisponibles = 0;
        for (int i = 1; i <= 6; i++)
        {
            var inicioMesAnterior = inicioMesVisto.AddMonths(-i);
            if (inicioMesAnterior < inicioPrimerMes) break;
            mesesDisponibles++;
        }
        if (mesesDisponibles == 0) return [];

        var gastosMesActual = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha.Month == mes && m.Fecha.Year == anyo)
            .ToList();

        var inicioVentana = inicioMesVisto.AddMonths(-mesesDisponibles);
        var gastosAnteriores = movimientos
            .Where(m => m.Tipo == TipoMovimiento.Gasto && m.Fecha >= inicioVentana && m.Fecha < inicioMesVisto)
            .ToList();

        var categoriaIds = gastosMesActual.Select(m => m.CategoriaId)
            .Union(gastosAnteriores.Select(m => m.CategoriaId))
            .Distinct();

        var resultado = new List<CategoriaDestacada>();

        foreach (var categoriaId in categoriaIds)
        {
            catById.TryGetValue(categoriaId, out var categoria);
            var nombre = categoria?.Nombre ?? "Sin categoría";
            var icono = categoria?.Icono ?? "•";

            var totalActual = gastosMesActual.Where(m => m.CategoriaId == categoriaId).Sum(m => m.Importe);
            var totalAnterior = gastosAnteriores.Where(m => m.CategoriaId == categoriaId).Sum(m => m.Importe);
            var mediaAnterior = totalAnterior / mesesDisponibles;

            var diferenciaImporte = totalActual - mediaAnterior;
            decimal? diferenciaPorcentaje = mediaAnterior > 0
                ? diferenciaImporte / mediaAnterior * 100
                : null;

            var incluir = mediaAnterior > 0
                ? Math.Abs(diferenciaPorcentaje!.Value) >= PorcentajeMinimo && Math.Abs(diferenciaImporte) >= ImporteMinimo
                : totalActual >= ImporteMinimo;

            if (!incluir) continue;

            resultado.Add(new CategoriaDestacada(
                nombre, icono, totalActual, mediaAnterior, diferenciaImporte, diferenciaPorcentaje));
        }

        return resultado.OrderByDescending(c => Math.Abs(c.DiferenciaImporte)).ToList();
    }
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Services/ComparativaService.cs
git commit -m "feat: añadir ComparativaService con cálculo de destacados mensuales"
```

---

### Task 3: Registrar el servicio en DI

**Files:**
- Modify: `Program.cs:12-15`

**Interfaces:**
- Consumes: `ComparativaService` (Tarea 2).

- [ ] **Step 1: Añadir el registro junto a los demás servicios**

En `Program.cs`, cambiar:

```csharp
builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();
```

por:

```csharp
builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<ComparativaService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat: registrar ComparativaService en el contenedor de DI"
```

---

### Task 4: Sección "Lo más destacado" en el Dashboard

**Files:**
- Modify: `Pages/Dashboard.razor` (inject, campo, carga en `Cargar()`, marcado de la sección)

**Interfaces:**
- Consumes: `ComparativaService.ObtenerDestacadosAsync(int mes, int anyo) : Task<List<CategoriaDestacada>>` (Tarea 2); `CategoriaDestacada.Nombre/Icono/TotalActual/MediaAnterior/DiferenciaImporte/DiferenciaPorcentaje/EsAumento` (Tarea 1).

- [ ] **Step 1: Inyectar el servicio**

En `Pages/Dashboard.razor:1-9`, añadir la línea de inject junto a las demás:

```razor
@inject LocalDbService Db
@inject PrevisionService Prevision
@inject ComparativaService Comparativa
@inject SyncService Sync
@inject DriveService Drive
@inject NavigationManager Nav
```

- [ ] **Step 2: Añadir el campo de estado**

En el bloque `@code`, junto a `_gastosPorCategoria` (línea ~211):

```csharp
private List<GastoCategoria> _gastosPorCategoria = [];
private List<CategoriaDestacada> _destacados = [];
```

- [ ] **Step 3: Calcular los destacados dentro de `Cargar()`**

Al final del método `Cargar()` (`Pages/Dashboard.razor`, tras el bloque que rellena `_gastosPorCategoria`, tras la línea 288 `.ToList();`), añadir:

```csharp
_destacados = await Comparativa.ObtenerDestacadosAsync(_mesVista, _anyoVista);
```

- [ ] **Step 4: Pintar la sección en el marcado**

En `Pages/Dashboard.razor`, justo antes del bloque `<!-- Ver todos -->` (línea ~187), añadir:

```razor
<!-- Lo más destacado -->
<div class="mt-4">
    <h6 class="text-muted mb-2">Lo más destacado este mes</h6>
    @if (_destacados.Count == 0)
    {
        <p class="text-muted small">Sin cambios destacables este mes.</p>
    }
    else
    {
        @foreach (var d in _destacados)
        {
            <div class="d-flex justify-content-between align-items-center mb-2">
                <span class="small">@(d.EsAumento ? "🔺" : "🔻") @d.Icono @d.Nombre</span>
                <span class="small text-end">
                    <span class="fw-bold">@d.TotalActual.ToString("C")</span>
                    @if (d.DiferenciaPorcentaje is null)
                    {
                        <span class="text-muted"> · nuevo gasto este mes</span>
                    }
                    else if (d.TotalActual == 0)
                    {
                        <span class="text-muted"> · sin gasto este mes (antes ~@d.MediaAnterior.ToString("C")/mes)</span>
                    }
                    else
                    {
                        <span class="text-muted">
                            · @(d.EsAumento ? "+" : "")@d.DiferenciaImporte.ToString("C")
                            · @(d.EsAumento ? "+" : "")@d.DiferenciaPorcentaje.Value.ToString("F0")% vs. media
                        </span>
                    }
                </span>
            </div>
        }
    }
</div>
```

- [ ] **Step 5: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 6: Commit**

```bash
git add Pages/Dashboard.razor
git commit -m "feat: mostrar comparativa mensual destacada en el Dashboard"
```

---

### Task 5: Verificación manual funcional

**Files:** ninguno (solo ejecución de la app)

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run` (o `dotnet watch run`) y abrir el Dashboard en el navegador.

- [ ] **Step 2: Comprobar escenario de aumento destacado**

Con datos donde una categoría de gasto supere en el mes visible ≥25% y ≥20€ su media de los meses anteriores: debe aparecer con 🔺 y el texto "+X € · +Y% vs. media".

- [ ] **Step 3: Comprobar escenario de bajada destacada**

Con una categoría cuyo gasto este mes sea ≥25% y ≥20€ menor que su media: debe aparecer con 🔻 y valores negativos.

- [ ] **Step 4: Comprobar categoría sin cambio relevante**

Una categoría con desviación por debajo del umbral (p. ej. ±5%) no debe aparecer en la lista.

- [ ] **Step 5: Comprobar categoría nueva**

Registrar un gasto ≥20€ en una categoría sin gasto en los 6 meses anteriores: debe aparecer como "nuevo gasto este mes" (sin porcentaje).

- [ ] **Step 6: Comprobar categoría que dejó de tener gasto**

Con una categoría con media anterior relevante y 0€ este mes: debe aparecer como "sin gasto este mes (antes ~X €/mes)".

- [ ] **Step 7: Comprobar mes sin destacados**

Navegar a un mes sin ninguna desviación significativa: debe mostrarse "Sin cambios destacables este mes."

- [ ] **Step 8: Comprobar navegación entre meses**

Usar los botones de mes anterior/siguiente del Dashboard y confirmar que la lista se recalcula correctamente para la ventana de 6 meses relativa a cada mes visto.
