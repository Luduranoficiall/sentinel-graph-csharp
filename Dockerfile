# =========================================================================
# Stage 1: Build & Publish (.NET 8 SDK)
# =========================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SentinelGraph.csproj", "./"]
RUN dotnet restore "SentinelGraph.csproj"

COPY . .
RUN dotnet publish "SentinelGraph.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# =========================================================================
# Stage 2: Runtime leve (.NET 8 ASP.NET Runtime) — Pronto para Render
# =========================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Configurações recomendadas para containers em produção
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production \
    PORT=8080

COPY --from=build /app/publish .

# Usuário não-root padrão da imagem oficial Microsoft .NET 8
USER app

EXPOSE 8080

# Render injeta dinamicamente a variável PORT em runtime;
# o shell garante o binding imediato em http://0.0.0.0:${PORT}
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet SentinelGraph.dll"]
