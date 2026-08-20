using BingoCart.Application.Descubrimiento.Dtos;

namespace BingoCart.Application.Descubrimiento;

/// <summary>
/// Puerto de orquestación del descubrimiento público de cartones (spec FEAT-008a, Block 2) — el
/// único punto que <see cref="Api.Controllers.CartonesController"/> conoce. Implementado por
/// <see cref="DescubrimientoService"/>, que traduce <see cref="IDescubrimientoRepository"/> +
/// <see cref="Dtos.BingoResumen"/> a <see cref="CartonDescubiertoResponse"/>.
/// </summary>
public interface IDescubrimientoService
{
    /// <summary>
    /// Método 1 (FR-01): hasta 5 cartones al azar entre todos los bingos vigentes de cualquier
    /// organizador.
    /// </summary>
    Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirGlobalAsync();

    /// <summary>
    /// Método 2 (FR-05): hasta 5 cartones al azar del bingo vigente de <paramref name="organizadorId"/>.
    /// </summary>
    /// <exception cref="BingoCart.Domain.Organizadores.Exceptions.OrganizadorNoEncontradoException">
    /// <paramref name="organizadorId"/> no corresponde a ningún organizador registrado.
    /// </exception>
    Task<IReadOnlyList<CartonDescubiertoResponse>> DescubrirPorOrganizadorAsync(Guid organizadorId);
}
