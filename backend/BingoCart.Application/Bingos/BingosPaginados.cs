using BingoCart.Domain.Bingos;

namespace BingoCart.Application.Bingos;

/// <summary>
/// Resultado de <see cref="IBingoRepository.ListarPorOrganizadorAsync"/>: la página de bingos
/// solicitada junto con el total real de bingos del organizador (sin paginar), calculados juntos en
/// una sola llamada a infraestructura para que Application (Block 2) pueda derivar
/// <c>TotalPaginas</c> sin una segunda consulta — mismo patrón de "record dedicado para retorno
/// correlacionado" que <see cref="BingoCart.Application.Organizadores.ResultadoAutenticacion"/> y
/// <see cref="BingoCart.Application.Auth.TokenGenerado"/>.
/// </summary>
public sealed record BingosPaginados(IReadOnlyList<Bingo> Bingos, int Total);
