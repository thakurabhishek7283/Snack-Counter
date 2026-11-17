# Build context: repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a csproj changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/SnackCounter.Domain/SnackCounter.Domain.csproj src/SnackCounter.Domain/
COPY src/SnackCounter.Application/SnackCounter.Application.csproj src/SnackCounter.Application/
COPY src/SnackCounter.Infrastructure/SnackCounter.Infrastructure.csproj src/SnackCounter.Infrastructure/
COPY src/SnackCounter.Api/SnackCounter.Api.csproj src/SnackCounter.Api/
RUN dotnet restore src/SnackCounter.Api/SnackCounter.Api.csproj

COPY src/ src/
RUN dotnet publish src/SnackCounter.Api/SnackCounter.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Non-root user provided by the .NET base image.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SnackCounter.Api.dll"]
