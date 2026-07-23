# Fecha de fin opcional para pagos recurrentes

## Objetivo

Hay pagos recurrentes (ingresos o gastos) que se repiten todos los meses sin fecha de fin previsible (una nómina, un gasto fijo mensual). Hoy `FechaFin` es obligatoria y limitada a un año por defecto, obligando al usuario a recordar actualizarla periódicamente — si se olvida, el recurrente deja de generarse sin que se note. Esta mejora permite marcar un recurrente como "sin fecha de fin" para que no haga falta mantenerlo actualizado.

## Alcance

- Solo afecta a `MovimientoRecurrente` (Pagos recurrentes). No toca la comparativa mensual ni el mínimo mensual configurable (features ya implementadas).
- No incluye el sistema de avisos de vencimiento (se diseñará por separado, después de esta mejora).
- No requiere ninguna lógica nueva de sincronización: la fusión entre dispositivos ya usa "gana el `ModificadoEn` más reciente" sobre el registro completo (`SyncService.MergeRecurrentesAsync`), y `FechaFin` es un campo más de ese registro — se propaga correctamente sea `null` o una fecha, sin cambios en `SyncService`.
- El checkbox "Sin fecha de fin" no aparece marcado por defecto: un recurrente nuevo sigue pidiendo una fecha de fin real (hoy + 1 año) salvo que el usuario marque la casilla explícitamente.

## Modelo de datos

`Models/MovimientoRecurrente.cs`: `FechaFin` pasa de `DateTime` a `DateTime?`. `null` = sin fecha de fin (indefinido).

```csharp
public bool EstaActivoEnMes(int mes, int anyo) =>
    Activo &&
    new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
    (FechaFin is null || new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1));
```

## Cálculo (`Services/PrevisionService.cs`)

Dos cálculos distintos que hoy asumen `FechaFin` como fecha concreta, ambos ya acotados por otro límite que no depende de `FechaFin`:

**1. Previsión de gastos/ingresos recurrentes** (`GastosRecurrentes`/`IngresosRecurrentes`, usados en las tarjetas "previstos"): se calculan filtrando `recurrentes.Where(r => r.EstaActivoEnMes(mes, anyo))` para el mes consultado, directamente sobre la definición del recurrente, sin depender de movimientos ya generados. Con `EstaActivoEnMes` actualizado, un recurrente indefinido se considera activo para cualquier mes que se consulte, por lejano que sea.

**2. Horizonte de generación de movimientos reales** (`GenerarMovimientosRecurrentesAsync`, pre-crea `Movimiento` con antelación): usa `finHorizonte` (por defecto ~2 meses vista, o el mes que se esté navegando en el Dashboard si se pasa explícitamente) como único límite cuando no hay `FechaFin`:

```csharp
// OcurrenciasEnMes (frecuencia semanal): antes usaba min(ultimoDia, FechaFin)
var hasta = rec.FechaFin is null || ultimoDia < rec.FechaFin.Value ? ultimoDia : rec.FechaFin.Value;
```

```csharp
// GenerarMovimientosRecurrentesAsync: horizonte de generación
var limite = rec.FechaFin is null || finHorizonte <= rec.FechaFin.Value
    ? finHorizonte
    : rec.FechaFin.Value;
```

En ambos casos, quitar la fecha de fin nunca genera "de más" ni causa bucles sin fin: el único límite que queda es el que ya existía (fin del mes consultado, o el horizonte de generación), ninguno de los dos depende de `FechaFin`.

## Interfaz (`Pages/Recurrentes.razor`)

En la lista de pagos recurrentes, donde hoy se ve "hasta MM/yyyy":

```razor
<small class="text-muted">@DescripcionFrecuencia(rec) · @(rec.FechaFin.HasValue ? $"hasta {rec.FechaFin.Value:MM/yyyy}" : "indefinido")</small>
```

En el formulario de alta/edición, un checkbox "Sin fecha de fin" (desmarcado por defecto) que oculta el campo "Hasta" al marcarlo. Implementado como propiedad auxiliar sin campo de estado nuevo:

```csharp
private bool SinFechaFin
{
    get => _nuevo.FechaFin is null;
    set => _nuevo.FechaFin = value ? null : DateTime.Today.AddYears(1);
}
```

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

## Manejo de errores

No se añade manejo de errores nuevo: `FechaFin` nullable se persiste igual que cualquier otro campo (el modelo `Movimiento` ya tiene `Notas : string?`, así que el patrón de nullable en el almacenamiento local ya existe). Guardar reutiliza `GuardarRecurrenteAsync`/`Guardar()`, que ya tienen su propio manejo de errores en `Recurrentes.razor`.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Crear un recurrente nuevo sin marcar el checkbox → comportamiento actual, sin cambios (fecha de fin real, editable).
- Crear uno marcando "Sin fecha de fin" → se guarda con `FechaFin = null`; en la lista aparece "indefinido"; se sigue generando el movimiento cada mes en el Dashboard sin tener que tocar nada.
- Editar un recurrente existente con fecha y marcarle "Sin fecha de fin" → pasa a indefinido y desaparece de la lista el "hasta".
- Editar uno indefinido y desmarcar el checkbox → vuelve a pedir una fecha (por defecto hoy + 1 año).
- Un recurrente semanal (`Frecuencia.Semanal`) indefinido también se sigue generando cada semana sin tope de fecha adicional.
- La previsión de "Gastos/Ingresos previstos" en el Dashboard sigue contando un recurrente indefinido al navegar a meses futuros lejanos (más allá del horizonte de generación de movimientos reales).
