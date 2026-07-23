# Mínimo mensual configurable para gastos variables

## Objetivo

Hay categorías de gasto obligatorias pero de importe variable (alimentación, gasolina...) que no encajan como "gastos recurrentes" (importe fijo) y hoy no se tienen en cuenta al calcular cuánto dinero queda disponible en el mes, ni en la previsión de gastos, hasta que el usuario ya los ha registrado como movimientos reales. Esta mejora permite configurar un mínimo mensual por categoría, de forma que ese mínimo se tenga en cuenta como suelo tanto en la previsión de gastos como en el dinero disponible, incluso antes de que el gasto real se haya producido.

## Alcance

- Solo categorías de tipo **Gasto** (igual que en la comparativa mensual).
- El mínimo es opcional por categoría; si no está configurado, el comportamiento es exactamente el actual (sin cambios).
- Sin lógica de arrastre a fin de mes: el saldo acumulado que pasa al mes siguiente ya se calcula con gastos reales (`SaldoAcumulado`, sin modificar), así que un mínimo no alcanzado no necesita tratamiento especial — se libera automáticamente al cerrar el mes.
- Se integra en `Services/PrevisionService.cs` (no un servicio nuevo): es una extensión directa de la previsión mensual que ya calcula ese servicio, a diferencia de la comparativa mensual (que sí era un concepto distinto).

## Arquitectura y modelo de datos

`Models/Categoria.cs` gana un campo opcional:

```csharp
public decimal? MinimoMensual { get; set; }
```

`null` = sin suelo configurado (comportamiento actual). Solo relevante cuando `Tipo == TipoMovimiento.Gasto`.

`Models/PrevisionMensual.cs` gana:

```csharp
public List<MinimoCategoria> DetalleMinimos { get; set; } = [];
```

con un nuevo record (en el mismo archivo o en `Models/`):

```csharp
public record MinimoCategoria(string Nombre, string Icono, decimal GastoReal, decimal Minimo)
{
    public decimal Objetivo => Math.Max(GastoReal, Minimo);
    public bool Alcanzado => GastoReal >= Minimo;
}
```

Las propiedades existentes `GastosTotales` y `Margen` pasan de ser directas a derivarse también de `DetalleMinimos`:

```csharp
public decimal GastosTotales => GastosRecurrentes + DetalleMinimos.Sum(d => d.Objetivo);
public decimal GastosMinimoAjuste => DetalleMinimos.Sum(d => Math.Max(0, d.Minimo - d.GastoReal));
public decimal Margen => IngresosReales - GastosReales - GastosMinimoAjuste;
```

`GastosReales` **no cambia** — sigue siendo el gasto real literal del mes, sin ajustar. Solo "Gastos previstos" (`GastosTotales`) y "dinero disponible" (`Margen`/`SaldoTotal`) incorporan el mínimo, tal y como se acordó explícitamente (afecta a ambos sitios).

`GastosMinimoAjuste` no duplica el gasto real ya contado en `GastosReales`: solo suma la diferencia no cubierta (`Minimo - GastoReal`) para categorías por debajo de su mínimo. Para una categoría que ya superó su mínimo, esa diferencia es 0, así que `GastosReales + GastosMinimoAjuste` equivale exactamente a "para cada categoría con mínimo, usar `max(GastoReal, Minimo)`; para el resto, usar el gasto real tal cual" — sin restar ni recalcular `GastosReales` por categoría.

## Cálculo (dentro de `PrevisionService.CalcularAsync`)

El método ya carga `movimientos`; se añade la carga de `categorias` (hoy no se cargan en este método) y el cálculo de `DetalleMinimos`.

**Duplicados de categoría:** igual que se descubrió y corrigió en la comparativa mensual, dos dispositivos pueden crear una categoría con el mismo nombre pero distinto Id antes de sincronizar. Para que el mínimo configurado en un Id no infravalore el gasto real repartido entre Ids duplicados, se agrupa por **nombre de categoría resuelto**, no por Id:

```csharp
var categorias = await db.ObtenerCategoriasAsync();
var categoriasGastoPorNombre = categorias
    .Where(c => c.Tipo == TipoMovimiento.Gasto)
    .GroupBy(c => c.Nombre);

var detalleMinimos = new List<MinimoCategoria>();
foreach (var grupo in categoriasGastoPorNombre)
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
```

(`movMes` ya existe en el método actual: son los movimientos del mes visto.) El resultado se asigna a `PrevisionMensual.DetalleMinimos`.

El cálculo es puramente "mínimo configurado vs. gasto real del mes visto" — no depende de historial de meses anteriores (a diferencia de la comparativa mensual) y se recalcula en cada llamada a `CalcularAsync`, así que cambiar el mínimo de una categoría afecta de inmediato al mes que se esté viendo.

## Interfaz

**`Pages/Configuracion.razor`** — modal de categoría: nuevo campo opcional, visible solo si `Tipo == Gasto`:

```razor
@if (_nuevaCategoria.Tipo == TipoMovimiento.Gasto)
{
    <div class="mb-2">
        <label class="form-label small">Mínimo mensual (€) <span class="text-muted">(opcional)</span></label>
        <input class="form-control" type="number" step="0.01" min="0"
               value="@_nuevaCategoria.MinimoMensual"
               @onchange="@(e => _nuevaCategoria.MinimoMensual = decimal.TryParse(e.Value?.ToString(), out var v) && v > 0 ? v : null)" />
    </div>
}
```

**`Pages/Dashboard.razor`** — nueva sección justo debajo de las tarjetas de resumen (Ingresos/Gastos previstos/reales) y antes del gráfico "Ahorro últimos 6 meses":

```
Gastos con mínimo mensual
🍽️ Alimentación   180€ / 300€   [barra de progreso]
⛽ Gasolina        90€ / 120€    [barra de progreso]
```

Si `_prevision.DetalleMinimos` está vacío, la sección no se muestra (a diferencia de "Lo más destacado", aquí no hace falta un texto de "vacío": es una función opcional que muchos usuarios no configurarán nunca).

## Manejo de errores

No se añade manejo de errores nuevo: es un cálculo derivado adicional dentro de `CalcularAsync`, con los mismos supuestos que el resto de `PrevisionService` (los errores de `Db` se propagan igual que hoy). Guardar `MinimoMensual` reutiliza `GuardarCategoriaAsync`/`GuardarCategoria`, que ya tienen su propio manejo de errores en `Configuracion.razor`.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Categoría sin mínimo configurado → previstos/disponible sin cambios respecto a hoy.
- Categoría con mínimo, gasto real por debajo → previstos y disponible usan el mínimo.
- Categoría con mínimo, gasto real por encima → previstos y disponible usan el gasto real.
- Varias categorías con mínimo a la vez → se suman correctamente.
- Mes siguiente sin haber llegado al mínimo → el saldo disponible ya refleja el gasto real de ese mes cerrado, sin arrastre especial.
- Categorías duplicadas (mismo nombre, distinto Id) con mínimo en una y gasto en la otra → se combinan en una sola línea del desglose.
- Cambiar el mínimo de una categoría se refleja al momento al recargar/navegar el Dashboard.
