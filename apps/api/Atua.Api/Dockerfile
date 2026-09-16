# =============================================================================
# Atua.Api — Dockerfile multi-stage
# Contexto de build: raiz do repositório (atua/)
#   docker build -f apps/api/Atua.Api/Dockerfile -t atua-api .
# =============================================================================

# ---------- estágio de build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar apenas o .csproj primeiro para aproveitar o cache de camadas do Docker
# ao restaurar dependências (o restore só re-executa quando o .csproj muda).
COPY apps/api/Atua.Api/Atua.Api.csproj apps/api/Atua.Api/
RUN dotnet restore apps/api/Atua.Api/Atua.Api.csproj

# Copiar o restante do código-fonte do projeto
COPY apps/api/Atua.Api/ apps/api/Atua.Api/

# Publicar em Release — saída em /app/publish
RUN dotnet publish apps/api/Atua.Api/Atua.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- estágio final (runtime) ----------
# ASP.NET Core runtime — menor que o SDK completo; inclui ASP.NET Core runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# ASP.NET Core 8+ escuta em http://+:8080 por padrão dentro do container.
# Não é necessário definir ASPNETCORE_URLS explicitamente; o valor padrão já é
# http://+:8080. Documentado aqui para clareza.
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Atua.Api.dll"]
