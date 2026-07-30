# Frecuencia anual para pagos recurrentes

## Objetivo

Hay gastos e ingresos que se repiten una sola vez al año en una fecha fija (ej. seguro del coche, IBI, paga extra). Hoy `MovimientoRecurrente` solo admite `Mensual` (cada mes) y `Semanal` (cada semana). Esta mejora añade una frecuencia `Anual`: se define una fecha (`FechaInicio`), y el pago se repite cada año en ese mismo día y mes.

## Alcance

- Solo afecta a `MovimientoRecurrente` (Pagos recurrentes) y a los cálculos de `PrevisionService` que dependen de la frecuencia.
- No se añade ningún campo nuevo al modelo: el mes y el día del pago anual se leen directamente de `FechaInicio` (que ya existe para todas las frecuencias). El usuario fija la fecha exacta de la primera ocurrencia (incluido el año) directamente en el campo "Desde"; no hay lógica de "si el mes ya pasó este año, saltar al que viene" — el año de inicio es literalmente el año que el usuario ponga en "Desde".
- No requiere ninguna lógica nueva de sincronización: `Frecuencia` ya es un campo más de `MovimientoRecurrente`, y `SyncService.MergeRecurrentesAsync` fusiona el registro completo por `ModificadoEn` más reciente, sin mirar valores concretos del enum.
- Los recurrentes ya guardados en Drive no tienen forma de ser `Anual` (el enum no existe hoy), así que no hay migración de datos que hacer.

## Modelo de datos

`Models/MovimientoRecurrente.cs`: se añade un valor al enum, sin campos nuevos.

```csharp
public enum Frecuencia { Mensual, Semanal, Anual }
```

`EstaActivoEnMes` gana una condición extra para `Anual` (el mes tiene que coincidir con el de `FechaInicio`); los límites de año con `FechaInicio`/`FechaFin` que ya existen no cambian:

```csharp
public bool EstaActivoEnMes(int mes, int anyo) =>
    Activo &&
    (Frecuencia != Frecuencia.Anual || mes == FechaInicio.Month) &&
    new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
    (FechaFin is null || new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1));
```

## Cálculo (`Services/PrevisionService.cs`)

**`OcurrenciasEnMes`**: sin cambios. Ya devuelve `1` para cualquier frecuencia que no sea `Semanal`, y `EstaActivoEnMes` ya garantiza que un recurrente `Anual` solo aparece en `recActivosMes` durante su mes correspondiente — así que se cuenta una vez al año automáticamente.

**`GenerarMovimientosRecurrentesAsync`**: nueva rama para `Anual`, junto a las de `Semanal` y Mensual (else actual). Genera un movimiento por año, con el mismo `Periodo` en formato `"yyyy-MM"` que usa Mensual (identifica de forma única la ocurrencia de ese año):

```csharp
else if (rec.Frecuencia == Models.Frecuencia.Anual)
{
    var anyo = rec.FechaInicio.Year;
    while (new DateTime(anyo, rec.FechaInicio.Month, 1) <= limite)
    {
        var periodo = $"{anyo:0000}-{rec.FechaInicio.Month:00}";
        if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
        {
            var dia = Math.Min(rec.FechaInicio.Day, DateTime.DaysInMonth(anyo, rec.FechaInicio.Month));
            await db.GuardarMovimientoAsync(new Movimiento
            {
                Concepto     = rec.Concepto,
                Importe      = rec.Importe,
                Tipo         = rec.Tipo,
                Fecha        = new DateTime(anyo, rec.FechaInicio.Month, dia),
                CategoriaId  = rec.CategoriaId,
                CuentaId     = rec.CuentaId,
                RecurrenteId = rec.Id,
                Periodo      = periodo
            });
        }
        anyo++;
    }
}
```

`limite` ya está calculado antes de este bloque (acota por `rec.FechaFin` si existe, igual que las otras dos frecuencias), así que no hace falta ningún límite adicional.

## Interfaz (`Pages/Recurrentes.razor`)

**Selector de frecuencia**: tercer botón junto a Mensual/Semanal:

```razor
<button class="btn @(_nuevo.Frecuencia == Frecuencia.Anual ? "btn-primary" : "btn-outline-primary")"
        @onclick="() => _nuevo.Frecuencia = Frecuencia.Anual">Anual</button>
```

**Bloque de "Día del mes"/"Día de la semana"**: pasa de `@if (_nuevo.Frecuencia == Frecuencia.Mensual) { ... } else { ... }` a un `switch` de tres ramas; para `Anual` no se muestra ningún input adicional (el mes y el día salen de "Desde"), solo un texto de ayuda:

```razor
@if (_nuevo.Frecuencia == Frecuencia.Anual)
{
    <p class="text-muted small mb-2">El día y mes de "Desde" se repiten cada año.</p>
}
```

**`DescripcionFrecuencia`**: nuevo caso para `Anual`, ej. "Cada 1 de agosto":

```csharp
private static string DescripcionFrecuencia(MovimientoRecurrente rec) => rec.Frecuencia switch
{
    Frecuencia.Semanal => $"Cada {NombreDia(rec.DiaDeSemana)}",
    Frecuencia.Anual   => $"Cada {rec.FechaInicio.Day} de {NombreMes(rec.FechaInicio.Month)}",
    _                  => $"Día {rec.DiaDelMes}"
};

private static string NombreMes(int m) => m switch
{
    1 => "enero", 2 => "febrero", 3 => "marzo", 4 => "abril",
    5 => "mayo", 6 => "junio", 7 => "julio", 8 => "agosto",
    9 => "septiembre", 10 => "octubre", 11 => "noviembre", _ => "diciembre"
};
```

`AbrirEditar` ya copia `Frecuencia` y `FechaInicio` al abrir el formulario de edición — no necesita ningún campo adicional para `Anual`.

## Manejo de errores

No se añade manejo de errores nuevo: mismo flujo de guardado (`GuardarRecurrenteAsync`/`Guardar()`) que las otras frecuencias, con su propio manejo de errores ya existente en `Recurrentes.razor`.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Crear un recurrente `Anual` con "Desde" = 01/08/2026 → en la lista aparece "Cada 1 de agosto"; al navegar el Dashboard a agosto 2026 aparece en previstos/reales tras generar movimientos; en cualquier otro mes de 2026 o 2027 no aparece salvo en agosto de cada año.
- Navegar el Dashboard a agosto 2027 (año siguiente) → el recurrente `Anual` sigue activo y genera su movimiento correspondiente.
- Crear un recurrente `Anual` con "Desde" en un mes ya pasado de un año futuro y `FechaFin` un año después → solo genera una ocurrencia (la del año de "Desde"), no dos.
- Editar un recurrente `Mensual` existente y cambiarlo a `Anual` → al guardar se regeneran los movimientos futuros (lógica ya existente en `Guardar()` que borra movimientos futuros al editar) usando ahora la nueva frecuencia.
- Un recurrente `Anual` con "Desde" = 29/02/2028 (año bisiesto) → en 2029 y 2030 (no bisiestos) genera el movimiento el 28 de febrero, gracias al `Math.Min` con `DateTime.DaysInMonth`.
