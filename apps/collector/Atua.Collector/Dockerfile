# =============================================================================
# Atua.Collector — Dockerfile multi-stage
# Contexto de build: raiz do repositório (atua/)
#   docker build -f apps/collector/Atua.Collector/Dockerfile -t atua-collector .
# =============================================================================

# ---------- estágio de build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar apenas o .csproj primeiro para aproveitar o cache de camadas do Docker
# ao restaurar dependências (o restore só re-executa quando o .csproj muda).
COPY apps/collector/Atua.Collector/Atua.Collector.csproj apps/collector/Atua.Collector/
RUN dotnet restore apps/collector/Atua.Collector/Atua.Collector.csproj

# Copiar o restante do código-fonte do projeto
COPY apps/collector/Atua.Collector/ apps/collector/Atua.Collector/

# Publicar em Release — saída em /app/publish
RUN dotnet publish apps/collector/Atua.Collector/Atua.Collector.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- estágio de instalação do Chromium ----------
# Criamos um projeto .NET Console mínimo que usa Microsoft.Playwright.Program.Main
# para instalar o Chromium. É a forma correta de invocar o CLI do Playwright
# no .NET (dotnet exec requer runtimeconfig.json próprio do executável).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS playwright-install
WORKDIR /playwright-runner

# Criar um projeto Console mínimo como launcher do Playwright CLI
RUN dotnet new console -n PlaywrightCli --no-restore \
    && sed -i 's|<TargetFramework>.*</TargetFramework>|<TargetFramework>net10.0</TargetFramework>|' PlaywrightCli/PlaywrightCli.csproj

# Adicionar referência ao Microsoft.Playwright com a mesma versão do Collector
RUN dotnet add PlaywrightCli/PlaywrightCli.csproj package Microsoft.Playwright --version 1.62.0

# Program.cs: invoca o CLI do Playwright passando os args do processo.
# Referência totalmente qualificada (Microsoft.Playwright.Program) é obrigatória:
# com top-level statements, "Program" sem qualificação resolve para a classe
# implícita gerada pelo compilador (sem método Main acessível), não para a
# classe do pacote Playwright — colisão de nomes que causa CS0117.
# Microsoft.Playwright.Program.Main é síncrono (int), não retorna Task — sem await.
RUN printf 'return Microsoft.Playwright.Program.Main(args);' > PlaywrightCli/Program.cs

# Publicar o launcher como executável
RUN dotnet publish PlaywrightCli/PlaywrightCli.csproj \
    -c Release \
    -o /playwright-cli

# Instalar Chromium com dependências de sistema
# PLAYWRIGHT_DRIVER_SEARCH_PATH aponta para o diretório com os binários do Playwright
RUN PLAYWRIGHT_DRIVER_SEARCH_PATH=/playwright-cli \
    dotnet /playwright-cli/PlaywrightCli.dll install chromium --with-deps

# ---------- estágio final (runtime) ----------
# .NET runtime (sem ASP.NET Core) — o Collector é um Worker Service sem HTTP.
# Menor footprint que aspnet runtime.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Copiar a aplicação publicada
COPY --from=build /app/publish .

# Copiar os browsers do Playwright instalados no estágio anterior
COPY --from=playwright-install /root/.cache/ms-playwright /root/.cache/ms-playwright

# Instalar dependências de sistema do Chromium no runtime (sem baixar browser novamente).
# Reutilizamos o launcher de playwright do estágio anterior para install-deps.
COPY --from=playwright-install /playwright-cli /playwright-cli
RUN PLAYWRIGHT_DRIVER_SEARCH_PATH=/playwright-cli \
    dotnet /playwright-cli/PlaywrightCli.dll install-deps chromium \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "Atua.Collector.dll"]
