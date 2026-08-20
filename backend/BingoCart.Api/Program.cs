using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using BingoCart.Api.Middleware;
using BingoCart.Application.Auth;
using BingoCart.Application.Bingos;
using BingoCart.Application.Organizadores;
using BingoCart.Infrastructure.Auth;
using BingoCart.Infrastructure.Bingos;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using BingoCart.Infrastructure.Organizadores;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .ConfigurarPoliticaDePassword();

builder.Services.AddScoped<IOrganizadorService, OrganizadorService>();
builder.Services.AddScoped<IIdentityGateway, IdentityGateway>();

// FEAT-003, Block 4: IBingoRepository/IBingoService Scoped (mismo lifetime que IOrganizadorService
// — dependen de AppDbContext, que es Scoped). ICartonNumberGenerator Singleton: no tiene estado ni
// depende de nada con lifetime más corto (mismo criterio que JwtTokenService).
builder.Services.AddScoped<IBingoService, BingoService>();
builder.Services.AddScoped<IBingoRepository, BingoRepository>();
builder.Services.AddSingleton<ICartonNumberGenerator, CartonNumberGenerator>();

// FEAT-005, Block 2: IDirectorioRepository Scoped, mismo lifetime que IBingoRepository — depende
// de AppDbContext.
builder.Services.AddScoped<IDirectorioRepository, DirectorioRepository>();

// TimeProvider.System es el reloj real en producción; los tests inyectan un TestTimeProvider
// propio en el JwtTokenService construido directamente (sin pasar por DI), spec FEAT-001b Block 1.
builder.Services.AddSingleton(TimeProvider.System);

// El required del record documenta la intención (SigningKey nunca debería faltar), pero
// AddOptions<T>().Bind(...) construye la instancia vía Activator.CreateInstance (bypassea el
// required, que es una verificación solo de compilador) y deja SigningKey en null si la clave no
// está en configuración — por eso el predicado valida explícitamente contra null/vacío antes de
// medir bytes, para fallar siempre con OptionsValidationException y el mensaje claro de abajo, en
// vez de un ArgumentNullException sin contexto. Cubre ambos casos del Error handling documentado
// en el spec: clave ausente y clave presente pero débil (<32 bytes, forzable por fuerza bruta).
builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.SigningKey)
            && Encoding.UTF8.GetByteCount(settings.SigningKey) >= 32,
        "Jwt:SigningKey debe tener al menos 32 bytes")
    .ValidateOnStart();

builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// AddIdentity() (arriba) ya registró IdentityConstants.ApplicationScheme (cookie) como
// DefaultChallengeScheme. AddAuthentication(defaultScheme) solo fija DefaultScheme — el challenge
// scheme, si ya está seteado explícitamente por otra llamada anterior, no se sobreescribe con el
// fallback. Sin fijar acá también DefaultAuthenticateScheme/DefaultChallengeScheme, [Authorize]
// (Block 3) challengea el cookie de Identity (redirige 302 a /Account/Login) en vez de devolver 401
// vía JwtBearer — hallazgo de CODE, Block 3, confirmado contra el pipeline real.
// DefaultForbidScheme no se fija: cae en DefaultChallengeScheme por la cadena de fallback de
// ASP.NET Core, y JwtBearerHandler no redirige en 403, así que el comportamiento ya es correcto.
// DefaultSignInScheme tampoco se fija — hoy nada llama a HttpContext.SignInAsync sin scheme
// explícito (SignInManager.CheckPasswordSignInAsync, usado en el login, no firma cookie; solo
// valida password+lockout). Si un ticket futuro agrega un flujo que sí firme sin especificar
// scheme, va a fallar en runtime (JwtBearerHandler no implementa IAuthenticationSignInHandler) —
// señal clara de que hace falta fijarlo explícitamente en ese momento, no antes.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

// Configura JwtBearerOptions a partir del MISMO IOptions<JwtSettings> ya validado arriba
// (.ValidateOnStart()), en vez de releer builder.Configuration.GetSection("Jwt") de nuevo acá —
// un solo camino de binding para JwtSettings, sin duplicar la lógica de parseo (hallazgo de
// daw-arch-auditor en CODE, Block 1).
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtSettingsOptions) =>
    {
        var jwtSettings = jwtSettingsOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // El JWT viaja en la cookie httpOnly `bingocart_auth` (fijada por Block 2), nunca en el
        // header Authorization — sin este evento, AddJwtBearer solo mira ese header por defecto y
        // [Authorize] (Block 3) rechazaría siempre, incluso con la cookie presente.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("bingocart_auth", out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddControllers();

// Reemplaza el ValidationProblemDetails por defecto de ASP.NET Core por el mismo contrato de
// error que usa ExceptionHandlingMiddleware ({ "error": "DatosInvalidos", "message": "..." }),
// para que un ModelState inválido (DataAnnotations de NombreOrganizacion/Mail, Block 3) responda
// con el mismo shape que el resto de los errores 400 del endpoint (Block 4).
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var mensaje = string.Join(
            " ",
            context.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));

        return new BadRequestObjectResult(new { error = "DatosInvalidos", message = mensaje });
    };
});

// Rate limiting sobre el endpoint público de registro (threat model, riesgo #3: spam/DoS sin
// autenticación): 5 solicitudes/minuto POR IP (particionado por IP remota), no un límite global
// compartido por todos los clientes.
builder.Services.AddRateLimiter(options =>
{
    // Default de ASP.NET Core es 503 (ServiceUnavailable) ante un rechazo — se fija explícitamente
    // a 429 (semánticamente correcto para rate limiting, y el código que documenta el contrato de
    // ambas políticas en Api contract/ProducesResponseType) para las políticas "registro" y
    // "bingos".
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("registro", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1)
        }));

    // Rate limiting sobre POST /api/bingos (threat model FEAT-003, riesgo TM-01): el chequeo de
    // "bingo activo" (FR-06) evita el abuso CONCURRENTE, pero no evita que un organizador fije
    // fechaSorteoUtc apenas en el futuro y repita la generación de hasta 5.000 cartones cada vez
    // que esa fecha vence. Particionado por organizadorId (claim NameIdentifier del JWT), NO por
    // IP como "registro" — este endpoint requiere autenticación, a diferencia del registro público.
    options.AddPolicy("bingos", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(5)
        }));

    // Rate limiting sobre GET /api/organizadores/directorio (spec FEAT-005, threat model, riesgo
    // R-02: spam/DoS sin autenticación): particionado por IP (mismo criterio que "registro", único
    // válido para un endpoint [AllowAnonymous]), con un límite más generoso (30 req/5 min vs. 5/min
    // de "registro") porque navegar el directorio paginado es un uso legítimo esperado con más
    // tráfico que un formulario de alta.
    options.AddPolicy("directorio", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(5)
        }));
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Origin fijo del frontend Angular en desarrollo local (puerto 8000 — mismo valor que usará el
// contenedor `web` del Block 7). Sin esta política, el navegador bloquea el preflight OPTIONS
// del formulario de registro antes de que la request llegue al controller.
builder.Services.AddCors(options =>
{
    // AllowCredentials() (spec FEAT-001b, Block 1): necesario para que el navegador envíe/reciba
    // la cookie httpOnly bingocart_auth en requests cross-origin (:8000 -> :8080). Compatible con
    // WithOrigins explícito (a diferencia de AllowAnyOrigin, incompatible por spec de CORS).
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:8000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// Middleware de manejo de excepciones global: primero en el pipeline para poder traducir a HTTP
// cualquier excepción lanzada más adelante (incluyendo las del ruteo de rate limiting).
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Corrective loop VERIFY→CODE de FEAT-001a: hasta acá nada aplicaba las migraciones de EF Core al
// arrancar, así que un `docker-compose up --build` desde un clone limpio no creaba el schema de
// `AspNetUsers` (solo funcionaba porque la migración se había corrido a mano en sesiones previas
// de este mismo entorno). `MigrateAsync` es idempotente: no reaplica migraciones ya aplicadas.
// TDE (F-TM-07, threat model riesgo #4) requiere que la base ya exista, por eso se habilita
// DESPUÉS de migrar, no antes. `EnsureTdeEnabledAsync` es igual de idempotente (ver comentario en
// AppDbContextTdeExtensions.cs) y se salta si no hay password configurado, para no romper el
// arranque en un entorno que todavía no haya definido `Tde:MasterKeyPassword`.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    var tdeMasterKeyPassword = builder.Configuration["Tde:MasterKeyPassword"];
    if (!string.IsNullOrWhiteSpace(tdeMasterKeyPassword))
    {
        await dbContext.EnsureTdeEnabledAsync(tdeMasterKeyPassword);
    }
}

app.Run();

// Necesario para que WebApplicationFactory<Program> (tests de integración de Block 4) pueda
// referenciar la clase Program del top-level statement.
public partial class Program
{
}
