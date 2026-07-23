# Aviso de vencimiento para pagos recurrentes

## Objetivo

Cuando un pago recurrente tiene fecha de fin (no es indefinido), avisar con antelación de que está a punto de vencer, para que el usuario pueda decidir si extender la fecha o dejar que termine, sin tener que estar revisando manualmente la lista de recurrentes. Complementa la mejora anterior ("fecha de fin opcional"): los recurrentes indefinidos nunca generan este aviso porque no tienen vencimiento.

## Alcance

- Solo recurrentes activos (`Activo == true`) con `FechaFin` no nulo.
- Banner discreto, app-wide, independiente del banner existente de reconexión con Google Drive (`Layout/MainLayout.razor`) — ambos pueden mostrarse a la vez, apilados, sin fusionarse.
- Sin estado persistido ni sincronizado: el aviso se calcula en vivo cada vez a partir de `DateTime.Today`, igual que el banner de Drive. Si el usuario no abre la app justo el día de los 30, pierde ese aviso concreto, pero los últimos 4 días (3, 2, 1, 0) dan margen de sobra.
- Se muestran todos los recurrentes que cumplan la condición ese día, por nombre — nunca se resume ni se oculta ninguno.

## Calendario de avisos

Para cada recurrente con `FechaFin`, se calcula `diasRestantes = (FechaFin.Date - hoy).Days`. Se avisa si:

- `diasRestantes == 30` (aviso único, un mes antes), o
- `diasRestantes` está entre 0 y 3 (aviso diario los últimos 4 días: 3, 2, 1 y el propio día del vencimiento).

Fuera de esos valores (ej. 29, 15, 5 días), no se avisa.

## Arquitectura

Nuevo servicio de responsabilidad única, `Services/RecurrenteAvisoService.cs` (mismo patrón que `ComparativaService`):

```csharp
public class RecurrenteAvisoService(LocalDbService db)
{
    public async Task<List<RecurrenteVencimiento>> ObtenerPorVencerAsync()
}
```

Record de resultado (en `Models/`):

```csharp
public record RecurrenteVencimiento(string Concepto, int DiasRestantes);
```

`Layout/MainLayout.razor` inyecta el servicio, carga la lista en `OnInitializedAsync` (este layout envuelve toda la app y persiste entre navegaciones) y la recalcula también cuando termina una sincronización (`Sync.OnSyncCompletado`), igual que ya hace con `Drive.OnEstadoCambiado`.

## Cálculo

```csharp
public async Task<List<RecurrenteVencimiento>> ObtenerPorVencerAsync()
{
    var recurrentes = await db.ObtenerRecurrentesAsync();
    var hoy = DateTime.Today;

    var resultado = new List<RecurrenteVencimiento>();
    foreach (var rec in recurrentes.Where(r => r.Activo && r.FechaFin.HasValue))
    {
        var diasRestantes = (rec.FechaFin!.Value.Date - hoy).Days;
        if (diasRestantes == 30 || (diasRestantes >= 0 && diasRestantes <= 3))
            resultado.Add(new RecurrenteVencimiento(rec.Concepto, diasRestantes));
    }

    return resultado.OrderBy(r => r.DiasRestantes).ToList();
}
```

Ordenado por `DiasRestantes` ascendente, para que el más urgente aparezca primero.

## Interfaz (`Layout/MainLayout.razor`)

Banner independiente del de Drive, con una lista debajo para que quepan todos los nombres sin amontonarse en una sola línea:

```razor
@if (_recurrentesPorVencer.Count > 0)
{
    <div class="bg-warning text-dark px-3 py-2" style="font-size:0.85rem">
        <div class="d-flex flex-wrap justify-content-between align-items-center gap-2">
            <span><i class="ph-bold ph-warning"></i> Pagos recurrentes a punto de vencer:</span>
            <a href="recurrentes" class="btn btn-sm btn-dark flex-shrink-0">Revisar</a>
        </div>
        <ul class="mb-0 mt-1 ps-3">
            @foreach (var r in _recurrentesPorVencer)
            {
                <li>@r.Concepto · @TextoVencimiento(r.DiasRestantes)</li>
            }
        </ul>
    </div>
}
```

```csharp
private static string TextoVencimiento(int dias) => dias switch
{
    0 => "vence hoy",
    1 => "vence mañana",
    _ => $"vence en {dias} días"
};
```

Este bloque es independiente del `@if (Drive.NecesitaReconectar)` ya existente — ambos pueden mostrarse a la vez, apilados uno debajo del otro, sin compartir marcado ni lógica. El botón "Revisar" enlaza a `recurrentes` (no a `configuracion`), ya que ahí es donde se edita la fecha de fin.

## Manejo de errores

No se añade manejo de errores nuevo: es una consulta de solo lectura sobre `db.ObtenerRecurrentesAsync()`, con los mismos supuestos que el resto de servicios de la app.

## Testing

No hay proyecto de tests automatizados en el repo. Verificación manual:

- Un recurrente con `FechaFin` a 30 días exactos de hoy → aparece con "vence en 30 días".
- Uno a 3, 2, 1 y 0 días → aparece cada uno de esos días con el texto correspondiente ("vence en 3 días", "vence mañana", "vence hoy").
- Uno a 29, 15 o 5 días (fuera de los umbrales) → no aparece.
- Uno indefinido (`FechaFin = null`) → nunca aparece.
- Varios recurrentes por vencer a la vez → todos listados por nombre, ordenados por urgencia.
- El banner de Drive y el de vencimiento pueden aparecer juntos, cada uno de forma independiente.
- El botón "Revisar" lleva a Recurrentes.
