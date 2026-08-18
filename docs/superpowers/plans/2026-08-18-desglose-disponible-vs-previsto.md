# Desglose "Disponible" vs "Previsión pendiente" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** En la tarjeta "Saldo total disponible" del Dashboard, mostrar de qué se compone la cifra de "Este mes" cuando hay previsión de mínimos pendiente de gastar, separando cuánto es dinero real disponible y cuánto es previsión aún no materializada.

**Architecture:** Cambio de dos archivos en el mismo commit: una propiedad calculada nueva en `Models/PrevisionMensual.cs` (sin estado propio, se deriva de campos que ya existen) y un bloque condicional nuevo en `Pages/Dashboard.razor` que la consume. No hay servicio ni lógica de negocio nueva — `PrevisionService.cs` no se toca.

**Tech Stack:** .NET / Blazor Server (C#, Razor). No hay proyecto de tests automatizados en el repo — la verificación es manual, ejecutando la app.

## Global Constraints

- No modificar `GastosTotales`, `GastosMinimoAjuste`, `Margen`, `SaldoTotal`, `SaldoAcumulado` ni ningún otro cálculo existente — solo se añade una propiedad de solo lectura nueva.
- No tocar el gráfico "Ahorro últimos 6 meses" ni ninguna otra sección del Dashboard.
- Las dos líneas nuevas de la tarjeta solo se muestran cuando `GastosMinimoAjuste != 0`.
- Textos exactos (sin abreviar): `Disponible (sin lo reservado)` y `Previsión pendiente (sin gastar)`.

---

### Task 1: Propiedad `BalanceSinAjuste` y desglose en la tarjeta de saldo

**Files:**
- Modify: `Models/PrevisionMensual.cs:27` (justo después de `GastosMinimoAjuste`)
- Modify: `Pages/Dashboard.razor:109-114` (entre la línea "Este mes" y la línea "Acumulado anterior")

**Interfaces:**
- Consumes: `PrevisionMensual.IngresosReales` (decimal, ya existe), `PrevisionMensual.GastosReales` (decimal, ya existe), `PrevisionMensual.GastosMinimoAjuste` (decimal, ya existe, `Models/PrevisionMensual.cs:27`).
- Produces: `PrevisionMensual.BalanceSinAjuste` (decimal, propiedad calculada de solo lectura) — no la consume ningún otro task de este plan, pero queda disponible para el resto del Dashboard si hiciera falta en el futuro.

- [ ] **Step 1: Añadir la propiedad `BalanceSinAjuste` al modelo**

En `Models/PrevisionMensual.cs`, justo debajo de la línea 27 (`public decimal GastosMinimoAjuste => ...`), añadir:

```csharp
    // Balance real del mes sin restar la previsión pendiente de los mínimos
    public decimal BalanceSinAjuste => IngresosReales - GastosReales;
```

El archivo completo alrededor de ese punto debe quedar así:

```csharp
    // Diferencia no cubierta por el gasto real en categorías con mínimo (0 si ya lo alcanzaron)
    public decimal GastosMinimoAjuste => DetalleMinimos.Sum(d => Math.Max(0, d.Minimo - d.GastoReal));

    // Balance real del mes sin restar la previsión pendiente de los mínimos
    public decimal BalanceSinAjuste => IngresosReales - GastosReales;

    // Balance del mes en curso (sin acumulado)
    public decimal Margen => IngresosReales - GastosReales - GastosMinimoAjuste;
```

- [ ] **Step 2: Compilar para verificar que el modelo es válido**

Run: `dotnet build`
Expected: `Build succeeded.` sin errores ni warnings nuevos relacionados con `PrevisionMensual.cs`.

- [ ] **Step 3: Añadir el bloque condicional en la tarjeta del Dashboard**

En `Pages/Dashboard.razor`, el bloque actual (líneas 109-120) es:

```razor
                        <div class="d-flex justify-content-between align-items-center mt-1" style="font-size:0.8rem">
                            <div class="text-muted ps-2">├─ Este mes</div>
                            <div class="@(_prevision.Margen >= 0 ? "text-success" : "text-danger")">
                                @(_prevision.Margen >= 0 ? "+" : "")@_prevision.Margen.ToString("C")
                            </div>
                        </div>
                        <div class="d-flex justify-content-between align-items-center" style="font-size:0.8rem">
                            <div class="text-muted ps-2">└─ Acumulado anterior</div>
                            <div class="@(_prevision.SaldoAcumulado >= 0 ? "text-success" : "text-danger")">
                                @(_prevision.SaldoAcumulado >= 0 ? "+" : "")@_prevision.SaldoAcumulado.ToString("C")
                            </div>
                        </div>
```

Reemplazarlo por (se inserta el bloque `@if` nuevo entre las dos divs existentes, sin tocar ninguna de las dos):

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
                            <div class="@(_prevision.SaldoAcumulado >= 0 ? "text-success" : "text-danger")">
                                @(_prevision.SaldoAcumulado >= 0 ? "+" : "")@_prevision.SaldoAcumulado.ToString("C")
                            </div>
                        </div>
```

- [ ] **Step 4: Compilar para verificar que el Razor es válido**

Run: `dotnet build`
Expected: `Build succeeded.` sin errores de compilación de Razor.

- [ ] **Step 5: Verificación manual en la app — caso con previsión pendiente**

Run: `dotnet run` y abrir el Dashboard en un mes con categorías con mínimo mensual configurado y gasto real por debajo del mínimo (por ejemplo, el caso ya visto: Alimentación con gasto real menor que su mínimo, o Transporte igual).

Expected:
- Aparecen las dos líneas nuevas "│  ├─ Disponible (sin lo reservado)" y "│  └─ Previsión pendiente (sin gastar)" entre "Este mes" y "Acumulado anterior".
- El valor de "Disponible (sin lo reservado)" + el valor negativo de "Previsión pendiente" suman exactamente el valor de "Este mes" (ej.: si "Este mes" es -143,60 €, deben verse +424,78 € y -568,38 €, cuya suma es -143,60 €).
- "Previsión pendiente" se muestra siempre en rojo.

- [ ] **Step 6: Verificación manual en la app — caso sin previsión pendiente**

En el mismo Dashboard, ir a un mes sin categorías con mínimo configurado, o donde el gasto real ya alcance o supere todos los mínimos configurados (`GastosMinimoAjuste == 0`).

Expected: las dos líneas nuevas no aparecen — la tarjeta se ve exactamente igual que antes de este cambio (una sola línea "Este mes").

- [ ] **Step 7: Verificación manual — el gráfico no cambia**

En el mismo Dashboard, comprobar visualmente el gráfico "Ahorro últimos 6 meses".

Expected: sin cambios respecto al comportamiento antes de este plan (mismas barras, mismas etiquetas, mismo doble nivel en el mes actual si aplica).

- [ ] **Step 8: Commit**

```bash
git add Models/PrevisionMensual.cs Pages/Dashboard.razor
git commit -m "feat: desglosar disponible real vs previsión pendiente en tarjeta de saldo"
```
