# syntax=docker/dockerfile:1
# Build context: raíz del repo (docker-compose.yml usa `context: .`), porque la solución tiene
# 4 proyectos (Domain, Application, Infrastructure, Api) con ProjectReference entre ellos y
# necesitamos toda la carpeta src/ disponible, no solo BingoCart.Api/.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiamos primero solo los .csproj de los 4 proyectos para aprovechar la cache de capas de
# Docker: mientras no cambien las dependencias, `dotnet restore` no se vuelve a ejecutar aunque
# cambie el código.
COPY src/BingoCart.Domain/BingoCart.Domain.csproj src/BingoCart.Domain/
COPY src/BingoCart.Application/BingoCart.Application.csproj src/BingoCart.Application/
COPY src/BingoCart.Infrastructure/BingoCart.Infrastructure.csproj src/BingoCart.Infrastructure/
COPY src/BingoCart.Api/BingoCart.Api.csproj src/BingoCart.Api/

RUN dotnet restore src/BingoCart.Api/BingoCart.Api.csproj

# Ahora sí, el resto del código de los 4 proyectos.
COPY src/ src/

RUN dotnet publish src/BingoCart.Api/BingoCart.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Sin esto, Kestrel bindea por defecto a localhost dentro del contenedor y el puerto publicado
# no llega a ningún lado.
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "BingoCart.Api.dll"]
