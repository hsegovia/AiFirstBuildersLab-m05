using BingoCart.Domain.Compras;

namespace BingoCart.Domain.Tests.Compras;

/// <summary>
/// Tests de <see cref="EnvioMail"/> — entidad pura del outbox de mail (spec FEAT-009b, Block 1).
/// Sin I/O: toda la lógica de transición de estado recibe <c>ahoraUtc</c> por parámetro, igual
/// patrón que <see cref="Compra.Crear"/>.
/// </summary>
public class EnvioMailTests
{
    [Fact]
    public void Crear_DevuelveEstadoInicialPendienteConCeroIntentos()
    {
        var confirmacionId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var ahoraUtc = DateTime.UtcNow;

        var envio = EnvioMail.Crear(confirmacionId, compradorId, ahoraUtc);

        Assert.NotEqual(Guid.Empty, envio.Id);
        Assert.Equal(confirmacionId, envio.ConfirmacionId);
        Assert.Equal(compradorId, envio.CompradorId);
        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);
        Assert.Equal(0, envio.Intentos);
        Assert.Null(envio.ProximoIntentoUtc);
        Assert.Equal(ahoraUtc, envio.FechaCreacionUtc);
    }

    [Fact]
    public void RegistrarIntentoFallido_AntesDelTercerIntento_SigueEnPendienteConProximoIntentoEnUnMinuto()
    {
        var ahoraCreacion = DateTime.UtcNow;
        var envio = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraCreacion);
        var ahoraFallo = ahoraCreacion.AddMinutes(5);

        envio.RegistrarIntentoFallido(ahoraFallo);

        Assert.Equal(1, envio.Intentos);
        Assert.Equal(EstadoEnvioMail.Pendiente, envio.Estado);
        Assert.Equal(ahoraFallo.AddMinutes(1), envio.ProximoIntentoUtc);
    }

    [Fact]
    public void RegistrarIntentoFallido_EnElTercerIntento_TransicionaAFallido()
    {
        var ahoraCreacion = DateTime.UtcNow;
        var envio = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), ahoraCreacion);
        var ahoraFallo1 = ahoraCreacion.AddMinutes(1);
        var ahoraFallo2 = ahoraCreacion.AddMinutes(2);
        var ahoraFallo3 = ahoraCreacion.AddMinutes(3);

        envio.RegistrarIntentoFallido(ahoraFallo1);
        envio.RegistrarIntentoFallido(ahoraFallo2);
        var proximoIntentoTrasSegundoFallo = envio.ProximoIntentoUtc;
        envio.RegistrarIntentoFallido(ahoraFallo3);

        Assert.Equal(3, envio.Intentos);
        Assert.Equal(EstadoEnvioMail.Fallido, envio.Estado);
        // No debe seguir programando reintentos en el 3er fallo: ProximoIntentoUtc queda como
        // quedó tras el 2do fallo (NFR-02: se deja de reintentar, no se toca el campo).
        Assert.Equal(proximoIntentoTrasSegundoFallo, envio.ProximoIntentoUtc);
    }

    [Fact]
    public void RegistrarExito_FijaEstadoExitoso()
    {
        var envio = EnvioMail.Crear(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        envio.RegistrarExito();

        Assert.Equal(EstadoEnvioMail.Exitoso, envio.Estado);
    }
}
