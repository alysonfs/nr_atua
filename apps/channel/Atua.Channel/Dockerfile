# =============================================================================
# Atua.Channel — Dockerfile multi-stage
# Contexto de build: raiz do repositório (atua/)
#   docker build -f apps/channel/Atua.Channel/Dockerfile -t atua-channel .
# =============================================================================

# ---------- estágio de build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar apenas o .csproj primeiro para aproveitar o cache de camadas do
# Docker ao restaurar dependências.
COPY apps/channel/Atua.Channel/Atua.Channel.csproj apps/channel/Atua.Channel/
RUN dotnet restore apps/channel/Atua.Channel/Atua.Channel.csproj

# Copiar o restante do código-fonte do projeto
COPY apps/channel/Atua.Channel/ apps/channel/Atua.Channel/

# Publicar em Release — saída em /app/publish
RUN dotnet publish apps/channel/Atua.Channel/Atua.Channel.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- estágio final (runtime) ----------
# .NET runtime (sem ASP.NET Core) — o canal é um Worker Service sem HTTP de
# entrada; realiza apenas conexões de saída (claim/complete), nunca expõe
# porta (ADR-042).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Atua.Channel.dll"]
