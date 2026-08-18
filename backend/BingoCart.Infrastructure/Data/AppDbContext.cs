using System;
using BingoCart.Domain.Bingos;
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.NombreOrganizacion)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(u => u.Cuit)
                .HasMaxLength(11)
                .IsRequired();

            entity.HasIndex(u => u.Cuit)
                .IsUnique();

            entity.Property(u => u.Telefono)
                .HasMaxLength(20)
                .IsRequired();
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
    }
}
