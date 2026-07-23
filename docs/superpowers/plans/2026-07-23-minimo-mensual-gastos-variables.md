# Mínimo mensual configurable para gastos variables — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir configurar un mínimo mensual opcional por categoría de gasto (p. ej. Alimentación, Gasolina), de forma que ese mínimo se tenga en cuenta como suelo tanto en "Gastos previstos" como en el "dinero disponible" (Margen/SaldoTotal) del Dashboard, incluso antes de que el gasto real alcance ese importe.

**Architecture:** Extensión directa de `Services/PrevisionService.cs` (no un servicio nuevo): el mínimo es parte del mismo cálculo de previsión mensual que ese servicio ya hace. `Models/Categoria.cs` gana el campo de configuración; `Models/PrevisionMensual.cs` gana el detalle por categoría y deriva de él las cifras ya existentes de previstos/margen.

**Tech Stack:** Blazor WebAssembly (.NET), C# records, LINQ sobre listas en memoria.

## Global Constraints

- Solo categorías de tipo Gasto; el mínimo no aplica a Ingresos.
- El mínimo es opcional (`decimal? MinimoMensual`); `null` = sin suelo = comportamiento actual sin cambios.
- `GastosReales` no se modifica — sigue siendo el gasto real literal del mes. Solo `GastosTotales` (previstos) y `Margen`/`SaldoTotal` (disponible) incorporan el mínimo.
- Fórmulas exactas (de la spec):
  - `GastosTotales = GastosRecurrentes + DetalleMinimos.Sum(d => Math.Max(d.GastoReal, d.Minimo))`
  - `GastosMinimoAjuste = DetalleMinimos.Sum(d => Math.Max(0, d.Minimo - d.GastoReal))`
  - `Margen = IngresosReales - GastosReales - GastosMinimoAjuste`
- Sin lógica de arrastre a fin de mes: `SaldoAcumulado` ya usa gastos reales, así que un mínimo no alcanzado se libera solo al cerrar el mes — no se añade ningún mecanismo adicional.
- Las categorías con el mismo nombre pero distinto Id (herencia de creación en dos dispositivos antes de sincronizar) deben tratarse como una sola categoría: se agrupa por **nombre resuelto**, no por Id, igual que ya hace `ComparativaService` y `_gastosPorCategoria` en `Dashboard.razor`.
- No hay proyecto de tests automatizados en el repo — verificación manual únicamente.
- No se añade ninguna sección "vacía" para el desglose de mínimos en el Dashboard: si no hay categorías con mínimo configurado, la sección entera no se muestra.

---

### Task 1: Campo `MinimoMensual` en `Categoria`

**Files:**
- Modify: `Models/Categoria.cs`

**Interfaces:**
- Produces: `Categoria.MinimoMensual : decimal?` (nullable, `null` = sin mínimo configurado).

- [ ] **Step 1: Añadir el campo**

Reemplazar el contenido de `Models/Categoria.cs` por:

```csharp
namespace HomeAccounts.Models;

public class Categoria
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public TipoMovimiento Tipo { get; set; }
    public string Icono { get; set; } = "💰";
    public decimal? MinimoMensual { get; set; }
    public DateTime ModificadoEn { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Models/Categoria.cs
git commit -m "feat: añadir MinimoMensual opcional a Categoria"
```

---

### Task 2: `MinimoCategoria` y campos derivados en `PrevisionMensual`

**Files:**
- Modify: `Models/PrevisionMensual.cs`

**Interfaces:**
- Produces: `record MinimoCategoria(string Nombre, string Icono, decimal GastoReal, decimal Minimo)` con `Objetivo` (`Math.Max(GastoReal, Minimo)`) y `Alcanzado` (`GastoReal >= Minimo`) calculados; `PrevisionMensual.DetalleMinimos : List<MinimoCategoria>` (settable, default `[]`); `PrevisionMensual.GastosMinimoAjuste : decimal` (calculada); `GastosTotales` y `Margen` recalculadas para incorporar `DetalleMinimos`.
- Consumes (Tarea 3 la rellenará): nada nuevo — este tipo no depende de `Categoria` ni de `PrevisionService`.

- [ ] **Step 1: Reemplazar el contenido completo del archivo**

Reemplazar `Models/PrevisionMensual.cs` por:

```csharp
namespace HomeAccounts.Models;

public class PrevisionMensual
{
    public int Mes { get; set; }
    public int Anyo { get; set; }

    // Plan mensual basado en recurrentes configurados
    public decimal IngresosRecurrentes { get; set; }
    public decimal GastosRecurrentes { get; set; }

    // Lo que realmente ha ocurrido este mes (todos los movimientos)
    public decimal IngresosReales { get; set; }
    public decimal GastosReales { get; set; }

    // Categorías de gasto variable con un mínimo mensual configurado
    public List<MinimoCategoria> DetalleMinimos { get; set; } = [];

    // Aliases para las tarjetas "previstos" del Dashboard
    public decimal IngresosTotales => IngresosRecurrentes;
    public decimal GastosTotales => GastosRecurrentes + DetalleMinimos.Sum(d => d.Objetivo);

    // Suma de márgenes reales de todos los meses anteriores al visualizado
    public decimal SaldoAcumulado { get; set; }

    // Diferencia no cubierta por el gasto real en categorías con mínimo (0 si ya lo alcanzaron)
    public decimal GastosMinimoAjuste => DetalleMinimos.Sum(d => Math.Max(0, d.Minimo - d.GastoReal));

    // Balance del mes en curso (sin acumulado)
    public decimal Margen => IngresosReales - GastosReales - GastosMinimoAjuste;

    // Saldo total = margen del mes + lo acumulado de meses anteriores
    public decimal SaldoTotal => Margen + SaldoAcumulado;

    public decimal PorcentajeGasto => IngresosReales == 0
        ? (GastosReales == 0 ? 0 : 100)
        : Math.Round(GastosReales / IngresosReales * 100, 1);

    public NivelAlerta Nivel => PorcentajeGasto switch
    {
        < 80 => NivelAlerta.Ok,
        < 100 => NivelAlerta.Aviso,
        _ => NivelAlerta.Peligro
    };
}

public enum NivelAlerta { Ok, Aviso, Peligro }

public record MinimoCategoria(string Nombre, string Icono, decimal GastoReal, decimal Minimo)
{
    public decimal Objetivo => Math.Max(GastoReal, Minimo);
    public bool Alcanzado => GastoReal >= Minimo;
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores (nada llena `DetalleMinimos` todavía — queda vacío por defecto, así que `GastosTotales`/`Margen` se comportan igual que antes hasta la Tarea 3).

- [ ] **Step 3: Commit**

```bash
git add Models/PrevisionMensual.cs
git commit -m "feat: añadir MinimoCategoria y ajuste de minimos en PrevisionMensual"
```

---

### Task 3: Cálculo de `DetalleMinimos` en `PrevisionService`

**Files:**
- Modify: `Services/PrevisionService.cs` (método `CalcularAsync`)

**Interfaces:**
- Consumes: `LocalDbService.ObtenerCategoriasAsync() : Task<List<Categoria>>` (ya existe, usado por otros servicios); `Categoria.Nombre/Tipo/Icono/MinimoMensual` (Tarea 1); `MinimoCategoria` (Tarea 2); `Movimiento.CategoriaId/Tipo/Importe` (ya existen).
- Produces: `PrevisionMensual.DetalleMinimos` poblado por `CalcularAsync`, consumido por `Dashboard.razor` en la Tarea 5.

- [ ] **Step 1: Cargar categorías y calcular `detalleMinimos`, e incluirlo en el resultado**

En `Services/PrevisionService.cs`, cambiar:

```csharp
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();

        var inicioMes     = new DateTime(anyo, mes, 1);
        var movMes        = movimientos.Where(m => m.Fecha.Month == mes && m.Fecha.Year == anyo);
        var movAnteriores = movimientos.Where(m => m.Fecha < inicioMes);
        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        return new PrevisionMensual
        {
            Mes  = mes,
            Anyo = anyo,
            IngresosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Ingreso)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            GastosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Gasto)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            IngresosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Ingreso)
                .Sum(m => m.Importe),
            GastosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto)
                .Sum(m => m.Importe),
            SaldoAcumulado =
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe) -
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe),
        };
    }
```

por:

```csharp
    public async Task<PrevisionMensual> CalcularAsync(int mes, int anyo)
    {
        var movimientos = await db.ObtenerMovimientosAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var categorias  = await db.ObtenerCategoriasAsync();

        var inicioMes     = new DateTime(anyo, mes, 1);
        var movMes        = movimientos.Where(m => m.Fecha.Month == mes && m.Fecha.Year == anyo);
        var movAnteriores = movimientos.Where(m => m.Fecha < inicioMes);
        var recActivosMes = recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo));

        // Agrupar por nombre resuelto para que categorías duplicadas (mismo nombre,
        // distinto Id, herencia de creación en dos dispositivos antes de sincronizar)
        // compartan un único mínimo y una única suma de gasto real.
        var detalleMinimos = new List<MinimoCategoria>();
        foreach (var grupo in categorias.Where(c => c.Tipo == TipoMovimiento.Gasto).GroupBy(c => c.Nombre))
        {
            var minimo = grupo.Select(c => c.MinimoMensual).FirstOrDefault(m => m is > 0);
            if (minimo is null) continue;

            var idsDelGrupo = grupo.Select(c => c.Id).ToHashSet();
            var gastoReal = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto && idsDelGrupo.Contains(m.CategoriaId))
                .Sum(m => m.Importe);

            var icono = grupo.Select(c => c.Icono).FirstOrDefault() ?? "•";
            detalleMinimos.Add(new MinimoCategoria(grupo.Key, icono, gastoReal, minimo.Value));
        }

        return new PrevisionMensual
        {
            Mes  = mes,
            Anyo = anyo,
            IngresosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Ingreso)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            GastosRecurrentes = recActivosMes
                .Where(r => r.Tipo == TipoMovimiento.Gasto)
                .Sum(r => r.Importe * OcurrenciasEnMes(r, mes, anyo)),
            IngresosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Ingreso)
                .Sum(m => m.Importe),
            GastosReales = movMes
                .Where(m => m.Tipo == TipoMovimiento.Gasto)
                .Sum(m => m.Importe),
            DetalleMinimos = detalleMinimos,
            SaldoAcumulado =
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe) -
                movAnteriores.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe),
        };
    }
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Services/PrevisionService.cs
git commit -m "feat: calcular DetalleMinimos por categoria en PrevisionService"
```

---

### Task 4: Campo "Mínimo mensual" en `Configuracion.razor`

**Files:**
- Modify: `Pages/Configuracion.razor`

**Interfaces:**
- Consumes: `Categoria.MinimoMensual : decimal?` (Tarea 1); `_nuevaCategoria : Categoria` (ya existe en este archivo); `GuardarCategoria()` (ya existe, no se modifica — ya persiste el objeto `Categoria` completo vía `Db.GuardarCategoriaAsync`).

- [ ] **Step 1: Añadir el campo al modal de categoría**

En `Pages/Configuracion.razor`, dentro del modal de categoría (`@if (_mostrarCategoria)`), cambiar:

```razor
                    <input class="form-control mb-2" @bind="_nuevaCategoria.Nombre" @bind:event="oninput" placeholder="Nombre" />
                    <input class="form-control" @bind="_nuevaCategoria.Icono" placeholder="Emoji (ej: 🛒)" />
                    @if (_errorCategoria is not null)
                    {
                        <p class="text-danger small mt-2 mb-0">@_errorCategoria</p>
                    }
```

por:

```razor
                    <input class="form-control mb-2" @bind="_nuevaCategoria.Nombre" @bind:event="oninput" placeholder="Nombre" />
                    <input class="form-control mb-2" @bind="_nuevaCategoria.Icono" placeholder="Emoji (ej: 🛒)" />
                    @if (_nuevaCategoria.Tipo == TipoMovimiento.Gasto)
                    {
                        <div class="mb-2">
                            <label class="form-label small">Mínimo mensual (€) <span class="text-muted">(opcional)</span></label>
                            <input class="form-control" type="number" step="0.01" min="0"
                                   value="@_nuevaCategoria.MinimoMensual"
                                   @onchange="@(e => _nuevaCategoria.MinimoMensual = decimal.TryParse(e.Value?.ToString(), out var v) && v > 0 ? v : null)" />
                        </div>
                    }
                    @if (_errorCategoria is not null)
                    {
                        <p class="text-danger small mt-2 mb-0">@_errorCategoria</p>
                    }
```

(Nota: se añade `mb-2` al input de `Icono`, que antes no lo tenía, para mantener el espaciado consistente ahora que hay un campo debajo.)

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Pages/Configuracion.razor
git commit -m "feat: añadir campo minimo mensual al formulario de categoria"
```

---

### Task 5: Sección "Gastos con mínimo mensual" en el Dashboard

**Files:**
- Modify: `Pages/Dashboard.razor` (solo marcado — no toca el bloque `@code`, ya que `_prevision.DetalleMinimos` ya está disponible vía el campo `_prevision` existente, poblado en `Cargar()` por la Tarea 3)

**Interfaces:**
- Consumes: `PrevisionMensual.DetalleMinimos : List<MinimoCategoria>` (Tareas 2-3); `MinimoCategoria.Nombre/Icono/GastoReal/Minimo/Alcanzado` (Tarea 2).

- [ ] **Step 1: Añadir la sección justo debajo de las tarjetas de resumen**

En `Pages/Dashboard.razor`, entre el cierre de las tarjetas de resumen (`</div>` que cierra `<div class="row g-2 mb-4">`) y el comentario `<!-- Gráfico + categorías: columnas en PC, apilado en móvil -->`, añadir:

```razor
    @if (_prevision is not null && _prevision.DetalleMinimos.Count > 0)
    {
        <div class="mb-4">
            <h6 class="text-muted mb-2">Gastos con mínimo mensual</h6>
            @foreach (var d in _prevision.DetalleMinimos)
            {
                <div class="mb-2">
                    <div class="d-flex justify-content-between align-items-center mb-1">
                        <span class="small">@d.Icono @d.Nombre</span>
                        <span class="small @(d.Alcanzado ? "text-success" : "text-muted")">
                            @d.GastoReal.ToString("C") / @d.Minimo.ToString("C")
                        </span>
                    </div>
                    <div class="progress" style="height:5px">
                        <div class="progress-bar @(d.Alcanzado ? "bg-success" : "bg-warning")"
                             style="width:@(Math.Min(d.Minimo > 0 ? d.GastoReal / d.Minimo * 100 : 100, 100).ToString("F1", CultureInfo.InvariantCulture))%"></div>
                    </div>
                </div>
            }
        </div>
    }
```

Es decir, el archivo queda así en ese punto (fragmento, mostrando dónde encaja):

```razor
    </div>

    @if (_prevision is not null && _prevision.DetalleMinimos.Count > 0)
    {
        <div class="mb-4">
            <h6 class="text-muted mb-2">Gastos con mínimo mensual</h6>
            @foreach (var d in _prevision.DetalleMinimos)
            {
                <div class="mb-2">
                    <div class="d-flex justify-content-between align-items-center mb-1">
                        <span class="small">@d.Icono @d.Nombre</span>
                        <span class="small @(d.Alcanzado ? "text-success" : "text-muted")">
                            @d.GastoReal.ToString("C") / @d.Minimo.ToString("C")
                        </span>
                    </div>
                    <div class="progress" style="height:5px">
                        <div class="progress-bar @(d.Alcanzado ? "bg-success" : "bg-warning")"
                             style="width:@(Math.Min(d.Minimo > 0 ? d.GastoReal / d.Minimo * 100 : 100, 100).ToString("F1", CultureInfo.InvariantCulture))%"></div>
                    </div>
                </div>
            }
        </div>
    }

    <!-- Gráfico + categorías: columnas en PC, apilado en móvil -->
    <div class="row g-4">
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 3: Commit**

```bash
git add Pages/Dashboard.razor
git commit -m "feat: mostrar gastos con minimo mensual en el Dashboard"
```

---

### Task 6: Verificación manual funcional

**Files:** ninguno (solo ejecución de la app)

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run` (o `dotnet watch run`) y abrir la app en el navegador.

- [ ] **Step 2: Configurar un mínimo**

En Configuración > Categorías, editar (o crear) una categoría de tipo Gasto (p. ej. "Alimentación") y ponerle un mínimo mensual, p. ej. 300€. Confirmar que el campo solo aparece para categorías de tipo Gasto.

- [ ] **Step 3: Comprobar gasto real por debajo del mínimo**

Sin registrar gasto (o con un gasto menor a 300€) en esa categoría este mes: en el Dashboard, "Gastos previstos" y el "dinero disponible" deben reflejar el mínimo (300€), no el gasto real. La nueva sección "Gastos con mínimo mensual" debe mostrar la barra en naranja/aviso.

- [ ] **Step 4: Comprobar gasto real por encima del mínimo**

Registrar movimientos en esa categoría hasta superar 300€ (p. ej. 350€): "Gastos previstos" y "dinero disponible" deben reflejar 350€ (el gasto real), no el mínimo. La barra debe pasar a verde/alcanzado.

- [ ] **Step 5: Varias categorías con mínimo**

Configurar un mínimo también en otra categoría (p. ej. "Gasolina", 120€) y comprobar que ambas aparecen en la sección y que "Gastos previstos"/"dinero disponible" suman correctamente ambos ajustes.

- [ ] **Step 6: Categoría sin mínimo**

Confirmar que una categoría sin mínimo configurado no aparece en absoluto en la nueva sección, y que su gasto real sigue contando normalmente en el resto del Dashboard.

- [ ] **Step 7: Mes siguiente sin haber llegado al mínimo**

Con un mes donde no se llegó al mínimo, navegar al mes siguiente y comprobar que el "Saldo acumulado" ya refleja el gasto real de ese mes cerrado (no el mínimo), sin ningún ajuste adicional.

- [ ] **Step 8: Sección vacía**

Sin ninguna categoría con mínimo configurado (o quitando los mínimos configurados), confirmar que la sección "Gastos con mínimo mensual" no se muestra en absoluto (no debe aparecer ni un texto de "vacío").

- [ ] **Step 9: Categorías duplicadas (mismo nombre, distinto Id)**

Si hay datos de prueba con dos categorías del mismo nombre y tipo Gasto pero distinto Id (p. ej. por sincronización previa entre dispositivos), configurar el mínimo en una de ellas y registrar gasto en la otra: la sección debe mostrar una única línea combinada para ese nombre, con el gasto real sumado de ambos Ids y el mínimo aplicado correctamente. Si no hay datos así disponibles, se puede omitir esta comprobación y confiar en la revisión de código de la Tarea 3.

- [ ] **Step 10: Cambio de mínimo se refleja al momento**

Editar el mínimo de una categoría ya configurada (subirlo o bajarlo) y volver al Dashboard (o navegar de mes y volver): confirmar que "Gastos previstos", "dinero disponible" y la barra de la sección reflejan el nuevo valor inmediatamente, sin necesidad de recargar la página completa.
