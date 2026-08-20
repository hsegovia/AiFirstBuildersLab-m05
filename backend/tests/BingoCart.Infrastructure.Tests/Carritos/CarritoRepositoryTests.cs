using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Application.Carritos;
using BingoCart.Infrastructure.Carritos;
using StackExchange.Redis;

namespace BingoCart.Infrastructure.Tests.Carritos;

/// <summary>
/// Tests de integración de <see cref="CarritoRepository"/> contra Redis real (spec FEAT-008b, Block
/// 1) — mismo patrón que <c>DescubrimientoRepositoryTests</c> contra SQL Server real: cada test
/// limpia ÚNICAMENTE las claves que él mismo creó (prefijo con un GUID propio en `sesionId`/
/// `cartonId`, Rule #0 de testing.instructions.md — nunca se hace <c>FLUSHDB</c> sobre datos ajenos),
/// y las borra en <see cref="DisposeAsync"/>.
/// </summary>
public sealed class CarritoRepositoryTests : IAsyncLifetime
{
    // Mismo puerto remapeado que docker-compose.yml (Block 1) — Redis real de desarrollo local,
    // nunca un mock: NFR-01 exige verificar atomicidad real, no simulada.
    private const string ConnectionString = "localhost:16379";

    private ConnectionMultiplexer _connectionMultiplexer = null!;
    private ICarritoRepository _repository = null!;
    private readonly List<string> _clavesABorrar = new();

    public async Task InitializeAsync()
    {
        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        _repository = new CarritoRepository(_connectionMultiplexer);
    }

    public async Task DisposeAsync()
    {
        var db = _connectionMultiplexer.GetDatabase();
        foreach (var clave in _clavesABorrar)
        {
            await db.KeyDeleteAsync(clave);
        }

        await _connectionMultiplexer.DisposeAsync();
    }

    // Registra las claves creadas por un test para borrarlas en DisposeAsync, sin tocar ninguna
    // clave de otra sesión/cartón (Rule #0).
    private void RegistrarClaves(string sesionId, params Guid[] cartonIds)
    {
        _clavesABorrar.Add($"carrito:{sesionId}");
        _clavesABorrar.Add($"descartados:{sesionId}");
        foreach (var cartonId in cartonIds)
        {
            _clavesABorrar.Add($"reservado:carton:{cartonId}");
        }
    }

    private static string NuevaSesionId() => Guid.NewGuid().ToString();

    [Fact]
    public async Task IntentarAgregarAsync_ConCartonLibre_DevuelveTrueYObtenerItemsLoIncluyeConElPrecioCorrecto()
    {
        var sesionId = NuevaSesionId();
        var cartonId = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonId);

        var agregado = await _repository.IntentarAgregarAsync(sesionId, cartonId, 150.50m, TimeSpan.FromMinutes(5));
        var items = await _repository.ObtenerItemsAsync(sesionId);

        Assert.True(agregado);
        Assert.Contains(items, i => i.CartonId == cartonId && i.PrecioUnitario == 150.50m);
    }

    [Fact]
    public async Task IntentarAgregarAsync_SobreCartonYaReservadoPorOtraSesion_DevuelveFalseYNoLoIncluyeEnElCarritoDelPerdedor()
    {
        var sesionGanadora = NuevaSesionId();
        var sesionPerdedora = NuevaSesionId();
        var cartonId = Guid.NewGuid();
        RegistrarClaves(sesionGanadora, cartonId);
        RegistrarClaves(sesionPerdedora);

        await _repository.IntentarAgregarAsync(sesionGanadora, cartonId, 100m, TimeSpan.FromMinutes(5));
        var segundoIntento = await _repository.IntentarAgregarAsync(sesionPerdedora, cartonId, 100m, TimeSpan.FromMinutes(5));
        var itemsPerdedora = await _repository.ObtenerItemsAsync(sesionPerdedora);

        Assert.False(segundoIntento);
        Assert.Empty(itemsPerdedora);
    }

    [Fact]
    public async Task IntentarAgregarAsync_DeUnSegundoCartonEnLaMismaSesion_DevuelveAmbosYRefrescaElTtlDelPrimero()
    {
        var sesionId = NuevaSesionId();
        var primerCartonId = Guid.NewGuid();
        var segundoCartonId = Guid.NewGuid();
        RegistrarClaves(sesionId, primerCartonId, segundoCartonId);

        await _repository.IntentarAgregarAsync(sesionId, primerCartonId, 100m, TimeSpan.FromSeconds(10));

        var db = _connectionMultiplexer.GetDatabase();
        var ttlAntes = await db.KeyTimeToLiveAsync($"reservado:carton:{primerCartonId}");

        // Segundo agregado con un TTL mucho más largo — si el primero se refresca correctamente
        // (FR-07), su TTL nuevo debe acercarse a este valor, no al original (10s) de arriba.
        await _repository.IntentarAgregarAsync(sesionId, segundoCartonId, 200m, TimeSpan.FromMinutes(5));
        var ttlDespues = await db.KeyTimeToLiveAsync($"reservado:carton:{primerCartonId}");

        var items = await _repository.ObtenerItemsAsync(sesionId);

        Assert.Equal(2, items.Count);
        Assert.NotNull(ttlAntes);
        Assert.NotNull(ttlDespues);
        Assert.True(ttlDespues!.Value > ttlAntes!.Value);
        Assert.True(ttlDespues.Value > TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task IntentarAgregarAsync_ConTtlCortoTrasExpirar_LiberaElCartonParaOtraSesion()
    {
        var sesionId = NuevaSesionId();
        var cartonId = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonId);

        await _repository.IntentarAgregarAsync(sesionId, cartonId, 100m, TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var itemsTrasExpirar = await _repository.ObtenerItemsAsync(sesionId);

        var otraSesion = NuevaSesionId();
        RegistrarClaves(otraSesion);
        var agregadoOtraSesion = await _repository.IntentarAgregarAsync(otraSesion, cartonId, 100m, TimeSpan.FromMinutes(5));

        Assert.Empty(itemsTrasExpirar);
        Assert.True(agregadoOtraSesion);
    }

    [Fact]
    public async Task QuitarAsync_ConOtrosDosItemsEnElCarrito_NoModificaElTtlDeLasClavesRestantes()
    {
        var sesionId = NuevaSesionId();
        var cartonAQuitar = Guid.NewGuid();
        var cartonRestanteUno = Guid.NewGuid();
        var cartonRestanteDos = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonAQuitar, cartonRestanteUno, cartonRestanteDos);

        var ttl = TimeSpan.FromMinutes(5);
        await _repository.IntentarAgregarAsync(sesionId, cartonAQuitar, 100m, ttl);
        await _repository.IntentarAgregarAsync(sesionId, cartonRestanteUno, 100m, ttl);
        await _repository.IntentarAgregarAsync(sesionId, cartonRestanteDos, 100m, ttl);

        var db = _connectionMultiplexer.GetDatabase();
        var ttlCarritoAntes = await db.KeyTimeToLiveAsync($"carrito:{sesionId}");
        var ttlRestanteUnoAntes = await db.KeyTimeToLiveAsync($"reservado:carton:{cartonRestanteUno}");
        var ttlRestanteDosAntes = await db.KeyTimeToLiveAsync($"reservado:carton:{cartonRestanteDos}");

        await _repository.QuitarAsync(sesionId, cartonAQuitar);

        var ttlCarritoDespues = await db.KeyTimeToLiveAsync($"carrito:{sesionId}");
        var ttlRestanteUnoDespues = await db.KeyTimeToLiveAsync($"reservado:carton:{cartonRestanteUno}");
        var ttlRestanteDosDespues = await db.KeyTimeToLiveAsync($"reservado:carton:{cartonRestanteDos}");

        Assert.NotNull(ttlCarritoAntes);
        Assert.NotNull(ttlRestanteUnoAntes);
        Assert.NotNull(ttlRestanteDosAntes);
        // Margen de milisegundos: el TTL decrece naturalmente entre las lecturas "antes" y
        // "después" por el tiempo que tarda en ejecutarse QuitarAsync — NFR-03 exige que
        // QuitarAsync NO lo reinicie a los 5 minutos originales, no que sea exactamente el mismo
        // valor al milisegundo.
        var margen = TimeSpan.FromSeconds(2);
        Assert.True((ttlCarritoAntes!.Value - ttlCarritoDespues!.Value) < margen);
        Assert.True((ttlRestanteUnoAntes!.Value - ttlRestanteUnoDespues!.Value) < margen);
        Assert.True((ttlRestanteDosAntes!.Value - ttlRestanteDosDespues!.Value) < margen);

        var items = await _repository.ObtenerItemsAsync(sesionId);
        Assert.DoesNotContain(items, i => i.CartonId == cartonAQuitar);
    }

    [Fact]
    public async Task AgregarDescartadosAsync_ConDosCartonesLuegoUnTercero_ObtenerDescartadosDevuelveLosTresAcumulados()
    {
        var sesionId = NuevaSesionId();
        var primerLote = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var segundoLote = new[] { Guid.NewGuid() };
        RegistrarClaves(sesionId);

        await _repository.AgregarDescartadosAsync(sesionId, primerLote, TimeSpan.FromMinutes(30));
        var trasPrimerLote = await _repository.ObtenerDescartadosAsync(sesionId);

        await _repository.AgregarDescartadosAsync(sesionId, segundoLote, TimeSpan.FromMinutes(30));
        var trasSegundoLote = await _repository.ObtenerDescartadosAsync(sesionId);

        Assert.Equal(2, trasPrimerLote.Count);
        Assert.Equal(3, trasSegundoLote.Count);
        Assert.All(primerLote.Concat(segundoLote), id => Assert.Contains(id, trasSegundoLote));
    }

    [Fact]
    public async Task IntentarAgregarAsync_DosSesionesConcurrentesSobreElMismoCarton_ExactamenteUnaGana()
    {
        var sesionUno = NuevaSesionId();
        var sesionDos = NuevaSesionId();
        var cartonId = Guid.NewGuid();
        RegistrarClaves(sesionUno, cartonId);
        RegistrarClaves(sesionDos);

        var tareaUno = _repository.IntentarAgregarAsync(sesionUno, cartonId, 100m, TimeSpan.FromMinutes(5));
        var tareaDos = _repository.IntentarAgregarAsync(sesionDos, cartonId, 100m, TimeSpan.FromMinutes(5));
        var resultados = await Task.WhenAll(tareaUno, tareaDos);

        Assert.Single(resultados, r => r);
        Assert.Single(resultados, r => !r);

        var itemsUno = await _repository.ObtenerItemsAsync(sesionUno);
        var itemsDos = await _repository.ObtenerItemsAsync(sesionDos);
        var ganadorEsUno = resultados[0];

        if (ganadorEsUno)
        {
            Assert.Contains(itemsUno, i => i.CartonId == cartonId);
            Assert.Empty(itemsDos);
        }
        else
        {
            Assert.Contains(itemsDos, i => i.CartonId == cartonId);
            Assert.Empty(itemsUno);
        }
    }

    // A partir de acá: tests de RevalidarReservasAsync/LiberarCarritoConfirmadoAsync (spec
    // FEAT-009a, Block 1).

    [Fact]
    public async Task RevalidarReservasAsync_ConTodasLasReservasVigentes_DevuelveEsValidoTrueConLosPreciosCorrectos()
    {
        var sesionId = NuevaSesionId();
        var cartonUno = Guid.NewGuid();
        var cartonDos = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonUno, cartonDos);

        await _repository.IntentarAgregarAsync(sesionId, cartonUno, 100m, TimeSpan.FromMinutes(5));
        await _repository.IntentarAgregarAsync(sesionId, cartonDos, 200m, TimeSpan.FromMinutes(5));

        var resultado = await _repository.RevalidarReservasAsync(sesionId);

        Assert.True(resultado.EsValido);
        Assert.Empty(resultado.CartonIdsInvalidos);
        Assert.Equal(2, resultado.Items.Count);
        Assert.Contains(resultado.Items, i => i.CartonId == cartonUno && i.PrecioUnitario == 100m);
        Assert.Contains(resultado.Items, i => i.CartonId == cartonDos && i.PrecioUnitario == 200m);
    }

    [Fact]
    public async Task RevalidarReservasAsync_ConUnaReservaYaExpirada_DevuelveEsValidoFalseConEseCartonIdInvalido()
    {
        var sesionId = NuevaSesionId();
        var cartonVigente = Guid.NewGuid();
        var cartonExpirado = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonVigente, cartonExpirado);

        await _repository.IntentarAgregarAsync(sesionId, cartonVigente, 100m, TimeSpan.FromMinutes(5));
        await _repository.IntentarAgregarAsync(sesionId, cartonExpirado, 100m, TimeSpan.FromMinutes(5));

        // Borra directamente `reservado:carton:{cartonExpirado}` (sin pasar por
        // IntentarAgregarAsync/TTL real) — mismo mecanismo que Block 3 usa para simular una reserva
        // vencida sin esperar los 5 minutos reales. Simular con un TTL corto no sirve acá:
        // ScriptIntentarAgregar refresca el TTL de TODAS las reservas del carrito a cada llamada
        // (FR-07), así que un TTL corto en el segundo agregado también reduciría el del primero.
        var db = _connectionMultiplexer.GetDatabase();
        await db.KeyDeleteAsync($"reservado:carton:{cartonExpirado}");

        var resultado = await _repository.RevalidarReservasAsync(sesionId);

        Assert.False(resultado.EsValido);
        Assert.Empty(resultado.Items);
        Assert.Equal(new[] { cartonExpirado }, resultado.CartonIdsInvalidos);
    }

    [Fact]
    public async Task RevalidarReservasAsync_NuncaBorraNingunaClave()
    {
        var sesionId = NuevaSesionId();
        var cartonId = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonId);

        await _repository.IntentarAgregarAsync(sesionId, cartonId, 100m, TimeSpan.FromMinutes(5));

        var db = _connectionMultiplexer.GetDatabase();
        var carritoExisteAntes = await db.KeyExistsAsync($"carrito:{sesionId}");
        var reservadoExisteAntes = await db.KeyExistsAsync($"reservado:carton:{cartonId}");

        await _repository.RevalidarReservasAsync(sesionId);

        var carritoExisteDespues = await db.KeyExistsAsync($"carrito:{sesionId}");
        var reservadoExisteDespues = await db.KeyExistsAsync($"reservado:carton:{cartonId}");

        Assert.True(carritoExisteAntes);
        Assert.True(reservadoExisteAntes);
        Assert.True(carritoExisteDespues);
        Assert.True(reservadoExisteDespues);
    }

    [Fact]
    public async Task LiberarCarritoConfirmadoAsync_BorraElCarritoYTodasLasReservasPasadas()
    {
        var sesionId = NuevaSesionId();
        var cartonUno = Guid.NewGuid();
        var cartonDos = Guid.NewGuid();
        RegistrarClaves(sesionId, cartonUno, cartonDos);

        await _repository.IntentarAgregarAsync(sesionId, cartonUno, 100m, TimeSpan.FromMinutes(5));
        await _repository.IntentarAgregarAsync(sesionId, cartonDos, 200m, TimeSpan.FromMinutes(5));

        await _repository.LiberarCarritoConfirmadoAsync(sesionId, new[] { cartonUno, cartonDos });

        var db = _connectionMultiplexer.GetDatabase();
        Assert.False(await db.KeyExistsAsync($"carrito:{sesionId}"));
        Assert.False(await db.KeyExistsAsync($"reservado:carton:{cartonUno}"));
        Assert.False(await db.KeyExistsAsync($"reservado:carton:{cartonDos}"));
    }

    [Fact]
    public async Task RevalidarReservasAsync_DosSesionesConcurrentesQueComparteUnCarton_CadaUnaVeInvalidoSoloElAjeno()
    {
        var sesionUno = NuevaSesionId();
        var sesionDos = NuevaSesionId();
        var cartonPropioDeUno = Guid.NewGuid();
        var cartonCompartido = Guid.NewGuid();
        RegistrarClaves(sesionUno, cartonPropioDeUno, cartonCompartido);
        RegistrarClaves(sesionDos);

        // El carrito de sesionUno reserva realmente `cartonCompartido` (queda como propietario en
        // Redis); sesionDos solo lo tiene en SU hash de carrito (simulando una reserva ya perdida
        // contra otra sesión) sin ser la propietaria real — mismo mecanismo que
        // "reserva perdida"/expirada, sin esperar un TTL real.
        await _repository.IntentarAgregarAsync(sesionUno, cartonPropioDeUno, 100m, TimeSpan.FromMinutes(5));
        await _repository.IntentarAgregarAsync(sesionUno, cartonCompartido, 150m, TimeSpan.FromMinutes(5));

        var db = _connectionMultiplexer.GetDatabase();
        await db.HashSetAsync($"carrito:{sesionDos}", cartonCompartido.ToString(), "150");

        var tareaUno = _repository.RevalidarReservasAsync(sesionUno);
        var tareaDos = _repository.RevalidarReservasAsync(sesionDos);
        var resultados = await Task.WhenAll(tareaUno, tareaDos);

        var resultadoUno = resultados[0];
        var resultadoDos = resultados[1];

        Assert.True(resultadoUno.EsValido);
        Assert.Contains(resultadoUno.Items, i => i.CartonId == cartonCompartido);

        Assert.False(resultadoDos.EsValido);
        Assert.Equal(new[] { cartonCompartido }, resultadoDos.CartonIdsInvalidos);
    }
}
