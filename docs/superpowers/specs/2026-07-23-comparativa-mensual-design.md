# Comparativa mensual por categoría — "Lo más destacado"

## Objetivo

Responder de un vistazo a "¿dónde se ha ido más dinero o se ha ahorrado más este mes?" mostrando, en el Dashboard, las categorías de gasto cuya diferencia respecto a su media histórica sea significativa — sin obligar al usuario a leer una tabla completa mes a mes.

## Alcance

- Solo categorías de tipo **Gasto** (no se compara Ingresos).
- Se integra en `Pages/Dashboard.razor`, como sección nueva al final de la página, justo antes del enlace "Ver todos los movimientos".
- No incluye configuración de umbrales desde Ajustes (valores fijos en código).
- No incluye tests automatizados (el repo no tiene proyecto de tests; verificación manual).

## Arquitectura

Nuevo servicio `Services/ComparativaService.cs`, siguiendo el mismo patrón que `PrevisionService`, `SyncService` y `DriveService` (una responsabilidad por servicio):

```csharp
public class ComparativaService(LocalDbService db)
{
    public async Task<List<CategoriaDestacada>> ObtenerDestacadosAsync(int mes, int anyo)
}
```

Modelo de resultado (en `Models/`, junto a `PrevisionMensual`):

```csharp
public record CategoriaDestacada(
    string Nombre,
    string Icono,
    decimal TotalActual,
    decimal MediaAnterior,
    decimal DiferenciaImporte,
    decimal? DiferenciaPorcentaje); // null => sin historial previo ("nuevo gasto")
```

`Dashboard.razor` inyecta `ComparativaService` (`@inject ComparativaService Comparativa`) y lo invoca dentro de `Cargar()`, junto a `Prevision.CalcularAsync(...)`, guardando el resultado en un campo `_destacados`.

## Flujo de datos / cálculo

Dentro de `ObtenerDestacadosAsync(mes, anyo)`:

1. Cargar todos los movimientos (`db.ObtenerMovimientosAsync()`) y categorías (`db.ObtenerCategoriasAsync()`).
2. Definir la ventana de comparación: los 6 meses **anteriores** al mes visto (`mes`/`anyo`), sin incluir el mes actual — mismo criterio relativo que ya usa `_historial` en `Dashboard.razor` para el gráfico de balance.
3. Para cada categoría de tipo Gasto que tenga movimientos en el mes visto **o** en la ventana anterior:
   - `TotalActual` = suma de gastos de esa categoría en el mes visto.
   - `MediaAnterior` = suma de gastos de esa categoría en los meses anteriores disponibles ÷ número de meses disponibles. "Meses disponibles" son los meses de la ventana de 6 que ya han transcurrido desde el primer movimiento registrado en toda la app (para no penalizar instalaciones recientes con poco historial global); dentro de esos meses, si la categoría concreta no tuvo gasto en alguno de ellos, ese mes cuenta como 0€ en la suma (una categoría usada de forma esporádica debe reflejar ese patrón en su media, no solo promediarse sobre los meses en que se usó).
   - `DiferenciaImporte` = `TotalActual - MediaAnterior`.
   - `DiferenciaPorcentaje` = `DiferenciaImporte / MediaAnterior * 100` si `MediaAnterior > 0`; si no, `null`.
4. Filtro de inclusión — se incluye la categoría si se cumple alguna de estas condiciones:
   - `MediaAnterior > 0` y `|DiferenciaPorcentaje| ≥ 25` y `|DiferenciaImporte| ≥ 20€`.
   - `MediaAnterior == 0` (sin gasto en los meses anteriores) y `TotalActual ≥ 20€` → se marca como "nuevo gasto".
   - `MediaAnterior > 0` y `TotalActual == 0` (categoría que dejó de tener gasto este mes) → se evalúa igual que el primer caso (será -100%, normalmente superará el umbral si `MediaAnterior` era relevante).
5. Ordenar por `|DiferenciaImporte|` descendente. Sin límite de elementos (se muestran todas las que superen el umbral).

## Interfaz

Nueva sección en `Dashboard.razor`, título "Lo más destacado este mes", con una línea por categoría destacada:

- 🔺 si `DiferenciaImporte > 0`, 🔻 si `< 0`.
- Formato normal: `{icono categoría} {Nombre}   {TotalActual:C}   {+/-}{DiferenciaImporte:C} · {+/-}{DiferenciaPorcentaje:F0}% vs. media`.
- Si `DiferenciaPorcentaje` es `null` (categoría nueva): `{icono} {Nombre}   {TotalActual:C}   nuevo gasto este mes`.
- Si `TotalActual == 0` (categoría que dejó de tener gasto): `{icono} {Nombre}   0 €   sin gasto este mes (antes ~{MediaAnterior:C}/mes)`.
- Si la lista está vacía: texto discreto `"Sin cambios destacables este mes."`, siguiendo el mismo patrón que el texto vacío ya usado en "Gastos por categoría" ("Sin gastos registrados este mes.").

## Manejo de errores

No se añade manejo de errores nuevo: es un cálculo local de solo lectura sobre datos ya cargados por `LocalDbService`, con los mismos supuestos que el resto de `Dashboard.razor`. Cualquier fallo de `db` se propaga igual que ya ocurre hoy en `Cargar()`.

## Testing

El repositorio no tiene proyecto de tests automatizados. Verificación manual tras implementar:

- Mes con una categoría claramente por encima de su media (≥25% y ≥20€) → aparece con 🔺.
- Mes con una categoría claramente por debajo (≥25% y ≥20€) → aparece con 🔻.
- Categoría con desviación pequeña (por debajo de umbral) → no aparece.
- Categoría nueva sin historial previo → aparece como "nuevo gasto este mes".
- Categoría con gasto habitual que este mes es 0€ → aparece como "sin gasto este mes".
- Mes sin ninguna categoría destacada → se muestra el texto de lista vacía.
- Navegación entre meses (botones anterior/siguiente) recalcula correctamente la ventana de 6 meses relativa al mes visto.
