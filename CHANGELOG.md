# Changelog

Todos los cambios notables de este proyecto se documentan en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added

- **FEAT-001a**: Registro de organizador — solución completa desde cero: solución .NET 8 por
  capas (Domain/Application/Infrastructure/Api), Angular 18 con NgModules, SQL Server dockerizado
  con TDE habilitado. Endpoint `POST /api/organizadores/registro` con validación de CUIT (dígito
  verificador), teléfono, password (política de ASP.NET Core Identity) y mail (unicidad),
  rate limiting por IP, y formulario reactivo con manejo de error por campo. Stack completo
  containerizado (`docker-compose up --build`) para pruebas manuales end-to-end.
