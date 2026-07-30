# Frecuencia Anual para Pagos Recurrentes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir una frecuencia `Anual` a `MovimientoRecurrente` para que un gasto o ingreso se repita una vez al año en el día/mes de `FechaInicio`, sin campos nuevos en el modelo.

**Architecture:** Se reutiliza el campo `FechaInicio` ya existente como origen del día y mes de la ocurrencia anual (nada de campos nuevos). Tres puntos de cambio: la regla de actividad por mes en el modelo (`EstaActivoEnMes`), la generación de movimientos futuros (`PrevisionService.GenerarMovimientosRecurrentesAsync`), y el formulario/listado (`Recurrentes.razor`). `OcurrenciasEnMes` no cambia — ya devuelve 1 para cualquier frecuencia no semanal, y con `EstaActivoEnMes` corregido un recurrente `Anual` solo entra en el cálculo durante su mes.

**Tech Stack:** Blazor WebAssembly (.NET), sin proyecto de tests automatizados en el repo — verificación por compilación (`dotnet build`) y comprobación manual en el navegador, mismo patrón que specs anteriores de esta app.

## Global Constraints

- No añadir campos nuevos al modelo `MovimientoRecurrente` — el mes/día anual sale de `FechaInicio.Month`/`FechaInicio.Day`.
- El año de la primera ocurrencia es literalmente `FechaInicio.Year`, sin inferencia de "si ya pasó, saltar al año siguiente".
- El `Periodo` de los movimientos generados para `Anual` usa el mismo formato `"yyyy-MM"` que `Mensual`.
- No tocar `SyncService` ni el esquema de sincronización — `Frecuencia` ya es un campo serializado más de `MovimientoRecurrente`.
- No actualizar `GUIA.md` — no es un paso establecido en las specs anteriores de esta app (se regenera aparte, no por feature).

---

### Task 1: Modelo — enum `Anual` y `EstaActivoEnMes`

**Files:**
- Modify: `Models/MovimientoRecurrente.cs:3` (enum), `Models/MovimientoRecurrente.cs:23-26` (`EstaActivoEnMes`)

**Interfaces:**
- Consumes: nada (es la base del resto de tasks).
- Produces: `Frecuencia.Anual` (valor de enum), `EstaActivoEnMes(int mes, int anyo) : bool` con la nueva regla — usado por `PrevisionService.CalcularAsync` (Task 2 no lo toca, ya funciona con el cambio) y por la UI (Task 3, indirectamente vía los datos que carga `Recurrentes.razor`).

- [ ] **Step 1: Cambiar el enum**

En `Models/MovimientoRecurrente.cs:3`, cambiar:

```csharp
public enum Frecuencia { Mensual, Semanal }
```

por:

```csharp
public enum Frecuencia { Mensual, Semanal, Anual }
```

- [ ] **Step 2: Actualizar `EstaActivoEnMes`**

En `Models/MovimientoRecurrente.cs:23-26`, cambiar:

```csharp
    public bool EstaActivoEnMes(int mes, int anyo) =>
        Activo &&
        new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
        (FechaFin is null || new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1));
```

por:

```csharp
    public bool EstaActivoEnMes(int mes, int anyo) =>
        Activo &&
        (Frecuencia != Frecuencia.Anual || mes == FechaInicio.Month) &&
        new DateTime(anyo, mes, 1) >= new DateTime(FechaInicio.Year, FechaInicio.Month, 1) &&
        (FechaFin is null || new DateTime(anyo, mes, 1) <= new DateTime(FechaFin.Value.Year, FechaFin.Value.Month, 1));
```

- [ ] **Step 3: Compilar**

Run: `dotnet build`
Expected: `Compilación correcta. 0 Errores` (el proyecto ya compilaba limpio antes de este cambio — confirmar que sigue así).

- [ ] **Step 4: Commit**

```bash
git add Models/MovimientoRecurrente.cs
git commit -m "feat: añadir Frecuencia.Anual al modelo de recurrentes"
```

---

### Task 2: Generación de movimientos anuales (`PrevisionService`)

**Files:**
- Modify: `Services/PrevisionService.cs:112-135` (rama `else` de `GenerarMovimientosRecurrentesAsync`, pasa a `else if (Semanal) / else if (Anual) / else (Mensual)`)

**Interfaces:**
- Consumes: `Frecuencia.Anual` (Task 1), `rec.FechaInicio` (`DateTime`), `rec.FechaFin` (`DateTime?`), `limite` (`DateTime`, ya calculado en el método antes de este bloque, línea 84-86), `db.ExisteMovimientoRecurrenteAsync(string recurrenteId, string periodo) : Task<bool>`, `db.GuardarMovimientoAsync(Movimiento) : Task`.
- Produces: un `Movimiento` por año para cada recurrente `Anual`, con `Periodo` en formato `"yyyy-MM"` — consumido por el Dashboard/Recurrentes vía `db.ObtenerMovimientosAsync()` igual que los de Mensual/Semanal, sin diferencias de tratamiento posterior.

- [ ] **Step 1: Añadir la rama `Anual`**

En `Services/PrevisionService.cs`, el método `GenerarMovimientosRecurrentesAsync` tiene hoy (líneas 88-135):

```csharp
            if (rec.Frecuencia == Models.Frecuencia.Semanal)
            {
                var cursor = rec.FechaInicio;
                while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
                while (cursor <= limite)
                {
                    var periodo = cursor.ToString("yyyy-MM-dd");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = cursor,
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    cursor = cursor.AddDays(7);
                }
            }
            else
            {
                var fecha = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
                while (fecha <= limite)
                {
                    var periodo = fecha.ToString("yyyy-MM");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = new DateTime(fecha.Year, fecha.Month, dia),
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    fecha = fecha.AddMonths(1);
                }
            }
```

Insertar una rama `else if` entre la de `Semanal` y el `else` (Mensual), quedando:

```csharp
            if (rec.Frecuencia == Models.Frecuencia.Semanal)
            {
                var cursor = rec.FechaInicio;
                while (cursor.DayOfWeek != rec.DiaDeSemana) cursor = cursor.AddDays(1);
                while (cursor <= limite)
                {
                    var periodo = cursor.ToString("yyyy-MM-dd");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = cursor,
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    cursor = cursor.AddDays(7);
                }
            }
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
            else
            {
                var fecha = new DateTime(rec.FechaInicio.Year, rec.FechaInicio.Month, 1);
                while (fecha <= limite)
                {
                    var periodo = fecha.ToString("yyyy-MM");
                    if (!await db.ExisteMovimientoRecurrenteAsync(rec.Id, periodo))
                    {
                        var dia = Math.Min(rec.DiaDelMes, DateTime.DaysInMonth(fecha.Year, fecha.Month));
                        await db.GuardarMovimientoAsync(new Movimiento
                        {
                            Concepto     = rec.Concepto,
                            Importe      = rec.Importe,
                            Tipo         = rec.Tipo,
                            Fecha        = new DateTime(fecha.Year, fecha.Month, dia),
                            CategoriaId  = rec.CategoriaId,
                            CuentaId     = rec.CuentaId,
                            RecurrenteId = rec.Id,
                            Periodo      = periodo
                        });
                    }
                    fecha = fecha.AddMonths(1);
                }
            }
```

- [ ] **Step 2: Compilar**

Run: `dotnet build`
Expected: `Compilación correcta. 0 Errores`

- [ ] **Step 3: Commit**

```bash
git add Services/PrevisionService.cs
git commit -m "feat: generar movimientos para recurrentes con frecuencia anual"
```

---

### Task 3: Interfaz — formulario y listado (`Recurrentes.razor`)

**Files:**
- Modify: `Pages/Recurrentes.razor:97-126` (selector de frecuencia + bloque de día), `Pages/Recurrentes.razor:306-309` (`DescripcionFrecuencia`)

**Interfaces:**
- Consumes: `Frecuencia.Anual` (Task 1), `_nuevo.FechaInicio` (ya existe como campo del formulario, líneas 127-130, sin cambios), `NombreDia(DayOfWeek) : string` (ya existe, línea 311, sin cambios).
- Produces: `NombreMes(int) : string` (nuevo helper privado, usado solo por `DescripcionFrecuencia`).

- [ ] **Step 1: Añadir el botón "Anual"**

En `Pages/Recurrentes.razor:97-104`, cambiar:

```razor
                    <div class="mb-2">
                        <label class="form-label small">Frecuencia</label>
                        <div class="btn-group w-100">
                            <button class="btn @(_nuevo.Frecuencia == Frecuencia.Mensual ? "btn-primary" : "btn-outline-primary")"
                                    @onclick="() => _nuevo.Frecuencia = Frecuencia.Mensual">Mensual</button>
                            <button class="btn @(_nuevo.Frecuencia == Frecuencia.Semanal ? "btn-primary" : "btn-outline-primary")"
                                    @onclick="() => _nuevo.Frecuencia = Frecuencia.Semanal">Semanal</button>
                        </div>
                    </div>
```

por:

```razor
                    <div class="mb-2">
                        <label class="form-label small">Frecuencia</label>
                        <div class="btn-group w-100">
                            <button class="btn @(_nuevo.Frecuencia == Frecuencia.Mensual ? "btn-primary" : "btn-outline-primary")"
                                    @onclick="() => _nuevo.Frecuencia = Frecuencia.Mensual">Mensual</button>
                            <button class="btn @(_nuevo.Frecuencia == Frecuencia.Semanal ? "btn-primary" : "btn-outline-primary")"
                                    @onclick="() => _nuevo.Frecuencia = Frecuencia.Semanal">Semanal</button>
                            <button class="btn @(_nuevo.Frecuencia == Frecuencia.Anual ? "btn-primary" : "btn-outline-primary")"
                                    @onclick="() => _nuevo.Frecuencia = Frecuencia.Anual">Anual</button>
                        </div>
                    </div>
```

- [ ] **Step 2: Ocultar el bloque de día para `Anual`**

En `Pages/Recurrentes.razor:105-126`, cambiar:

```razor
                    @if (_nuevo.Frecuencia == Frecuencia.Mensual)
                    {
                        <div class="mb-2">
                            <label class="form-label small">Día del mes</label>
                            <input class="form-control" type="number" min="1" max="31" @bind="_nuevo.DiaDelMes" />
                        </div>
                    }
                    else
                    {
                        <div class="mb-2">
                            <label class="form-label small">Día de la semana</label>
                            <select class="form-select" @bind="_nuevo.DiaDeSemana">
                                <option value="1">Lunes</option>
                                <option value="2">Martes</option>
                                <option value="3">Miércoles</option>
                                <option value="4">Jueves</option>
                                <option value="5">Viernes</option>
                                <option value="6">Sábado</option>
                                <option value="0">Domingo</option>
                            </select>
                        </div>
                    }
```

por:

```razor
                    @if (_nuevo.Frecuencia == Frecuencia.Mensual)
                    {
                        <div class="mb-2">
                            <label class="form-label small">Día del mes</label>
                            <input class="form-control" type="number" min="1" max="31" @bind="_nuevo.DiaDelMes" />
                        </div>
                    }
                    else if (_nuevo.Frecuencia == Frecuencia.Semanal)
                    {
                        <div class="mb-2">
                            <label class="form-label small">Día de la semana</label>
                            <select class="form-select" @bind="_nuevo.DiaDeSemana">
                                <option value="1">Lunes</option>
                                <option value="2">Martes</option>
                                <option value="3">Miércoles</option>
                                <option value="4">Jueves</option>
                                <option value="5">Viernes</option>
                                <option value="6">Sábado</option>
                                <option value="0">Domingo</option>
                            </select>
                        </div>
                    }
                    else
                    {
                        <p class="text-muted small mb-2">El día y mes de "Desde" se repiten cada año.</p>
                    }
```

- [ ] **Step 3: `DescripcionFrecuencia` + helper `NombreMes`**

En `Pages/Recurrentes.razor:306-309`, cambiar:

```csharp
    private static string DescripcionFrecuencia(MovimientoRecurrente rec) =>
        rec.Frecuencia == Frecuencia.Semanal
            ? $"Cada {NombreDia(rec.DiaDeSemana)}"
            : $"Día {rec.DiaDelMes}";
```

por:

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

- [ ] **Step 4: Compilar**

Run: `dotnet build`
Expected: `Compilación correcta. 0 Errores`

- [ ] **Step 5: Commit**

```bash
git add Pages/Recurrentes.razor
git commit -m "feat: UI para frecuencia anual en pagos recurrentes"
```

---

### Task 4: Verificación manual end-to-end

**Files:** ninguno (solo verificación en navegador, no hay proyecto de tests automatizados)

**Interfaces:**
- Consumes: la app completa ya construida en las Tasks 1-3.
- Produces: confirmación de que el flujo funciona; no produce artefactos para otras tasks.

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run`
Expected: la consola imprime la URL local (tipo `http://localhost:5xxx`); abrir esa URL en el navegador.

- [ ] **Step 2: Crear un recurrente Anual y comprobar el listado**

En Ajustes → Pagos recurrentes → Nuevo: Tipo Gasto, Concepto "Seguro coche", Importe 300, Frecuencia "Anual", Desde = 01/08/2026, Sin fecha de fin. Guardar.
Expected: en la lista aparece "Cada 1 de agosto".

- [ ] **Step 3: Comprobar que solo aparece en su mes**

En el Dashboard, navegar a agosto 2026.
Expected: el gasto de 300€ aparece en previstos/reales (tras la generación automática de movimientos).

Navegar a julio 2026 y a septiembre 2026.
Expected: el gasto NO aparece en ninguno de los dos meses.

- [ ] **Step 4: Comprobar que se repite el año siguiente**

Navegar a agosto 2027.
Expected: el gasto de 300€ vuelve a aparecer (nueva ocurrencia anual generada).

- [ ] **Step 5: Comprobar el clamping de día en año bisiesto**

Editar el recurrente (o crear uno nuevo) con Desde = 29/02/2028. Guardar.
Navegar a febrero 2029.
Expected: el movimiento generado cae el 28 de febrero de 2029 (no falla ni genera fecha inválida).

## Self-Review

**1. Cobertura del spec:** modelo (Task 1) ✅, `GenerarMovimientosRecurrentesAsync` (Task 2) ✅, UI botón/bloque día/`DescripcionFrecuencia` (Task 3) ✅, casos de la sección Testing del spec (agosto/año siguiente/bisiesto) cubiertos en Task 4 ✅. `OcurrenciasEnMes` no necesita task porque el spec explícitamente dice que no cambia.

**2. Placeholders:** ninguno — cada step tiene el código completo a pegar, sin "TODO" ni "similar a Task N".

**3. Consistencia de tipos:** `EstaActivoEnMes(int mes, int anyo) : bool` (Task 1) se usa sin cambios por `PrevisionService.CalcularAsync` (no tocado). `Frecuencia.Anual` se referencia igual en las tres tasks. `NombreMes(int) : string` solo se usa dentro de `DescripcionFrecuencia`, mismo archivo — sin desajustes entre tasks.
