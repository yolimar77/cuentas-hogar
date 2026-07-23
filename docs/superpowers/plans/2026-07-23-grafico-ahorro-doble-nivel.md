# Doble nivel en el gráfico "Ahorro últimos 6 meses" — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar, solo para la barra del mes en curso del gráfico "Ahorro últimos 6 meses", dos niveles superpuestos (balance real y balance ajustado con los mínimos mensuales pendientes), resolviendo la incoherencia con la cifra de "Margen" mostrada arriba.

**Architecture:** Cambio contenido en `Pages/Dashboard.razor`: `MesBalance` gana un campo opcional `BalanceAjustado`, poblado solo para la última entrada del histórico reutilizando `_prevision.Margen` (ya calculado). El marcado dibuja dos `div` superpuestos cuando ese campo difiere del balance real.

**Tech Stack:** Blazor WebAssembly (.NET), CSS con posicionamiento absoluto para superponer barras.

## Global Constraints

- Solo la última barra del histórico (el mes que se está viendo) puede mostrar dos niveles; las otras 5 no cambian.
- Si `BalanceAjustado` coincide con `Balance` (mínimos no configurados, o ya alcanzados/superados), se muestra una sola barra, igual que antes de esta mejora.
- Las etiquetas numéricas debajo de las barras siguen mostrando `m.Balance` (el real), sin cambios.
- No hay proyecto de tests automatizados — verificación manual únicamente.

---

### Task 1: Doble nivel en la barra del mes en curso

**Files:**
- Modify: `Pages/Dashboard.razor` (record `MesBalance`, cálculo de `_historial`/`_maxBalance` en `Cargar()`, marcado del gráfico)

**Interfaces:**
- Consumes: `_prevision.Margen : decimal` (ya calculado en `Cargar()` antes del bucle de `_historial`, ver `Models/PrevisionMensual.cs`).

- [ ] **Step 1: Añadir el campo opcional al record `MesBalance`**

Cambiar:

```csharp
    private record MesBalance(string Etiqueta, decimal Balance, bool EsPositivo);
```

por:

```csharp
    private record MesBalance(string Etiqueta, decimal Balance, bool EsPositivo, decimal? BalanceAjustado = null);
```

- [ ] **Step 2: Poblar `BalanceAjustado` solo para la última iteración y ampliar `_maxBalance`**

Cambiar:

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
                balance >= 0
            ));
        }
        _maxBalance = _historial.Max(m => Math.Abs(m.Balance));
        if (_maxBalance == 0) _maxBalance = 1;
```

por:

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

- [ ] **Step 3: Dibujar los dos niveles superpuestos en el marcado del gráfico**

Cambiar:

```razor
            <div class="d-flex align-items-end gap-1" style="height:80px">
                @foreach (var m in _historial)
                {
                    var h = _maxBalance > 0
                        ? (int)((double)Math.Abs(m.Balance) / (double)_maxBalance * 76) + 4
                        : 4;
                    <div class="flex-fill d-flex align-items-end justify-content-center" style="height:80px">
                        <div style="width:80%;max-width:36px;height:@(h)px;background:@(m.EsPositivo ? "#198754" : "#dc3545");border-radius:3px 3px 0 0"></div>
                    </div>
                }
            </div>
```

por:

```razor
            <div class="d-flex align-items-end gap-1" style="height:80px">
                @foreach (var m in _historial)
                {
                    var h = _maxBalance > 0
                        ? (int)((double)Math.Abs(m.Balance) / (double)_maxBalance * 76) + 4
                        : 4;
                    <div class="flex-fill d-flex align-items-end justify-content-center position-relative" style="height:80px">
                        @if (m.BalanceAjustado is decimal ajustado && ajustado != m.Balance)
                        {
                            var hAjustado = _maxBalance > 0
                                ? (int)((double)Math.Abs(ajustado) / (double)_maxBalance * 76) + 4
                                : 4;
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

(Las dos filas de etiquetas debajo del gráfico — mes abreviado y balance en €, líneas siguientes en el archivo — no se tocan: siguen usando `m.Balance` y `m.EsPositivo` tal cual.)

- [ ] **Step 4: Verificar que compila**

Run: `dotnet build`
Expected: build correcto, sin errores.

- [ ] **Step 5: Commit**

```bash
git add Pages/Dashboard.razor
git commit -m "feat: mostrar doble nivel en la barra del mes en curso del grafico de ahorro"
```

---

### Task 2: Verificación manual funcional

**Files:** ninguno (solo ejecución de la app)

- [ ] **Step 1: Arrancar la app**

Run: `dotnet run` (o `dotnet watch run`).

- [ ] **Step 2: Mes en curso con mínimos pendientes**

Con al menos una categoría con mínimo mensual configurado y sin alcanzar (por ejemplo, el caso ya visto: Alimentación 0€/550€), comprobar que la barra del mes en curso muestra dos niveles: uno translúcido más alto (balance real) y uno sólido más bajo, y que este último coincide exactamente con la cifra de "Saldo total disponible" → "Este mes" (o "Margen") mostrada arriba.

- [ ] **Step 3: Alcanzar el mínimo**

Registrar gastos en esas categorías hasta igualar o superar el mínimo configurado. Comprobar que las dos alturas convergen y la barra pasa a verse como una sola, sólida.

- [ ] **Step 4: Sin mínimos configurados**

Quitar (o con una cuenta/mes sin) categorías con mínimo configurado. Comprobar que el gráfico se ve exactamente igual que antes de esta mejora — una sola barra en cada mes, sin niveles superpuestos.

- [ ] **Step 5: Meses anteriores nunca muestran doble nivel**

Comprobar visualmente que ninguna de las 5 barras anteriores a la del mes en curso muestra nunca dos niveles, sea cual sea su balance.

- [ ] **Step 6: Navegación entre meses**

Usar las flechas de mes anterior/siguiente y comprobar que el doble nivel siempre aparece en la barra de más a la derecha (el mes que se está viendo en cada momento), recalculándose correctamente.
