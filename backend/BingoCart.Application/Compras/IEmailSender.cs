using BingoCart.Application.Compras.Dtos;

namespace BingoCart.Application.Compras;

/// <summary>
/// Puerto de envío de mail (spec FEAT-009b, Block 2). Recibe EXCLUSIVAMENTE el DTO de Application
/// <see cref="EnvioMailMensaje"/> — nunca un tipo de MailKit, mismo criterio de separación de capas
/// ya aplicado a <c>ICompraRepository</c> con <c>DbUpdateException</c> (AGENTS.md, "Layer
/// separation"). Infrastructure lo implementa en Block 3 vía MailKit.
/// </summary>
public interface IEmailSender
{
    Task EnviarAsync(EnvioMailMensaje mensaje);
}
