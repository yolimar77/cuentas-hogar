# Desglose "Disponible" vs "Previsión pendiente" en la tarjeta de saldo

## Objetivo

La tarjeta "Saldo total disponible" del Dashboard muestra `Margen` como cifra de "Este mes", que ya resta la previsión pendiente de las categorías con mínimo mensual configurado (`GastosMinimoAjuste`, ver mejora "mínimo mensual configurable"). Esto puede dar una cifra negativa aunque el dinero que ha entrado menos el que realmente se ha gastado siga siendo positivo — porque ese "hueco" de previsión (lo que falta por gastar hasta llegar al mínimo de cada categoría) sigue físicamente en la cuenta hasta que se gasta de verdad. Al ver solo el número final, no se distingue cuánto de la diferencia es gasto ya hecho y cuánto es previsión que aún podría no llegar a materializarse. Esta mejora añade un desglose de esos dos componentes dentro de la propia tarjeta, para que se entienda de dónde sale la cifra y se pueda valorar si tiene sentido usar parte de esa previsión pendiente para otro fin.

## Alcance

- Solo afecta a la tarjeta "Saldo total disponible" del Dashboard (`Pages/Dashboard.razor`).
- No se toca el gráfico "Ahorro últimos 6 meses" (queda exactamente como está hoy, con su propio doble nivel real/ajustado).
- No se toca ningún cálculo existente: `GastosTotales`, `GastosMinimoAjuste`, `Margen`, `SaldoTotal`, `SaldoAcumulado` se quedan igual. Esta mejora solo añade una propiedad de solo lectura nueva y su visualización.
- No afecta a la alerta "superas el X% de tus ingresos previstos" ni a "Gastos con mínimo mensual" ni a "Gastos por categoría".

## Cálculo

`Models/PrevisionMensual.cs` gana una propiedad calculada:

```csharp
public decimal BalanceSinAjuste => IngresosReales - GastosReales;
```

Es el balance real puro del mes (lo que ha entrado menos lo que ya ha salido de verdad), sin restar la previsión pendiente de los mínimos. `IngresosReales` y `GastosReales` ya existen en el modelo y no cambian.

La otra cifra necesaria, `GastosMinimoAjuste` (la previsión pendiente sin gastar), ya existe en el modelo desde la mejora "mínimo mensual configurable" — no requiere ningún cambio.

Relación entre las tres cifras (ya cierta hoy, no es un cálculo nuevo): `Margen == BalanceSinAjuste - GastosMinimoAjuste`.

## Interfaz (`Pages/Dashboard.razor`)

Dentro de la tarjeta "Saldo total disponible", entre la línea "Este mes" y la línea "Acumulado anterior", se añaden dos líneas indentadas un nivel más que las actuales (para reflejar que son el desglose de "Este mes", no hermanas de "Acumulado anterior"):

```razor
<div class="d-flex justify-content-between align-items-center mt-1" style="font-size:0.8rem">
    <div class="text-muted ps-2">├─ Este mes</div>
    <div class="@(_prevision.Margen >= 0 ? "text-success" : "text-danger")">
        @(_prevision.Margen >= 0 ? "+" : "")@_prevision.Margen.ToString("C")
    </div>
</div>
@if (_prevision.GastosMinimoAjuste != 0)
{
    <div class="d-flex justify-content-between align-items-center" style="font-size:0.72rem">
        <div class="text-muted ps-4">│  ├─ Disponible (sin lo reservado)</div>
        <div class="@(_prevision.BalanceSinAjuste >= 0 ? "text-success" : "text-danger")">
            @(_prevision.BalanceSinAjuste >= 0 ? "+" : "")@_prevision.BalanceSinAjuste.ToString("C")
        </div>
    </div>
    <div class="d-flex justify-content-between align-items-center" style="font-size:0.72rem">
        <div class="text-muted ps-4">│  └─ Previsión pendiente (sin gastar)</div>
        <div class="text-danger">
            -@_prevision.GastosMinimoAjuste.ToString("C")
        </div>
    </div>
}
<div class="d-flex justify-content-between align-items-center" style="font-size:0.8rem">
    <div class="text-muted ps-2">└─ Acumulado anterior</div>
    ...
</div>
```

Las dos líneas nuevas solo se muestran cuando `GastosMinimoAjuste != 0` (hay previsión pendiente sin gastar). Si es 0 (no hay categorías con mínimo configurado, o el gasto real ya alcanzó todos los mínimos), `BalanceSinAjuste` y `Margen` coinciden y el desglose no aportaría nada nuevo — la tarjeta se ve exactamente igual que hoy, con una sola línea "Este mes".

`Previsión pendiente` se muestra siempre en rojo (`text-danger`) porque por definición `GastosMinimoAjuste >= 0` (es una resta) — no necesita color condicional.

## Manejo de errores

No se añade manejo de errores nuevo: es una propiedad calculada adicional sobre datos ya cargados (`_prevision`, ya calculado en `Cargar()`), con los mismos supuestos que el resto del método.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Mes con categorías con mínimo no alcanzado (caso de la captura: Alimentación 205,62€/550€, Transporte 76€/300€) → aparecen las dos líneas nuevas: "Disponible (sin lo reservado)" +424,78€ y "Previsión pendiente (sin gastar)" -568,38€, y su suma coincide con "Este mes" (-143,60€).
- Sin categorías con mínimo configurado, o con el gasto real ya igualando o superando todos los mínimos → las dos líneas nuevas no aparecen, la tarjeta se ve igual que antes de esta mejora.
- Registrar gastos hasta alcanzar los mínimos → `GastosMinimoAjuste` llega a 0 y las líneas nuevas desaparecen al recargar.
- Navegar a un mes distinto (con las flechas) recalcula correctamente el desglose para el mes visto.
- El gráfico "Ahorro últimos 6 meses" no cambia en ningún caso.
