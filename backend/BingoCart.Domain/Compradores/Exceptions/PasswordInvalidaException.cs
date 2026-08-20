using BingoCart.Domain.Common;

namespace BingoCart.Domain.Compradores.Exceptions;

/// <summary>
/// Se lanza cuando la contraseña provista no cumple la política configurada en ASP.NET Core
/// Identity (FR-02). El mensaje incluye el detalle de las reglas incumplidas (tal como las reporta
/// Identity), pero nunca la contraseña real: es un dato sensible que nunca debe llegar a un log ni a
/// una respuesta HTTP (NFR-02).
/// </summary>
public sealed class PasswordInvalidaException : DomainException
{
    public IReadOnlyList<string> Errores { get; }

    public PasswordInvalidaException(IReadOnlyList<string> errores)
        : base(ConstruirMensaje(errores))
    {
        Errores = errores;
    }

    private static string ConstruirMensaje(IReadOnlyList<string> errores)
    {
        return "La contraseña no cumple la política requerida: " + string.Join("; ", errores);
    }
}
