using BingoCart.Application.Auth;
using BingoCart.Application.Compradores.Dtos;
using BingoCart.Application.Organizadores;
using BingoCart.Domain.Auth.Exceptions;
using BingoCart.Domain.Compradores;
using BingoCart.Domain.Compradores.Exceptions;
using BingoCart.Domain.Organizadores;
using Microsoft.Extensions.Logging;

namespace BingoCart.Application.Compradores;

/// <summary>
/// Orquesta el registro, el login y la actualización de datos de cuenta de comprador (spec
/// FEAT-009a Block 2, spec FEAT-009d Block 5) — calco exacto de
/// <see cref="Organizadores.OrganizadorService.RegistrarAsync"/>/
/// <see cref="Organizadores.OrganizadorService.AutenticarAsync"/> (mismo orden: Domain valida →
/// unicidad de mail → gateway → JWT con `rol: "Comprador"`). No hace I/O propio: toda la
/// persistencia/verificación de credenciales pasa por <see cref="ICompradorIdentityGateway"/> y
/// <see cref="ICompradorCuentaRepository"/>, ambos inyectados.
/// </summary>
public sealed class CompradorService : ICompradorService
{
    private const string Rol = "Comprador";

    // Ventana de la regla de los 60 minutos (FR-07, NFR-05, D-01, D-02): [ahora, ahora + 60 min].
    // Vive en Application, no en el repositorio (Infrastructure) ni en la base de datos — AGENTS.md,
    // "no guardar reglas de negocio en la base de datos", aplicado también a la capa de datos en
    // general: el repositorio solo filtra contra los límites que este service calcula y le pasa.
    private const int VentanaModificacionMinutos = 60;

    private readonly ICompradorIdentityGateway _gateway;
    private readonly ICompradorCuentaRepository _cuentaRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompradorService> _logger;

    public CompradorService(
        ICompradorIdentityGateway gateway,
        ICompradorCuentaRepository cuentaRepository,
        IJwtTokenService jwtTokenService,
        TimeProvider timeProvider,
        ILogger<CompradorService> logger)
    {
        _gateway = gateway;
        _cuentaRepository = cuentaRepository;
        _jwtTokenService = jwtTokenService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RegistrarCompradorResponse> RegistrarAsync(RegistrarCompradorRequest request)
    {
        // (1) Domain valida CUIT y lanza CuitInvalidoException sin haber llamado todavía al gateway.
        var comprador = Comprador.Crear(request.Apellido, request.Nombre, request.Cuit, request.Mail);

        // (2) Unicidad de mail: si ya existe, no se llega a intentar crear el usuario de Identity.
        var mailYaExiste = await _gateway.ExisteMailAsync(comprador.Mail);
        if (mailYaExiste)
        {
            throw new MailYaRegistradoException(
                "El mail ingresado ya pertenece a una cuenta existente.");
        }

        // (3) Política de password delegada íntegramente a Identity vía el gateway.
        var resultado = await _gateway.CrearUsuarioAsync(comprador, request.Password);
        if (!resultado.Exitoso)
        {
            throw new PasswordInvalidaException(resultado.Errores);
        }

        return new RegistrarCompradorResponse(comprador.Id, comprador.Apellido, comprador.Nombre, comprador.Mail);
    }

    public async Task<LoginCompradorResponse> AutenticarAsync(LoginCompradorRequest request)
    {
        var resultado = await _gateway.AutenticarAsync(request.Mail, request.Password);

        if (resultado.Estado != EstadoAutenticacion.Exitoso)
        {
            // Mismo tipo y mismo mensaje para CredencialesInvalidas y CuentaBloqueada (mismo
            // criterio que OrganizadorService): un 401 nunca debe confirmar por sí solo que la
            // cuenta existe o está bloqueada.
            throw new CredencialesInvalidasException();
        }

        // Invariante de ICompradorIdentityGateway.AutenticarAsync: Estado == Exitoso siempre viene
        // con OrganizadorId poblado (mismo campo reutilizado de ResultadoAutenticacion, aquí
        // representa el id del comprador). Se refuerza explícitamente acá en vez de un `!.Value`.
        if (resultado.OrganizadorId is not { } compradorId)
        {
            throw new InvalidOperationException(
                "ICompradorIdentityGateway.AutenticarAsync devolvió Exitoso sin OrganizadorId.");
        }

        var tokenGenerado = _jwtTokenService.GenerarToken(compradorId, request.Mail, Rol);

        return new LoginCompradorResponse(tokenGenerado.Token, tokenGenerado.ExpiraEnUtc);
    }

    /// <summary>
    /// Actualiza los cuatro datos de cuenta del comprador (spec FEAT-009d, Block 5) — actualización
    /// TOTAL, no parcial (A-03): si cualquier verificación falla, no se modifica nada, porque todas
    /// ocurren antes de la primera escritura (paso 7). El orden es estricto y no es estético
    /// (mitigación de R-04): la contraseña se verifica PRIMERO, antes del CUIT y antes de las
    /// colisiones — de lo contrario, alguien con una sesión robada podría usar "ese mail/CUIT ya
    /// está en uso" como oráculo de enumeración sin conocer la contraseña.
    /// </summary>
    public async Task<CuentaCompradorResponse> ActualizarCuentaAsync(Guid compradorId, ActualizarCuentaRequest request)
    {
        // (1) Contraseña actual: la prueba de identidad que autoriza el resto (FR-13, AC-16). Si
        // falla, ninguna otra validación se ejecuta.
        var passwordCorrecta = await _gateway.VerificarPasswordAsync(compradorId, request.ContrasenaActual);
        if (!passwordCorrecta)
        {
            throw new ContrasenaIncorrectaException("La contraseña actual ingresada no es correcta.");
        }

        // (2) CUIT: longitud y dígito verificador evaluados por separado (AC-09), reutilizando
        // CuitValidator tal cual — mismo criterio que Comprador.Crear.
        if (!CuitValidator.TieneLongitudValida(request.Cuit))
        {
            throw new CuitInvalidoException("El CUIT debe tener exactamente 11 dígitos numéricos.");
        }

        if (!CuitValidator.TieneDigitoVerificadorValido(request.Cuit))
        {
            throw new CuitInvalidoException("El dígito verificador del CUIT ingresado no es válido.");
        }

        // (3) Regla de los 60 minutos (FR-07/D-01/D-02): ventana [ahora, ahora + 60 min], calculada
        // acá con TimeProvider — cero DateTime.UtcNow en código productivo (NFR-05).
        var ahoraUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hastaUtc = ahoraUtc.AddMinutes(VentanaModificacionMinutos);
        var sorteoInminente = await _cuentaRepository.TieneSorteoInminenteAsync(compradorId, ahoraUtc, hastaUtc);
        if (sorteoInminente)
        {
            throw new PlazoModificacionVencidoException(
                "El plazo para modificar los datos de la cuenta ya venció.");
        }

        // (4)/(5) Colisiones de mail y CUIT (FR-08/FR-10), excluyendo la propia cuenta —
        // ExisteMailDeOtraCuentaAsync/ExisteCuitDeOtraCuentaAsync ya excluyen compradorId.
        var mailEnUso = await _gateway.ExisteMailDeOtraCuentaAsync(compradorId, request.Mail);
        if (mailEnUso)
        {
            throw new MailEnUsoException("Ese mail ya está en uso.");
        }

        var cuitEnUso = await _gateway.ExisteCuitDeOtraCuentaAsync(compradorId, request.Cuit);
        if (cuitEnUso)
        {
            throw new CuitEnUsoException("Ese CUIT ya está en uso.");
        }

        // (6) Snapshot previo, solo para calcular qué campos cambian — sus valores nunca se loguean
        // (mitigación de R-05).
        var cuentaActual = await _gateway.ObtenerCuentaAsync(compradorId);
        if (cuentaActual is null)
        {
            throw new InvalidOperationException(
                "No se encontró la cuenta del comprador autenticado.");
        }

        // (7) Primera escritura: los cuatro campos juntos (A-03). Un IdentityResult no exitoso se
        // traduce a excepción, no se ignora.
        var resultado = await _gateway.ActualizarDatosAsync(
            compradorId, request.Apellido, request.Nombre, request.Cuit, request.Mail);
        if (!resultado.Exitoso)
        {
            throw new InvalidOperationException(
                "No se pudo actualizar la cuenta del comprador: " + string.Join("; ", resultado.Errores));
        }

        // (8) Auditoría (mitigación de R-05): compradorId, timestamp y NOMBRES de los campos que
        // cambiaron — nunca sus valores, ni el anterior ni el nuevo.
        var camposModificados = ObtenerCamposModificados(cuentaActual, request);
        _logger.LogInformation(
            "Actualización de datos de cuenta de comprador. CompradorId: {CompradorId}. " +
            "TimestampUtc: {TimestampUtc}. CamposModificados: {CamposModificados}.",
            compradorId,
            ahoraUtc,
            string.Join(", ", camposModificados));

        return new CuentaCompradorResponse(request.Apellido, request.Nombre, request.Cuit, request.Mail);
    }

    private static List<string> ObtenerCamposModificados(CuentaCompradorResponse actual, ActualizarCuentaRequest nuevo)
    {
        var campos = new List<string>();

        if (actual.Apellido != nuevo.Apellido)
        {
            campos.Add(nameof(ActualizarCuentaRequest.Apellido));
        }

        if (actual.Nombre != nuevo.Nombre)
        {
            campos.Add(nameof(ActualizarCuentaRequest.Nombre));
        }

        if (actual.Cuit != nuevo.Cuit)
        {
            campos.Add(nameof(ActualizarCuentaRequest.Cuit));
        }

        if (actual.Mail != nuevo.Mail)
        {
            campos.Add(nameof(ActualizarCuentaRequest.Mail));
        }

        return campos;
    }
}
