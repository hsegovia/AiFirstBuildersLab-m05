using BingoCart.Application.Compras.Dtos;

namespace BingoCart.Application.Compras;

/// <summary>
/// Resultado de <see cref="ICompraRepository.ListarCartonesDelCompradorAsync"/>: la página de
/// cartones adquiridos solicitada, ya proyectada a <see cref="CartonAdquiridoResponse"/>, junto con
/// el total real (sin paginar) — spec FEAT-009d, Block 3. A diferencia de
/// <see cref="ComprasPaginadas"/> (que retiene <c>Compra</c> + monto y deja a Application mapear al
/// DTO final), acá <c>Items</c> ya es el DTO de respuesta: el precedente que este bloque sigue
/// (<c>DirectorioRepository.ListarActivosAsync</c>) proyecta directo con <c>Select()</c> a un tipo ya
/// terminado, sin materializar las entidades intermedias del join (<c>Carton</c>/<c>Bingo</c>/
/// <c>ApplicationUser</c>).
/// </summary>
public sealed record CartonesAdquiridosPaginados(IReadOnlyList<CartonAdquiridoResponse> Items, int Total);
