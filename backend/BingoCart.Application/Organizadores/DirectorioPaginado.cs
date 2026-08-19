using BingoCart.Application.Organizadores.Dtos;

namespace BingoCart.Application.Organizadores;

/// <summary>
/// Resultado de <see cref="IDirectorioRepository.ListarActivosAsync"/>: la página de organizadores
/// con bingo activo solicitada junto con el total real (sin paginar), calculados juntos en una sola
/// llamada a infraestructura — mismo patrón de "record dedicado para retorno correlacionado" que
/// <see cref="BingoCart.Application.Bingos.BingosPaginados"/>.
/// </summary>
public sealed record DirectorioPaginado(IReadOnlyList<DirectorioOrganizadorItem> Items, int Total);
