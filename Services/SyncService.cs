using HomeAccounts.Models;
using System.Text.Json;

namespace HomeAccounts.Services;

public class SyncService(DriveService drive, LocalDbService db)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private const string NombreMovs = "movimientos.json";
    private const string NombreRecs = "recurrentes.json";
    private const string NombreCats = "categorias.json";
    private const string NombreCuents = "cuentas.json";
    private const string NombreDels = "deletions.json";

    public bool SincronizandoAhora { get; private set; } = false;
    public DateTime? UltimaSincronizacion { get; private set; }
    public string? UltimoError { get; private set; }
    public string? UltimoDetalle { get; private set; }

    // Solo diagnóstico: en qué paso concreto está la sync ahora mismo, para poder saber dónde se
    // queda parada si vuelve a pasar, sin necesidad de la consola del navegador.
    public string? PasoActual { get; private set; }

    public event Func<Task>? OnSyncCompletado;
    public event Action? OnEstadoCambiado;

    public async Task SincronizarAsync()
    {
        if (!drive.Conectado || drive.FolderId == null || SincronizandoAhora) return;
        SincronizandoAhora = true;
        UltimoError = null;
        OnEstadoCambiado?.Invoke();
        Console.WriteLine("[HA-diag] SincronizarAsync: inicio");

        try
        {
            // IMPORTANTE: no hay un bloqueo de principio a fin aquí. Cada paso interno habla con
            // Drive (puede tardar mucho o colgarse en una conexión mala) y solo readquiere el
            // bloqueo brevemente justo antes de escribir en local, releyendo el estado fresco en
            // ese momento. Si se mantuviera un único bloqueo durante toda la sync, una llamada a
            // Drive que se cuelga bloquearía también cualquier guardado/lectura local del resto
            // de la app durante todo ese tiempo (esto pasó: sync colgada = app entera colgada).
            PasoActual = "listando archivos";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            var archivos = await drive.ListarArchivosAsync();
            var idx = new Dictionary<string, DriveFileInfo>();
            foreach (var f in archivos) idx[f.Nombre] = f;

            // 1. Propagar eliminaciones
            PasoActual = "borrados";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            var eliminados = await SincronizarEliminacionesAsync(idx);

            // 2. Categorías y cuentas primero: dos dispositivos pueden haber creado una
            //    con el mismo nombre antes de sincronizar entre sí. MergeCategoriasAsync/
            //    MergeCuentasAsync deduplican por nombre y devuelven el remap idPerdedor->idGanador.
            PasoActual = "categorías";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            var (cambiosCat, remapCats) = await MergeCategoriasAsync(idx, eliminados);
            PasoActual = "cuentas";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            var (cambiosCuent, remapCuents) = await MergeCuentasAsync(idx, eliminados);

            // 3. Reparar movimientos/recurrentes que quedaran apuntando a un Id descartado
            //    en el paso anterior. IMPORTANTE: no eliminar este paso sin preservar el
            //    remapeo — quitarlo (como pasó una vez en el pasado) deja categorías en
            //    blanco al sincronizar entre dispositivos.
            PasoActual = "reparando referencias";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            int cambiosReparacion = await AplicarRemapReferenciasAsync(remapCats, remapCuents);

            // 4. Merge movimientos y recurrentes
            PasoActual = "movimientos";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            int cambiosMov = await MergeMovimientosAsync(idx, eliminados);
            PasoActual = "recurrentes";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            int cambiosRec = await MergeRecurrentesAsync(idx, eliminados);

            // 5. Propagar categoría/cuenta del recurrente a sus movimientos generados
            PasoActual = "propagando recurrentes";
            OnEstadoCambiado?.Invoke();
            Console.WriteLine($"[HA-diag] Paso: {PasoActual}");
            int cambiosPropagacion = await PropagarcategoriasRecurrentesAsync();

            int totalCambios = cambiosMov + cambiosRec + cambiosCat + cambiosCuent + cambiosReparacion + cambiosPropagacion;
            UltimaSincronizacion = DateTime.Now;
            UltimoDetalle = $"Sync OK · {totalCambios} cambios";
            Console.WriteLine($"[HA-diag] SincronizarAsync: pasos OK, {totalCambios} cambios, invocando OnSyncCompletado");

            // Se avisa siempre que la sync termina bien, no solo si el diff detectó cambios de
            // contenido: un movimiento guardado en este dispositivo pasa de "pendiente" a
            // "sincronizado" (campo Sincronizado) sin que eso cuente como "cambio" para el diff,
            // y la UI necesita refrescarse igualmente para dejar de mostrar el aviso de pendiente.
            if (OnSyncCompletado is not null)
                await OnSyncCompletado.Invoke();
            Console.WriteLine("[HA-diag] SincronizarAsync: OnSyncCompletado terminado");
        }
        catch (Exception ex)
        {
            UltimoError = ex.Message;
            Console.WriteLine($"[HA-diag] SincronizarAsync: EXCEPCIÓN — {ex}");
        }
        finally
        {
            SincronizandoAhora = false;
            PasoActual = null;
            OnEstadoCambiado?.Invoke();
            Console.WriteLine("[HA-diag] SincronizarAsync: fin (finally)");
        }
    }

    public async Task RestablecerTombstonesAsync()
    {
        using var _ = await db.BloqueoAsync();
        await db.LimpiarTodosEliminadosAsync();
        var archivos = await drive.ListarArchivosAsync();
        var idx = new Dictionary<string, DriveFileInfo>();
        foreach (var f in archivos) idx[f.Nombre] = f;
        if (idx.TryGetValue(NombreDels, out var arc))
            await drive.EliminarArchivoAsync(arc.Id);
    }

    // Punto de entrada público para propagar desde la UI (al guardar un recurrente)
    public async Task PropagateRecurrentesAsync()
    {
        using var _ = await db.BloqueoAsync();
        await PropagarcategoriasRecurrentesAsync();
    }

    // Repara movimientos y recurrentes que apunten a un CategoriaId/CuentaId descartado
    // por la deduplicación de MergeCategoriasAsync/MergeCuentasAsync, en vez de dejarlos
    // huérfanos (lo que se veía en la UI como categoría en blanco).
    private async Task<int> AplicarRemapReferenciasAsync(
        Dictionary<string, string> remapCats, Dictionary<string, string> remapCuents)
    {
        if (remapCats.Count == 0 && remapCuents.Count == 0) return 0;

        using var _ = await db.BloqueoAsync();

        bool cambio = false;
        var movimientos = await db.ObtenerMovimientosAsync();
        foreach (var mov in movimientos)
        {
            var cat    = remapCats.GetValueOrDefault(mov.CategoriaId, mov.CategoriaId);
            var cuenta = remapCuents.GetValueOrDefault(mov.CuentaId, mov.CuentaId);
            if (cat != mov.CategoriaId || cuenta != mov.CuentaId)
            {
                mov.CategoriaId  = cat;
                mov.CuentaId     = cuenta;
                mov.ModificadoEn = DateTime.UtcNow;
                cambio = true;
            }
        }
        if (cambio) await db.ReemplazarMovimientosAsync(movimientos);

        bool cambioRec = false;
        var recurrentes = await db.ObtenerRecurrentesAsync();
        foreach (var rec in recurrentes)
        {
            var cat    = remapCats.GetValueOrDefault(rec.CategoriaId, rec.CategoriaId);
            var cuenta = remapCuents.GetValueOrDefault(rec.CuentaId, rec.CuentaId);
            if (cat != rec.CategoriaId || cuenta != rec.CuentaId)
            {
                rec.CategoriaId  = cat;
                rec.CuentaId     = cuenta;
                rec.ModificadoEn = DateTime.UtcNow;
                cambioRec = true;
            }
        }
        if (cambioRec) await db.ReemplazarRecurrentesAsync(recurrentes);

        return (cambio || cambioRec) ? 1 : 0;
    }

    // Categoría y cuenta: se propagan siempre (son clasificaciones, no afectan al histórico).
    // Importe y concepto: solo del mes en curso en adelante, para respetar el histórico real
    // de los meses ya cerrados.
    private async Task<int> PropagarcategoriasRecurrentesAsync()
    {
        using var _ = await db.BloqueoAsync();
        var recurrentes = await db.ObtenerRecurrentesAsync();
        var movimientos = await db.ObtenerMovimientosAsync();
        var recById     = recurrentes.ToDictionary(r => r.Id);
        var inicioMes   = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        bool cambio = false;
        foreach (var mov in movimientos.Where(m => m.RecurrenteId != null))
        {
            if (!recById.TryGetValue(mov.RecurrenteId!, out var rec)) continue;

            if (mov.CategoriaId != rec.CategoriaId || mov.CuentaId != rec.CuentaId)
            {
                mov.CategoriaId  = rec.CategoriaId;
                mov.CuentaId     = rec.CuentaId;
                mov.ModificadoEn = DateTime.UtcNow;
                cambio = true;
            }

            if (mov.Fecha >= inicioMes && (mov.Importe != rec.Importe || mov.Concepto != rec.Concepto))
            {
                mov.Importe      = rec.Importe;
                mov.Concepto     = rec.Concepto;
                mov.ModificadoEn = DateTime.UtcNow;
                cambio = true;
            }
        }

        if (cambio)
            await db.ReemplazarMovimientosAsync(movimientos);

        return cambio ? 1 : 0;
    }

    // --- Movimientos ---

    private async Task<int> MergeMovimientosAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local = await db.ObtenerMovimientosAsync();

        List<Movimiento> deDrive = [];
        if (idx.TryGetValue(NombreMovs, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Movimiento>>(contenido, _json) ?? [];
        }

        var merged = local.ToDictionary(m => m.Id);
        foreach (var mov in deDrive)
        {
            if (eliminados.Contains(mov.Id)) continue;
            if (!merged.TryGetValue(mov.Id, out var localMov) || mov.ModificadoEn > localMov.ModificadoEn)
                merged[mov.Id] = mov;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Si dos dispositivos generaron el mismo recurrente+periodo con IDs distintos, queda uno
        var lista = merged.Values
            .GroupBy(m => m.RecurrenteId != null ? $"{m.RecurrenteId}_{m.Periodo}" : m.Id)
            .Select(g => g.OrderByDescending(m => m.ModificadoEn).First())
            .OrderBy(m => m.Fecha)
            .ToList();

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreMovs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreMovs, json);

        foreach (var mov in lista) mov.Sincronizado = true;

        // Desde que se leyó "local" hasta aquí han pasado dos llamadas de red a Drive (pueden
        // tardar). Antes de escribir, releer el almacenamiento fresco y conservar cualquier
        // movimiento guardado o editado localmente mientras tanto (gana por ModificadoEn más
        // reciente) y respetar cualquiera que se haya borrado localmente entre medias.
        using var _ = await db.BloqueoAsync();
        var actual = await db.ObtenerMovimientosAsync();
        var idsOriginales = local.Select(m => m.Id).ToHashSet();
        var borradosMientras = idsOriginales.Except(actual.Select(m => m.Id)).ToHashSet();

        var final = lista.Where(m => !borradosMientras.Contains(m.Id)).ToDictionary(m => m.Id);
        foreach (var mov in actual)
        {
            if (eliminados.Contains(mov.Id)) continue;
            if (!final.TryGetValue(mov.Id, out var existente) || mov.ModificadoEn > existente.ModificadoEn)
                final[mov.Id] = mov;
        }
        var listaFinal = final.Values.OrderBy(m => m.Fecha).ToList();

        var listaIds  = listaFinal.Select(m => m.Id).ToHashSet();
        var localById = local.ToDictionary(m => m.Id);
        bool hayCambios = listaFinal.Count != local.Count
            || listaFinal.Any(m => !localById.ContainsKey(m.Id))
            || local.Any(m => !listaIds.Contains(m.Id))
            || listaFinal.Any(m => localById.TryGetValue(m.Id, out var l) && l.ModificadoEn != m.ModificadoEn);

        await db.ReemplazarMovimientosAsync(listaFinal);
        return hayCambios ? 1 : 0;
    }

    // --- Recurrentes ---

    private async Task<int> MergeRecurrentesAsync(Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local = await db.ObtenerRecurrentesAsync();

        List<MovimientoRecurrente> deDrive = [];
        if (idx.TryGetValue(NombreRecs, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<MovimientoRecurrente>>(contenido, _json) ?? [];
        }

        var merged = local.ToDictionary(r => r.Id);
        foreach (var rec in deDrive)
        {
            if (eliminados.Contains(rec.Id)) continue;
            if (!merged.TryGetValue(rec.Id, out var localRec) || rec.ModificadoEn > localRec.ModificadoEn)
                merged[rec.Id] = rec;
        }
        foreach (var id in eliminados) merged.Remove(id);

        var lista = merged.Values.ToList();

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreRecs, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreRecs, json);

        foreach (var rec in lista) rec.Sincronizado = true;

        // Igual que en movimientos: releer fresco antes de escribir, para no perder un
        // recurrente creado/editado/borrado localmente durante las llamadas a Drive.
        using var _ = await db.BloqueoAsync();
        var actual = await db.ObtenerRecurrentesAsync();
        var idsOriginales = local.Select(r => r.Id).ToHashSet();
        var borradosMientras = idsOriginales.Except(actual.Select(r => r.Id)).ToHashSet();

        var final = lista.Where(r => !borradosMientras.Contains(r.Id)).ToDictionary(r => r.Id);
        foreach (var rec in actual)
        {
            if (eliminados.Contains(rec.Id)) continue;
            if (!final.TryGetValue(rec.Id, out var existente) || rec.ModificadoEn > existente.ModificadoEn)
                final[rec.Id] = rec;
        }
        var listaFinal = final.Values.ToList();

        var localById = local.ToDictionary(r => r.Id);
        bool hayCambios = listaFinal.Count != local.Count
            || listaFinal.Any(r => !localById.ContainsKey(r.Id))
            || local.Any(r => !final.ContainsKey(r.Id))
            || listaFinal.Any(r => localById.TryGetValue(r.Id, out var l) && l.ModificadoEn != r.ModificadoEn);

        await db.ReemplazarRecurrentesAsync(listaFinal);
        return hayCambios ? 1 : 0;
    }

    // --- Categorías ---

    private async Task<(int cambios, Dictionary<string, string> remap)> MergeCategoriasAsync(
        Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local     = await db.ObtenerCategoriasAsync();
        var localById = local.ToDictionary(c => c.Id);

        List<Categoria> deDrive = [];
        if (idx.TryGetValue(NombreCats, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Categoria>>(contenido, _json) ?? [];
        }

        var merged = new Dictionary<string, Categoria>(localById);
        foreach (var cat in deDrive)
        {
            if (eliminados.Contains(cat.Id)) continue;
            if (!merged.TryGetValue(cat.Id, out var localCat) || cat.ModificadoEn > localCat.ModificadoEn)
                merged[cat.Id] = cat;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Dos dispositivos pueden haber creado una categoría con el mismo nombre+tipo
        // antes de sincronizar entre sí: quedan como filas distintas con el mismo
        // Nombre pero Id diferente. Nos quedamos con la más antigua (la más probable
        // de tener ya movimientos históricos) y devolvemos el remap idPerdedor->idGanador.
        var remap = new Dictionary<string, string>();
        var lista = new List<Categoria>();
        foreach (var grupo in merged.Values.GroupBy(c => (c.Nombre.Trim().ToLowerInvariant(), c.Tipo)))
        {
            var ganador = grupo.OrderBy(c => c.ModificadoEn).First();
            lista.Add(ganador);
            foreach (var perdedor in grupo.Where(c => c.Id != ganador.Id))
                remap[perdedor.Id] = ganador.Id;
        }

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCats, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCats, json);

        // Releer fresco antes de escribir: conservar cualquier categoría creada/editada/borrada
        // localmente durante las llamadas a Drive, y repetir la deduplicación por nombre+tipo
        // sobre el conjunto combinado para no dejar duplicados sueltos.
        using var _ = await db.BloqueoAsync();
        var actual = await db.ObtenerCategoriasAsync();
        var idsOriginales = local.Select(c => c.Id).ToHashSet();
        var borradosMientras = idsOriginales.Except(actual.Select(c => c.Id)).ToHashSet();

        var combinadas = lista.Where(c => !borradosMientras.Contains(c.Id)).ToDictionary(c => c.Id);
        foreach (var cat in actual)
        {
            if (eliminados.Contains(cat.Id)) continue;
            if (!combinadas.TryGetValue(cat.Id, out var existente) || cat.ModificadoEn > existente.ModificadoEn)
                combinadas[cat.Id] = cat;
        }

        var listaFinal = new List<Categoria>();
        foreach (var grupo in combinadas.Values.GroupBy(c => (c.Nombre.Trim().ToLowerInvariant(), c.Tipo)))
        {
            var ganador = grupo.OrderBy(c => c.ModificadoEn).First();
            listaFinal.Add(ganador);
            foreach (var perdedor in grupo.Where(c => c.Id != ganador.Id))
                remap[perdedor.Id] = ganador.Id;
        }

        var listaIds = listaFinal.Select(c => c.Id).ToHashSet();
        bool hayCambios = listaFinal.Count != local.Count
            || listaFinal.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || listaFinal.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn)
            || remap.Count > 0;

        await db.ReemplazarCategoriasAsync(listaFinal);
        return (hayCambios ? 1 : 0, remap);
    }

    // --- Cuentas ---

    private async Task<(int cambios, Dictionary<string, string> remap)> MergeCuentasAsync(
        Dictionary<string, DriveFileInfo> idx, HashSet<string> eliminados)
    {
        var local     = await db.ObtenerCuentasAsync();
        var localById = local.ToDictionary(c => c.Id);

        List<Cuenta> deDrive = [];
        if (idx.TryGetValue(NombreCuents, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
                deDrive = JsonSerializer.Deserialize<List<Cuenta>>(contenido, _json) ?? [];
        }

        var merged = new Dictionary<string, Cuenta>(localById);
        foreach (var cuenta in deDrive)
        {
            if (eliminados.Contains(cuenta.Id)) continue;
            if (!merged.TryGetValue(cuenta.Id, out var localCuenta) || cuenta.ModificadoEn > localCuenta.ModificadoEn)
                merged[cuenta.Id] = cuenta;
        }
        foreach (var id in eliminados) merged.Remove(id);

        // Mismo problema que con las categorías: dos dispositivos pueden haber creado
        // la misma cuenta con Ids distintos antes de sincronizar. Ver MergeCategoriasAsync.
        var remap = new Dictionary<string, string>();
        var lista = new List<Cuenta>();
        foreach (var grupo in merged.Values.GroupBy(c => c.Nombre.Trim().ToLowerInvariant()))
        {
            var ganadora = grupo.OrderBy(c => c.ModificadoEn).First();
            lista.Add(ganadora);
            foreach (var perdedora in grupo.Where(c => c.Id != ganadora.Id))
                remap[perdedora.Id] = ganadora.Id;
        }

        var json = JsonSerializer.Serialize(lista);
        if (idx.TryGetValue(NombreCuents, out var archivoExistente))
            await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
        else
            await drive.SubirArchivoAsync(NombreCuents, json);

        // Releer fresco antes de escribir: conservar cualquier cuenta creada/editada/borrada
        // localmente durante las llamadas a Drive, y repetir la deduplicación por nombre sobre
        // el conjunto combinado para no dejar duplicados sueltos.
        using var _ = await db.BloqueoAsync();
        var actual = await db.ObtenerCuentasAsync();
        var idsOriginales = local.Select(c => c.Id).ToHashSet();
        var borradosMientras = idsOriginales.Except(actual.Select(c => c.Id)).ToHashSet();

        var combinadas = lista.Where(c => !borradosMientras.Contains(c.Id)).ToDictionary(c => c.Id);
        foreach (var cuenta in actual)
        {
            if (eliminados.Contains(cuenta.Id)) continue;
            if (!combinadas.TryGetValue(cuenta.Id, out var existente) || cuenta.ModificadoEn > existente.ModificadoEn)
                combinadas[cuenta.Id] = cuenta;
        }

        var listaFinal = new List<Cuenta>();
        foreach (var grupo in combinadas.Values.GroupBy(c => c.Nombre.Trim().ToLowerInvariant()))
        {
            var ganadora = grupo.OrderBy(c => c.ModificadoEn).First();
            listaFinal.Add(ganadora);
            foreach (var perdedora in grupo.Where(c => c.Id != ganadora.Id))
                remap[perdedora.Id] = ganadora.Id;
        }

        var listaIds = listaFinal.Select(c => c.Id).ToHashSet();
        bool hayCambios = listaFinal.Count != local.Count
            || listaFinal.Any(c => !localById.ContainsKey(c.Id))
            || local.Any(c => !listaIds.Contains(c.Id))
            || listaFinal.Any(c => localById.TryGetValue(c.Id, out var l) && l.ModificadoEn != c.ModificadoEn)
            || remap.Count > 0;

        await db.ReemplazarCuentasAsync(listaFinal);
        return (hayCambios ? 1 : 0, remap);
    }

    // --- Eliminaciones ---

    private async Task<HashSet<string>> SincronizarEliminacionesAsync(Dictionary<string, DriveFileInfo> idx)
    {
        var locales = await db.ObtenerEliminadosAsync();

        HashSet<string> deDrive = [];
        if (idx.TryGetValue(NombreDels, out var arc))
        {
            var contenido = await drive.DescargarArchivoAsync(arc.Id);
            if (contenido is not null)
            {
                var lista = JsonSerializer.Deserialize<List<string>>(contenido, _json) ?? [];
                deDrive = lista.ToHashSet();
            }
        }

        var nuevasEliminaciones = deDrive.Except(locales).ToList();
        if (nuevasEliminaciones.Count > 0)
        {
            // Sin llamadas de red dentro de este bloque (la descarga ya se hizo arriba, la
            // subida de la lista de borrados se hace más abajo, fuera del bloqueo), así que
            // el bloqueo aquí es breve.
            using var _ = await db.BloqueoAsync();
            var movimientos = await db.ObtenerMovimientosAsync();
            var recurrentes = await db.ObtenerRecurrentesAsync();
            var categorias  = await db.ObtenerCategoriasAsync();
            var cuentas     = await db.ObtenerCuentasAsync();

            var ids = nuevasEliminaciones.ToHashSet();
            movimientos.RemoveAll(m => ids.Contains(m.Id));
            recurrentes.RemoveAll(r => ids.Contains(r.Id));
            categorias.RemoveAll(c => ids.Contains(c.Id));
            cuentas.RemoveAll(c => ids.Contains(c.Id));

            await db.ReemplazarMovimientosAsync(movimientos);
            await db.ReemplazarRecurrentesAsync(recurrentes);
            await db.ReemplazarCategoriasAsync(categorias);
            await db.ReemplazarCuentasAsync(cuentas);

            foreach (var id in nuevasEliminaciones)
                await db.MarcarEliminadoAsync(id);
        }

        var todos = locales.Union(deDrive).ToHashSet();
        if (todos.Count > 0)
        {
            var json = JsonSerializer.Serialize(todos.ToList());
            if (idx.TryGetValue(NombreDels, out var archivoExistente))
                await drive.ActualizarContenidoAsync(archivoExistente.Id, json);
            else
                await drive.SubirArchivoAsync(NombreDels, json);
        }

        return todos;
    }
}
