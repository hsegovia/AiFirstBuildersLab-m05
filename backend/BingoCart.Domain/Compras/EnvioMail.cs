namespace BingoCart.Domain.Compras;

/// <summary>
/// Entidad de dominio: un envío de mail en la tabla outbox (spec FEAT-009b, Block 1; extendida en
/// FEAT-009c, Block 1). <see cref="TipoEnvio"/> discrimina entre confirmación de compra
/// (agrupa por <see cref="ConfirmacionId"/> todas las <see cref="Compra"/> de una misma confirmación
/// de carrito, FEAT-009a) y cancelación (referencia una única <see cref="Compra"/> vía
/// <see cref="CompraId"/>). Lógica pura, sin I/O — toda transición de estado recibe <c>ahoraUtc</c>
/// por parámetro, igual patrón que <see cref="Compra.Crear"/>.
/// </summary>
public sealed class EnvioMail
{
    public Guid Id { get; private init; }

    public TipoEnvioMail TipoEnvio { get; private init; }

    /// <summary>
    /// Seteado únicamente cuando <see cref="TipoEnvio"/> es <see cref="TipoEnvioMail.Confirmacion"/>.
    /// </summary>
    public Guid? ConfirmacionId { get; private init; }

    /// <summary>
    /// Seteado únicamente cuando <see cref="TipoEnvio"/> es <see cref="TipoEnvioMail.Cancelacion"/>.
    /// </summary>
    public Guid? CompraId { get; private init; }

    public Guid CompradorId { get; private init; }

    public EstadoEnvioMail Estado { get; private set; }

    public int Intentos { get; private set; }

    public DateTime? ProximoIntentoUtc { get; private set; }

    public DateTime FechaCreacionUtc { get; private init; }

    private EnvioMail()
    {
    }

    /// <summary>
    /// Crea un <see cref="EnvioMail"/> de tipo <see cref="TipoEnvioMail.Confirmacion"/> en estado
    /// <see cref="EstadoEnvioMail.Pendiente"/>, con <see cref="Intentos"/> en 0 y sin
    /// <see cref="ProximoIntentoUtc"/> programado.
    /// </summary>
    public static EnvioMail CrearConfirmacion(Guid confirmacionId, Guid compradorId, DateTime ahoraUtc)
    {
        return new EnvioMail
        {
            Id = Guid.NewGuid(),
            TipoEnvio = TipoEnvioMail.Confirmacion,
            ConfirmacionId = confirmacionId,
            CompraId = null,
            CompradorId = compradorId,
            Estado = EstadoEnvioMail.Pendiente,
            Intentos = 0,
            ProximoIntentoUtc = null,
            FechaCreacionUtc = ahoraUtc,
        };
    }

    /// <summary>
    /// Crea un <see cref="EnvioMail"/> de tipo <see cref="TipoEnvioMail.Cancelacion"/> en estado
    /// <see cref="EstadoEnvioMail.Pendiente"/>, con <see cref="Intentos"/> en 0 y sin
    /// <see cref="ProximoIntentoUtc"/> programado.
    /// </summary>
    public static EnvioMail CrearCancelacion(Guid compraId, Guid compradorId, DateTime ahoraUtc)
    {
        return new EnvioMail
        {
            Id = Guid.NewGuid(),
            TipoEnvio = TipoEnvioMail.Cancelacion,
            ConfirmacionId = null,
            CompraId = compraId,
            CompradorId = compradorId,
            Estado = EstadoEnvioMail.Pendiente,
            Intentos = 0,
            ProximoIntentoUtc = null,
            FechaCreacionUtc = ahoraUtc,
        };
    }

    /// <summary>
    /// Registra un intento de envío fallido. Al llegar al 3er intento (NFR-02) transiciona a
    /// <see cref="EstadoEnvioMail.Fallido"/> y deja de programar reintentos — no toca
    /// <see cref="ProximoIntentoUtc"/>. En caso contrario, permanece <see cref="EstadoEnvioMail.Pendiente"/>
    /// y programa el próximo intento 1 minuto después (NFR-01).
    /// </summary>
    public void RegistrarIntentoFallido(DateTime ahoraUtc)
    {
        Intentos++;

        if (Intentos >= 3)
        {
            Estado = EstadoEnvioMail.Fallido;
            return;
        }

        ProximoIntentoUtc = ahoraUtc.AddMinutes(1);
    }

    /// <summary>
    /// Registra el envío exitoso del mail.
    /// </summary>
    public void RegistrarExito()
    {
        Estado = EstadoEnvioMail.Exitoso;
    }
}
