# NSG Mobile API — container image (used by Render.com, works with any Docker host).
# Build context: repository root.   docker build -t nsg-mobile-api .
# Configuration and secrets come ONLY from environment variables at runtime (see .env.example, docs/DEPLOYMENT-RENDER.md).

# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the restore layer is cached until a dependency changes.
COPY src/NSG.Mobile.Domain/NSG.Mobile.Domain.csproj                 src/NSG.Mobile.Domain/
COPY src/NSG.Mobile.Application/NSG.Mobile.Application.csproj       src/NSG.Mobile.Application/
COPY src/NSG.Mobile.Infrastructure/NSG.Mobile.Infrastructure.csproj src/NSG.Mobile.Infrastructure/
COPY src/NSG.Mobile.API/NSG.Mobile.API.csproj                       src/NSG.Mobile.API/
RUN dotnet restore src/NSG.Mobile.API/NSG.Mobile.API.csproj

COPY src/ src/
RUN dotnet publish src/NSG.Mobile.API/NSG.Mobile.API.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Writable locations for the non-root user: Serilog file sink (logs/ relative to /app) and file storage.
# On Render the container filesystem is ephemeral; mount a persistent disk at /var/data for files that must survive a deploy.
RUN mkdir -p /app/logs /var/data/files \
    && chown -R $APP_UID:$APP_UID /app/logs /var/data

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    FileStorage__BasePath=/var/data/files

# Run as the unprivileged "app" user that ships with the .NET 8 images.
USER $APP_UID

# Render injects PORT (default 10000) and routes HTTPS traffic to it; fall back to 8080 elsewhere.
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet NSG.Mobile.API.dll"]
