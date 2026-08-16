using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Data;

/// <summary>
/// Habilita Transparent Data Encryption (TDE) sobre la base de datos de <see cref="AppDbContext"/>
/// (corrective loop VERIFY→CODE de FEAT-001a: el threat model y el spec del Bloque 1 declaraban
/// TDE "Mitigado", pero nunca se ejecutaba — solo había un comentario en `docker-compose.yml`
/// diciendo que "no aplica en desarrollo local"). Toda la secuencia es idempotente
/// (`IF NOT EXISTS`/verificación de estado antes de cada paso): correrla en cada arranque de la Api
/// no falla ni duplica objetos si ya estaba habilitado.
///
/// Se invoca EXCLUSIVAMENTE desde <c>Program.cs</c>, después de aplicar las migraciones (TDE
/// requiere que la base ya exista). Deliberadamente NO se llama desde <see cref="AppDbContext"/>
/// ni desde ningún helper compartido con los tests de integración de este proyecto
/// (<c>AppDbContextTests</c>), que crean y destruyen bases descartables propias en cada corrida:
/// pagar el costo — y el riesgo de tocar la master key / el certificado a nivel de instancia,
/// objetos compartidos por todo el server — en cada test no aporta nada que el test de este mismo
/// archivo no cubra ya de forma explícita.
/// </summary>
public static class AppDbContextTdeExtensions
{
    private const string CertificateName = "BingoCartTdeCert";

    public static async Task EnsureTdeEnabledAsync(
        this AppDbContext context,
        string masterKeyPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(masterKeyPassword))
        {
            throw new ArgumentException(
                "Se requiere un password para la master key de TDE.", nameof(masterKeyPassword));
        }

        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "El AppDbContext no tiene una connection string configurada.");
        }

        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        var targetDatabase = connectionStringBuilder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new InvalidOperationException(
                "La connection string no especifica una base de datos (Initial Catalog).");
        }

        // El master key y el certificado son objetos de instancia (viven en `master`), no de la
        // base de destino — por eso el primer tramo corre contra `master` y el segundo contra la
        // base de destino (CREATE DATABASE ENCRYPTION KEY siempre encripta "la base actual": no
        // toma un nombre de base como parámetro, hay que cambiar de catálogo para ejecutarlo).
        connectionStringBuilder.InitialCatalog = "master";
        await using (var masterConnection = new SqlConnection(connectionStringBuilder.ConnectionString))
        {
            await masterConnection.OpenAsync(cancellationToken);

            // El literal del password se escapa duplicando comillas simples (escape estándar de
            // T-SQL): CREATE MASTER KEY no admite pasar este literal como parámetro de
            // sp_executesql. No es input de usuario — viene de configuración de servidor
            // (variable de entorno / appsettings), nunca de una request HTTP.
            var escapedPassword = masterKeyPassword.Replace("'", "''");

            await ExecuteAsync(
                masterConnection,
                $"""
                 IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##')
                     CREATE MASTER KEY ENCRYPTION BY PASSWORD = '{escapedPassword}';
                 """,
                cancellationToken);

            await ExecuteAsync(
                masterConnection,
                $"""
                 IF NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = '{CertificateName}')
                     CREATE CERTIFICATE {CertificateName} WITH SUBJECT = 'BingoCart TDE Certificate';
                 """,
                cancellationToken);
        }

        connectionStringBuilder.InitialCatalog = targetDatabase;
        await using (var targetConnection = new SqlConnection(connectionStringBuilder.ConnectionString))
        {
            await targetConnection.OpenAsync(cancellationToken);

            await ExecuteAsync(
                targetConnection,
                $"""
                 IF NOT EXISTS (SELECT 1 FROM sys.dm_database_encryption_keys WHERE database_id = DB_ID())
                     CREATE DATABASE ENCRYPTION KEY WITH ALGORITHM = AES_256 ENCRYPTION BY SERVER CERTIFICATE {CertificateName};
                 """,
                cancellationToken);

            var escapedDatabaseName = targetDatabase.Replace("]", "]]");

            // `sys.databases.is_encrypted` (no `encryption_state`) es el chequeo correcto de
            // idempotencia acá: `encryption_state` pasa por estados transitorios (2 = "Encryption
            // in progress") antes de llegar a 3 ("Encrypted"), y SQL Server rechaza un segundo
            // `ALTER DATABASE ... SET ENCRYPTION ON` en cuanto el cifrado ya quedó activado —
            // aunque el estado todavía no sea 3 — con "Cannot enable database encryption because
            // it is already enabled.". `is_encrypted` refleja ese "ya activado" sin la carrera.
            await ExecuteAsync(
                targetConnection,
                $"""
                 IF NOT EXISTS (
                     SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_encrypted = 1
                 )
                     ALTER DATABASE [{escapedDatabaseName}] SET ENCRYPTION ON;
                 """,
                cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
