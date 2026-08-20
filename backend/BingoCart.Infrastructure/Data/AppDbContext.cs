using System;
using BingoCart.Domain.Bingos;
using BingoCart.Domain.Compras;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BingoCart.Infrastructure.Data;

/// <summary>
/// DbContext de Identity extendido (Block 1 del spec FEAT-001a). Persiste `ApplicationUser`
/// (tabla `AspNetUsers`, generada por Identity) con los campos de negocio del organizador, y
/// (Block 3 del spec FEAT-003) `Bingo`/`Carton`. Ningún requerimiento funcional se implementa
/// acá — solo el mapeo de columnas.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Bingo> Bingos => Set<Bingo>();

    public DbSet<Carton> Cartones => Set<Carton>();

    public DbSet<Compra> Compras => Set<Compra>();

    // Entidad de persistencia pura (Infrastructure, no Domain) — decisión de PLAN, spec FEAT-009a
    // Block 1: `Compra.Items` (Domain, `IReadOnlyList<ItemCompra>` inmutable) no se mapea como
    // navegación EF (una colección de solo lectura de un `sealed record` no ofrece un método `Add`
    // que EF Core pueda usar al materializar). En vez de eso, `CompraCartones` es una tabla
    // independiente con FK lógica a `Compras`, mismo criterio ya usado para `Bingo`/`Carton`
    // (`Carton` tampoco es una colección navegable de `Bingo`, es un DbSet propio).
    public DbSet<CompraCarton> CompraCartones => Set<CompraCarton>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            // Nullable (spec FEAT-009a, Block 1): el comprador no completa estos dos campos — la
            // obligatoriedad para organizador se sigue validando en `Organizador.Crear`/
            // `OrganizadorService.RegistrarAsync`, nunca a nivel de columna.
            entity.Property(u => u.NombreOrganizacion)
                .HasMaxLength(200);

            entity.Property(u => u.Cuit)
                .HasMaxLength(11)
                .IsRequired();

            entity.HasIndex(u => u.Cuit)
                .IsUnique();

            entity.Property(u => u.Telefono)
                .HasMaxLength(20);

            // Nuevas (spec FEAT-009a, Block 1): usadas solo por comprador — nullable por el mismo
            // motivo, el organizador no las completa.
            entity.Property(u => u.Apellido)
                .HasMaxLength(200);

            entity.Property(u => u.Nombre)
                .HasMaxLength(200);
        });

        builder.Entity<Bingo>(entity =>
        {
            entity.HasKey(b => b.Id);

            entity.Property(b => b.NombreEvento)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(b => b.CostoPorCarton)
                .HasColumnType("decimal(10,2)");

            // FK LÓGICA hacia AspNetUsers.Id (decisión de PLAN, ver spec Block 3 "Data model"): no
            // hay `HasOne`/`HasForeignKey` hacia `ApplicationUser` acá — el organizador de dominio
            // nunca se persiste como agregado propio (confirmado por impact scan), solo se indexa
            // el Guid para las consultas de FR-06 (`TieneBingoActivoAsync`). No única: un
            // organizador puede tener bingos históricos, solo uno vigente a la vez (validado en
            // Application).
            entity.HasIndex(b => b.OrganizadorId);

            // Índice nuevo (spec FEAT-005, Block 1): el directorio público filtra por
            // `FechaSorteoUtc > ahoraUtc` y ordena por la misma columna en cada request — sin este
            // índice, el JOIN con `Users` forzaría un table scan de `Bingos` (NFR-01). No único:
            // múltiples bingos pueden compartir fecha de sorteo.
            entity.HasIndex(b => b.FechaSorteoUtc);
        });

        builder.Entity<Carton>(entity =>
        {
            entity.HasKey(c => c.Id);

            // FK física con borrado en cascada: un cartón sin su bingo no tiene sentido de
            // dominio (no hay ningún caso de uso que conserve cartones huérfanos), así que
            // borrar un Bingo borra sus Cartones. Decisión explícita, no el default implícito
            // de EF Core para FKs requeridas — este ticket no expone ningún endpoint de borrado
            // todavía (eso es RF-27, ticket futuro), pero el esquema ya queda coherente con esa
            // regla de negocio.
            entity.HasOne<Bingo>()
                .WithMany()
                .HasForeignKey(c => c.BingoId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            // Representación canónica string delimitada por comas (decisión de PLAN, ver spec
            // Block 3 "Data model") en vez de una tabla hija normalizada — evita hasta 50.000 filas
            // adicionales para un bingo de 5.000 cartones (NFR-01/R-02). `Carton.Crear` ya deja
            // `Numeros` en orden ascendente, así que el string resultante es determinístico.
            entity.Property(c => c.Numeros)
                .HasConversion(
                    numeros => string.Join(",", numeros),
                    serializado => (IReadOnlyList<int>)serializado
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(int.Parse)
                        .ToList())
                .HasColumnName("NumerosSerializados")
                .HasMaxLength(60)
                .IsRequired()
                // `ValueComparer<T>` tipa el delegado de igualdad como `Func<T?, T?, bool>` (no
                // `Func<T,T,bool>`) — de ahí los `!`. `Carton.Numeros` nunca es null (columna
                // `IsRequired()` arriba), así que no ocultan un caso real de nulidad.
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<int>>(
                    (a, b) => a!.SequenceEqual(b!),
                    numeros => numeros.Aggregate(0, (hash, n) => HashCode.Combine(hash, n)),
                    numeros => numeros.ToList()));

            // Red de seguridad a nivel de esquema para FR-05 (además de la validación en memoria
            // de Block 2): dos cartones del mismo bingo con el mismo conjunto de números violan
            // este índice único y el INSERT falla con DbUpdateException en vez de persistir un
            // duplicado silenciosamente.
            entity.HasIndex(c => new { c.BingoId, c.Numeros })
                .IsUnique();
        });

        builder.Entity<Compra>(entity =>
        {
            entity.HasKey(c => c.Id);

            // `Compra.Items` (Domain) NO se mapea acá — ver comentario de `DbSet<CompraCarton>`
            // arriba. Sin este `Ignore`, la convención de EF Core intentaría descubrirla como
            // navegación (es una colección de un tipo no primitivo) y fallaría al construir el
            // modelo, porque `IReadOnlyList<ItemCompra>` no expone ningún método para que EF Core
            // agregue elementos al materializar.
            entity.Ignore(c => c.Items);

            // FKs LÓGICAS hacia AspNetUsers.Id (mismo criterio que `Bingo.OrganizadorId`): ni
            // `Organizador` ni `Comprador` se persisten como agregados propios, solo se indexan los
            // Guid para las consultas de este ticket (agrupación por organizador, "mis compras" de
            // un comprador en tickets futuros).
            entity.HasIndex(c => c.OrganizadorId);
            entity.HasIndex(c => c.CompradorId);
        });

        builder.Entity<CompraCarton>(entity =>
        {
            entity.ToTable("CompraCartones");

            // `CartonId` como PK (en vez de compuesta con `CompraId`): un cartón nunca puede
            // pertenecer a más de una compra en toda la vida del sistema (NFR-01/RNF-03) — la PK
            // expresa esa invariante directamente, sin necesitar un índice `UNIQUE` adicional.
            entity.HasKey(cc => cc.CartonId);

            entity.Property(cc => cc.PrecioUnitario)
                .HasColumnType("decimal(10,2)");

            // FK física con borrado en cascada: una fila de `CompraCartones` sin su `Compra` no
            // tiene sentido de dominio, mismo criterio ya usado para `Carton`/`Bingo`.
            entity.HasOne<Compra>()
                .WithMany()
                .HasForeignKey(cc => cc.CompraId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}
