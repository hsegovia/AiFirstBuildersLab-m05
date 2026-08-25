using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando la contraseña actual enviada junto con una actualización de datos de cuenta
/// (FR-13, AC-16) no coincide con la almacenada. Es la PRIMERA verificación de
/// <c>CompradorService.ActualizarCuentaAsync</c>, antes de validar el CUIT y antes de consultar
/// colisiones de mail/CUIT: verificarla al final permitiría usar esas respuestas como oráculo de
/// enumeración sin conocer la contraseña (mitigación de R-04). El mensaje es genérico a propósito —
/// no distingue "contraseña vacía" de "contraseña que no coincide"— y la contraseña en sí nunca se
/// incluye acá ni en ningún log, ni siquiera enmascarada.
/// </summary>
public sealed class ContrasenaIncorrectaException : DomainException
{
    public ContrasenaIncorrectaException(string message)
        : base(message)
    {
    }
}
