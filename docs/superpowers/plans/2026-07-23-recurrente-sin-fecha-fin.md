# Fecha de fin opcional para pagos recurrentes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir marcar un pago recurrente como "sin fecha de fin" (indefinido), para que no haga falta actualizar manualmente una fecha lejana en gastos/ingresos que se repiten sin fin previsible.

**Architecture:** `MovimientoRecurrente.FechaFin` pasa de `DateTime` a `DateTime?` (`null` = indefinido). Se ajustan los dos cálculos de `PrevisionService` que asumían `FechaFin` como fecha concreta, y se añade un checkbox en `Recurrentes.razor` que oculta el campo "Hasta" cuando está marcado.

**Tech Stack:** Blazor WebAssembly (.NET), C#.

## Global Constraints

- El checkbox "Sin fecha de fin" no aparece marcado por defecto: un recurrente nuevo sigue pidiendo una fecha de fin real (hoy + 1 año).
- Sin cambios en `SyncService`: la fusión ya usa "gana el `ModificadoEn` más reciente" sobre el registro completo, y `FechaFin` es un campo más de ese registro.
- No se añade el sistema de avisos de vencimiento (se diseñará como mejora aparte).
- No hay proyecto de tests automatizados — verificación manual únicamente.

---

### Task 1: `FechaFin` opcional en `MovimientoRecurrente`

**Files:**
- Modify: `Models/MovimientoRecurrente.cs`

**Interfaces:**
- Produces: `MovimientoRecurrente.FechaFin : DateTime?` (`null` = sin fecha de fin); `EstaActivoEnMes(int mes, int anyo) : bool` actualizado para tratar `null` como "sin límite superior".

- [ ] **Step 1: Reemplazar el contenido completo del archivo**

```csharp
namespace HomeAccounts.Models;

public enum Frecuencia { Mensual, Semanal }

public class MovimientoRecurrente
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public Frecuencia Frecuencia { get; set; } = Frecuencia.Mensual;
    public int DiaDelMes { get; set; } = 1;
    public DayOfWeek DiaDeSemana { get; set; } = DayOfWeek.Monday;
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime? FechaFin { get; set; } = DateTime.Today.AddYears(1);
    public string CategoriaId { get; set; } = "";
    public string CuentaId { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Sincronizado { get; set; } = false;
    public DateTime ModificadoEn { get; set; } = DateTime.MinValue;

    public bool EstaActivoEnMes(int mes, int anyo) =>
        Activo &&
        new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
        (FechaFin is null || new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1));
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: errores de tipo en `Services/PrevisionService.cs` (usa `rec.FechaFin.Ticks` y `ultimoDia < rec.FechaFin` sobre un `DateTime?`, todavía no actualizado). Esto es esperado en este paso — se corrige en la Tarea 2. Si prefieres un build limpio en cada paso, puedes hacer las Tareas 1 y 2 en el mismo commit; en ese caso, salta la verificación intermedia y compila al final de la Tarea 2.

- [ ] **Step 3: Commit**

```bash
git add Models/MovimientoRecurrente.cs
git commit -m "feat: hacer FechaFin opcional en MovimientoRecurrente"
```

---

### Task 2: Ajustar cálculos en `PrevisionService`

**Files:**
- Modify: `Services/PrevisionService.cs`

**Interfaces:**
- Consumes: `MovimientoRecurrente.FechaFin : DateTime?` (Tarea 1).

- [ ] **Step 1: Ajustar `OcurrenciasEnMes` (frecuencia semanal)**

Cambiar:

```csharp
        var hasta      = ultimoDia < rec.FechaFin    ? ultimoDia : rec.FechaFin;
```

por:

```csharp
        var hasta      = rec.FechaFin is null || ultimoDia < rec.FechaFin.Value ? ultimoDia : rec.FechaFin.Value;
```

- [ ] **Step 2: Ajustar el horizonte de generación en `GenerarMovimientosRecurrentesAsync`**

Cambiar:

```csharp
            var limite = new DateTime(Math.Min(finHorizonte.Ticks, rec.FechaFin.Ticks));
```

por:

```csharp
            var limite = rec.FechaFin is null || finHorizonte <= rec.FechaFin.Value
                ? finHorizonte
                : rec.FechaFin.Value;
```

- [ ] **Step 3: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 4: Commit**

```bash
git add Services/PrevisionService.cs
git commit -m "feat: tratar FechaFin nula como sin limite en PrevisionService"
```

---

### Task 3: Checkbox "Sin fecha de fin" en `Recurrentes.razor`

**Files:**
- Modify: `Pages/Recurrentes.razor`

**Interfaces:**
- Consumes: `MovimientoRecurrente.FechaFin : DateTime?` (Tarea 1); `_nuevo : MovimientoRecurrente` (ya existe en este archivo).
- Produces: propiedad auxiliar `SinFechaFin : bool` (no persiste como campo — envuelve `_nuevo.FechaFin`).

- [ ] **Step 1: Actualizar el texto de la lista (aparece dos veces: Ingresos fijos y Gastos fijos)**

Cambiar (las dos apariciones, una en cada sección):

```razor
                        <small class="text-muted">@DescripcionFrecuencia(rec) · hasta @rec.FechaFin.ToString("MM/yyyy")</small>
```

por:

```razor
                        <small class="text-muted">@DescripcionFrecuencia(rec) · @(rec.FechaFin.HasValue ? $"hasta {rec.FechaFin.Value:MM/yyyy}" : "indefinido")</small>
```

(Es el mismo texto exacto en ambas secciones — reemplaza las dos apariciones idénticas.)

- [ ] **Step 2: Añadir el checkbox y ocultar el campo "Hasta" cuando esté marcado**

Cambiar:

```razor
                    <div class="mb-2">
                        <label class="form-label small">Hasta (fecha fin)</label>
                        <input class="form-control" type="date" @bind="_nuevo.FechaFin" />
                    </div>
```

por:

```razor
                    <div class="form-check mb-2">
                        <input class="form-check-input" type="checkbox" id="sinFechaFin" @bind="SinFechaFin" />
                        <label class="form-check-label small" for="sinFechaFin">Sin fecha de fin</label>
                    </div>
                    @if (!SinFechaFin)
                    {
                        <div class="mb-2">
                            <label class="form-label small">Hasta (fecha fin)</label>
                            <input class="form-control" type="date" @bind="_nuevo.FechaFin" />
                        </div>
                    }
```

- [ ] **Step 3: Añadir la propiedad auxiliar `SinFechaFin` en el bloque `@code`**

En `Pages/Recurrentes.razor`, dentro de `@code`, junto a los demás campos/propiedades (por ejemplo, justo antes de `private async Task OnInitializedAsync()`), añadir:

```csharp
    private bool SinFechaFin
    {
        get => _nuevo.FechaFin is null;
        set => _nuevo.FechaFin = value ? null : DateTime.Today.AddYears(1);
    }
```

- [ ] **Step 4: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 5: Commit**

```bash
git add Pages/Recurrentes.razor
git commit -m "feat: anadir checkbox sin fecha de fin en pagos recurrentes"
```

---

### Task 4: Verificación manual funcional

**Files:** ninguno (solo ejecución de la app)

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run` (o `dotnet watch run`) y abrir Recurrentes en el navegador.

- [ ] **Step 2: Crear uno nuevo sin marcar el checkbox**

Confirmar que el comportamiento es igual que hoy: pide fecha de fin (por defecto hoy + 1 año), editable.

- [ ] **Step 3: Crear uno marcando "Sin fecha de fin"**

El campo "Hasta" desaparece del formulario al marcar el checkbox. Guardar y comprobar que en la lista aparece "... · indefinido" en vez de "... · hasta MM/yyyy".

- [ ] **Step 4: Comprobar que se sigue generando el movimiento**

En el Dashboard, navegar varios meses hacia adelante (usando las flechas de mes) y comprobar que el recurrente indefinido se sigue generando/contando en "Gastos previstos" o "Ingresos previstos" sin límite.

- [ ] **Step 5: Editar uno existente con fecha y marcarle "Sin fecha de fin"**

Abrir un recurrente con fecha de fin real, marcar el checkbox, guardar. Comprobar que pasa a "indefinido" en la lista.

- [ ] **Step 6: Editar uno indefinido y desmarcar el checkbox**

Comprobar que reaparece el campo "Hasta" con una fecha por defecto (hoy + 1 año) lista para editar.

- [ ] **Step 7: Recurrente semanal indefinido**

Crear o editar un recurrente de frecuencia Semanal, marcarlo como indefinido, y comprobar en el Dashboard que se sigue generando cada semana correctamente al navegar varios meses adelante.
