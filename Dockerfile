# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SentinelGraph.csproj", "./"]
RUN dotnet restore "SentinelGraph.csproj"

COPY . .
RUN dotnet publish "SentinelGraph.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

# Executa como usuário não-root padrão da imagem oficial .NET 8
USER app
EXPOSE 8080

ENTRYPOINT ["dotnet", "SentinelGraph.dll"]
