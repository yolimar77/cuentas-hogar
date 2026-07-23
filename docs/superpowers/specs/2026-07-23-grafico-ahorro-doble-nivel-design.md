# Doble nivel en el gráfico "Ahorro últimos 6 meses"

## Objetivo

El gráfico "Ahorro últimos 6 meses" del Dashboard calcula el balance de cada mes con gasto real puro, sin tener en cuenta el ajuste de mínimos mensuales configurados (`GastosMinimoAjuste`, ver mejora "mínimo mensual configurable"). Esto hace que la barra del mes en curso no coincida con la cifra de "Margen disponible este mes" mostrada justo encima, cuando hay categorías con mínimo aún no alcanzado. Esta mejora resuelve la incoherencia mostrando, solo para el mes que se está viendo, dos niveles superpuestos en la misma barra: el ahorro real puro y el ahorro ya ajustado con los mínimos pendientes.

## Alcance

- Solo afecta a la barra del mes que se está viendo actualmente (la última del histórico de 6 meses, la de más a la derecha). Los otros 5 meses son siempre anteriores a ese punto de referencia — ya están cerrados, así que no tienen mínimo pendiente y se quedan como hoy (una sola barra).
- Si no hay categorías con mínimo configurado, o si el gasto real ya alcanzó o superó todos los mínimos aplicables (`GastosMinimoAjuste == 0`), las dos cifras coinciden y se ve una sola barra, exactamente como hoy — no hace falta ninguna condición especial de "vacío" porque el propio cálculo ya converge.
- No se toca el resto del Dashboard (tarjetas de resumen, "Gastos por categoría", "Lo más destacado", "Gastos con mínimo mensual" — esa sección ya existe y no cambia).
- Las etiquetas numéricas debajo de las barras siguen mostrando el balance real (como hoy, sin cambios) — la cifra ajustada ya se ve arriba en la tarjeta "Saldo total disponible"/"Margen", y visualmente en la propia altura de la barra opaca.

## Diseño visual

Dos barras superpuestas, del mismo ancho, ambas ancladas a la base del contenedor (`position:absolute; bottom:0`):

- **Barra real** (más alta cuando hay mínimos pendientes): mismo color verde/rojo según su signo, pero con opacidad reducida (`opacity:0.35`).
- **Barra ajustada** (`= _prevision.Margen`, coincide con la cifra mostrada arriba): color sólido normal, dibujada encima.

Como comparten ancho y posición horizontal, la parte inferior se ve sólida (la ajustada) y solo el tramo superior que sobra —si lo hay— se ve translúcido (el resto de la barra real). Si ambas alturas coinciden, se solapan del todo y se ve como una única barra sólida, igual que antes de esta mejora.

## Cálculo (`Pages/Dashboard.razor`, método `Cargar()`)

`MesBalance` gana un campo opcional:

```csharp
private record MesBalance(string Etiqueta, decimal Balance, bool EsPositivo, decimal? BalanceAjustado = null);
```

Al construir `_historial`, solo la última iteración (`i == 0`, el mes que se está viendo) recibe `BalanceAjustado = _prevision?.Margen`; el resto se queda en `null`:

```csharp
_historial = [];
for (int i = 5; i >= 0; i--)
{
    var fecha = new DateTime(_anyoVista, _mesVista, 1).AddMonths(-i);
    var movMes = todos.Where(m => m.Fecha.Month == fecha.Month && m.Fecha.Year == fecha.Year);
    var ingresos = movMes.Where(m => m.Tipo == TipoMovimiento.Ingreso).Sum(m => m.Importe);
    var gastos   = movMes.Where(m => m.Tipo == TipoMovimiento.Gasto).Sum(m => m.Importe);
    var balance  = ingresos - gastos;
    _historial.Add(new MesBalance(
        fecha.ToString("MMM", es),
        balance,
        balance >= 0,
        i == 0 ? _prevision?.Margen : null
    ));
}
_maxBalance = _historial.Max(m => Math.Max(Math.Abs(m.Balance), Math.Abs(m.BalanceAjustado ?? m.Balance)));
if (_maxBalance == 0) _maxBalance = 1;
```

`_prevision` ya está calculado antes de este bucle en el propio método `Cargar()` (`_prevision = await Prevision.CalcularAsync(_mesVista, _anyoVista);`), así que no hace falta ningún cálculo adicional — solo se reutiliza `_prevision.Margen`, que ya incorpora `GastosMinimoAjuste` (ver mejora "mínimo mensual configurable").

`_maxBalance` se amplía para considerar también `BalanceAjustado`, por si en algún caso raro este fuera mayor en valor absoluto que el balance real (posible si el balance real ya es negativo y el ajuste lo empeora), evitando que esa barra se recorte.

## Interfaz

```razor
<div class="d-flex align-items-end gap-1" style="height:80px">
    @foreach (var m in _historial)
    {
        var h = _maxBalance > 0 ? (int)((double)Math.Abs(m.Balance) / (double)_maxBalance * 76) + 4 : 4;
        <div class="flex-fill d-flex align-items-end justify-content-center position-relative" style="height:80px">
            @if (m.BalanceAjustado is decimal ajustado && ajustado != m.Balance)
            {
                var hAjustado = _maxBalance > 0 ? (int)((double)Math.Abs(ajustado) / (double)_maxBalance * 76) + 4 : 4;
                <div style="position:absolute;bottom:0;width:80%;max-width:36px;height:@(h)px;background:@(m.EsPositivo ? "#198754" : "#dc3545");opacity:0.35;border-radius:3px 3px 0 0"></div>
                <div style="position:absolute;bottom:0;width:80%;max-width:36px;height:@(hAjustado)px;background:@(ajustado >= 0 ? "#198754" : "#dc3545");border-radius:3px 3px 0 0"></div>
            }
            else
            {
                <div style="width:80%;max-width:36px;height:@(h)px;background:@(m.EsPositivo ? "#198754" : "#dc3545");border-radius:3px 3px 0 0"></div>
            }
        </div>
    }
</div>
```

Las filas de etiquetas (mes abreviado y balance en €) debajo del gráfico no cambian — siguen mostrando `m.Balance` (el real), igual que hoy.

## Manejo de errores

No se añade manejo de errores nuevo: es un cálculo derivado adicional sobre datos ya cargados (`_prevision`, ya calculado en `Cargar()`), con los mismos supuestos que el resto del método.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Mes en curso con una categoría con mínimo no alcanzado (ej. el caso ya visto: Alimentación 0€/550€, Transporte 0€/300€) → la barra de ese mes muestra dos niveles: uno translúcido más alto (balance real) y uno sólido más bajo (Margen), coincidiendo esta cifra con la tarjeta "Saldo total disponible" de arriba.
- Registrar gastos hasta alcanzar o superar los mínimos de esas categorías → las dos alturas convergen hasta verse como una sola barra sólida.
- Sin ninguna categoría con mínimo configurado → el gráfico se ve exactamente igual que antes de esta mejora (una sola barra en cada mes).
- Los 5 meses anteriores al mes en curso nunca muestran doble nivel, sea cual sea su balance.
- Navegar a un mes distinto (con las flechas) recalcula correctamente cuál es "el mes en curso" del histórico (siempre la barra de más a la derecha).
