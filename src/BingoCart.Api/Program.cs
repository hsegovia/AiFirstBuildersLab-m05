using System.Threading.RateLimiting;
using BingoCart.Api.Middleware;
using BingoCart.Application.Organizadores;
using BingoCart.Infrastructure.Data;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

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
    options.AddPolicy("registro", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1)
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
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:8000")
              .AllowAnyHeader()
              .AllowAnyMethod());
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
