using System;
using BingoCart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Data;

/// <summary>
/// DbContext de Identity extendido (Block 1 del spec FEAT-001a). Persiste `ApplicationUser`
/// (tabla `AspNetUsers`, generada por Identity) con los campos de negocio del organizador.
/// Ningún requerimiento funcional se implementa acá — solo el mapeo de columnas adicionales.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

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
    }
}
