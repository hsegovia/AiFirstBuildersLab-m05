namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Estado de la cuenta del comprador (spec FEAT-009d, Block 5/6) — nunca incluye
/// <c>ContrasenaActual</c> ni ningún derivado de ella (AC-16). Doble uso: es a la vez el shape que
/// devuelve <see cref="ICompradorIdentityGateway.ObtenerCuentaAsync"/> (lectura interna, para poder
/// calcular qué campos cambiaron sin loguear valores) y el `Response` del `PUT` del Block 6 — ambos
/// exponen exactamente los mismos cuatro campos de negocio, así que un solo `record` alcanza sin
/// duplicar el shape entre Application e Infrastructure.
/// </summary>
public sealed record CuentaCompradorResponse(string Apellido, string Nombre, string Cuit, string Mail);
