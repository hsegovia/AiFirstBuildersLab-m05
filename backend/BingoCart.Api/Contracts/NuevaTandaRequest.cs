using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BingoCart.Api.Contracts;

/// <summary>
/// Contrato público de <c>POST /api/carrito/tandas/nueva</c> (spec FEAT-008b, Block 3).
/// <see cref="CartonIdsDescartados"/> se bindea vía el model binding normal de ASP.NET Core como
/// <see cref="IReadOnlyList{T}"/> de <see cref="Guid"/> — nunca como <c>List&lt;string&gt;</c>
/// parseado a mano: la cadena de tipos que garantiza que el <c>NOT IN (...)</c> de
/// <c>DescubrimientoRepository</c> (Block 2) nunca concatena input crudo de request depende de que
/// cualquier elemento no conforme a <c>Guid</c> del array JSON sea rechazado automáticamente con 400
/// antes de que este action se ejecute (restricción de seguridad confirmada en la revisión de
/// Block 2, no reabierta acá). <see cref="OrganizadorId"/> nulo → Método 1 (global); presente →
/// Método 2.
/// <see cref="ValidateNeverAttribute"/> en <see cref="CartonIdsDescartados"/> (corrección puntual,
/// revisión de Block 3, Hallazgo 2): ASP.NET Core 8 infiere un <c>[Required]</c> implícito sobre
/// todo parámetro de tipo referencia no-nullable de un record bindeado desde el body — así que un
/// cliente que omite el campo (o lo manda <c>null</c> explícito) recibía 400 "field is required"
/// ANTES de llegar al action, no el 500 por <c>NullReferenceException</c> que describía el hallazgo
/// original (verificado empíricamente: el mensaje real es
/// <c>{"error":"DatosInvalidos","message":"The CartonIdsDescartados field is required."}</c>, no una
/// excepción no controlada). <c>[ValidateNever]</c> desactiva ese required implícito SOLO para este
/// parámetro — no toca su tipo (sigue siendo <see cref="IReadOnlyList{T}"/> no-nullable a nivel de
/// compilación) ni agrega <c>[Required]</c>; el fallback en <c>CarritoController.NuevaTanda</c> es
/// el que evita el <c>NullReferenceException</c> real una vez que el binding deja pasar el valor
/// nulo hasta el action. Se aplica con AMBOS target-specifiers (<c>param:</c> y <c>property:</c>):
/// ASP.NET Core valida el grafo bindeado recorriendo <c>ModelMetadata.Properties</c>, así que solo
/// <c>property:</c> alcanza para la validación en sí — pero se deja también <c>param:</c> porque es
/// el target por el que el compilador aplica el atributo por defecto en un parámetro posicional de
/// record sin target explícito, y omitirlo sería inconsistente con esa convención.
/// </summary>
public sealed record NuevaTandaRequest(
    Guid? OrganizadorId,
    [property: ValidateNever][param: ValidateNever] IReadOnlyList<Guid> CartonIdsDescartados);
