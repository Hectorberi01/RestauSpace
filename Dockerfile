# syntax=docker/dockerfile:1.7-labs

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props ./
COPY --parents src/**/*.csproj ./
RUN dotnet restore src/Host/SaasRestaurant.Host/SaasRestaurant.Host.csproj
COPY src/ src/
RUN dotnet publish src/Host/SaasRestaurant.Host/SaasRestaurant.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys /home/app/files \
    && chown -R $APP_UID /home/app/.aspnet /home/app/files
# Fichiers lisibles par l'utilisateur non root, quels que soient les droits des sources (600 sur un Mac).
COPY --from=build --chmod=0755 /app ./
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=5 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "SaasRestaurant.Host.dll"]
