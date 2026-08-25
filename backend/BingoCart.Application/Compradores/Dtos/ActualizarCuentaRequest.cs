using System.ComponentModel.DataAnnotations;

namespace BingoCart.Application.Compradores.Dtos;

/// <summary>
/// Request de actualización de datos de cuenta del comprador (spec FEAT-009d, Block 5/6, FR-06).
/// Los cuatro primeros campos viajan y se persisten juntos (A-03): una solicitud parcial se
/// rechaza, no se interpreta como actualización de lo enviado. <c>ContrasenaActual</c> NO es un dato
/// de la cuenta — es la prueba de identidad que autoriza el cambio (FR-13), se verifica y se
/// descarta, nunca se persiste ni se devuelve. Mismo criterio que <c>RegistrarCompradorRequest</c>:
/// <c>Apellido</c>/<c>Nombre</c>/<c>Mail</c> llevan DataAnnotations de forma; <c>Cuit</c> solo lleva
/// <c>[Required]</c> (spec FEAT-009d, Block 6: un valor ausente/vacío se rechaza en el binding con
/// 400 "DatosInvalidos") y nada más — su validación de longitud/dígito verificador sigue siendo
/// responsabilidad de Domain vía <c>CuitValidator</c>, para poder distinguir cuál de las dos reglas
/// falló, AC-09; <c>ContrasenaActual</c> tampoco lleva anotaciones de formato/longitud a propósito:
/// aplicar reglas de complejidad acá filtraría información sobre la contraseña almacenada.
/// </summary>
public sealed record ActualizarCuentaRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Apellido,
    [Required, StringLength(100, MinimumLength = 1)] string Nombre,
    [Required] string Cuit,
    [Required, EmailAddress] string Mail,
    [Required] string ContrasenaActual);
