import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as kms from 'aws-cdk-lib/aws-kms';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import { Construct } from 'constructs';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Chave (prefixo) do objeto S3 onde o script de bootstrap do Collector é
 * publicado, usado SOMENTE na topologia colocada (ADR-034). Ver
 * `writeCollectorBootstrapScript` e `make deploy-collector-bootstrap`.
 */
export const COLLECTOR_BOOTSTRAP_S3_KEY = 'bootstrap/collector-bootstrap.sh';

/**
 * Prefixo S3 (dentro do mesmo `releasesBucket` já lido pela apiRole) onde
 * `make deploy-channel`/`make redeploy-channel` publicam o bundle de
 * produção do Channel (ADR-050): `dist/` + `node_modules/` + `package.json`
 * gerados por `pnpm --filter @atua/channel-node deploy --prod`. Reaproveita
 * a permissão `ReadReleasesBucket` já concedida à apiRole (wildcard sobre
 * todo o bucket) — não é uma permissão IAM nova.
 *
 * Diferente do Collector, o Channel NÃO lê nenhum secret do Secrets
 * Manager/SSM no boot: as credenciais de serviço (tokens
 * `ChannelWorker__*ServiceToken`) ficam fora deste bundle e fora deste
 * prefixo S3 (nunca em texto plano em S3). Por isso o UserData só
 * restaura runtime + bundle + unit systemd automaticamente em cada
 * `make up`; o arquivo `/etc/atua-channel.env` (tokens) precisa ser
 * reescrito manualmente após cada `make up` via `make deploy-channel`
 * (ver docs/guides/channel-node-deploy-ec2.md) — decisão deliberada para
 * não exigir nenhuma permissão IAM nova (Secrets Manager/SSM) para o
 * Channel.
 */
export const CHANNEL_RELEASES_S3_PREFIX = 'channel-latest';

export interface AtuaComputeStackProps extends cdk.StackProps {
  vpc: ec2.IVpc;
  sgApi: ec2.ISecurityGroup;
  sgCollector: ec2.ISecurityGroup;
  releasesBucket: s3.IBucket;
  mediaTriagemBucket: s3.IBucket;
  rdsSecret: secretsmanager.ISecret;
  mongoSecret: secretsmanager.ISecret;
  appSecret: secretsmanager.ISecret;
  /** Key Pair dedicado do projeto, criado na AtuaNetworkStack. */
  keyPair: ec2.IKeyPair;
  /**
   * CMK para envelope encryption de credenciais de integração (D9).
   * Concedida à apiRole com as 3 ações mínimas necessárias.
   * A collectorRole NÃO recebe nenhuma permissão desta chave.
   */
  credentialCipherKey: kms.IKey;
  /**
   * Recomendação 1 de corte de custo (análise Cost Explorer, set/2026):
   * quando `true`, o Collector roda na MESMA instância EC2 da API
   * (`atua-api-master`), eliminando a segunda t3.micro
   * (`atua-collector-base`), seu EBS e seu IP público.
   * Economia estimada: ~US$ 17-18/mês (EC2 ~12,10 + IP público ~3,60 +
   * EBS ~2,00), condicionada ao uso residual do MVP em set/2026.
   *
   * Default: `false` — preserva a topologia de DUAS instâncias hoje
   * provisionada, para que este flag não altere nada em um `cdk deploy`
   * feito sem intenção explícita. Ativar exige troca de topologia
   * (destroy + recreate da AtuaComputeStack) e aprovação humana prévia
   * (ver docs/architecture/aws-ambiente-mvp.md e o ADR desta decisão).
   *
   * Ativação: `pnpm --filter @atua/infra exec cdk deploy AtuaComputeStack
   * -c collocateCollector=true` (ou via `cdk.context.json` /
   * `infra/Makefile`, nunca hardcoded neste arquivo).
   */
  collocateCollector?: boolean;
}

/**
 * Monta os comandos de bootstrap do Agente Coletor (RF-009/016/017,
 * ADR-023): runtime .NET, dependências do Chromium headless (Playwright),
 * leitura de secrets, escrita do EnvironmentFile, unit systemd e deploy
 * do artefato via S3.
 *
 * @param props        Props da stack (para bucket de releases).
 * @param apiBaseUrl    URL base da API Master que o Worker deve chamar
 *                      (claim/complete/eligibility). Varia por topologia:
 *                      - duas instâncias: IP PRIVADO da API (o IP público
 *                        não funciona entre instâncias na mesma VPC/subnet
 *                        pública — ver nota histórica abaixo);
 *                      - colocated (mesma instância): `http://127.0.0.1`.
 * @param installRuntime Se `true`, instala o runtime .NET
 *                       (`dotnet-runtime-10.0`). Deve ser `false` quando
 *                       colocated, pois o passo da API já instala
 *                       `aspnetcore-runtime-10.0`, que traz o runtime base
 *                       (`Microsoft.NETCore.App`) exigido pelo Worker —
 *                       evita instalar o pacote duas vezes na mesma
 *                       instância.
 */
function collectorBootstrapCommands(
  props: AtuaComputeStackProps,
  apiBaseUrl: string,
  installRuntime: boolean,
  /**
   * Controla se este script declara `RELEASES_BUCKET="${props.releasesBucket.bucketName}"`.
   *
   * `true` (topologia de duas instâncias): o script fica inline na MESMA
   * `ec2.UserData` que o CDK sintetiza para essa instância — o Token do
   * CDK embutido em `props.releasesBucket.bucketName` é resolvido
   * normalmente pelo pipeline de síntese (vira `Fn::ImportValue` no
   * template).
   *
   * `false` (topologia colocada — ADR-034): o script é gerado em disco
   * por `writeCollectorBootstrapScript` e publicado como arquivo
   * ESTÁTICO no S3. Um Token do CDK escrito por `fs.writeFileSync` NÃO é
   * resolvido (não passa pelo pipeline CDK) — vira o literal
   * `${Token[TOKEN.N]}` no bash, quebrando a sintaxe do script (bug real
   * encontrado na validação de 30/09/2026: `TOKEN.185: syntax error:
   * invalid arithmetic operator`). Por isso, quando `false`, a
   * declaração é OMITIDA aqui: o chamador (UserData da API, que
   * continua dentro do pipeline CDK) exporta `RELEASES_BUCKET` com o
   * valor já resolvido ANTES de baixar e executar este script, e o
   * valor é apenas herdado do ambiente.
   */
  declareReleasesBucketVar: boolean,
): string[] {
  const commands: string[] = [];

  if (installRuntime) {
    commands.push(
      '# 1) Runtime .NET (worker service puro — sem Kestrel/ASP.NET Core,',
      '#    por isso dotnet-runtime, não aspnetcore-runtime, diferente da API).',
      'dnf install -y dotnet-runtime-10.0 || yum install -y dotnet-runtime-10.0 || true',
      '',
    );
  } else {
    commands.push(
      '# 1) Runtime .NET: já instalado pelo bootstrap da API nesta mesma',
      '#    instância (aspnetcore-runtime-10.0 inclui Microsoft.NETCore.App,',
      '#    o runtime base exigido pelo Worker). Não reinstalar.',
      '',
    );
  }

  commands.push(
    '# 2) Bibliotecas necessárias para o Chromium headless do Playwright rodar',
    '#    em Amazon Linux 2023 (dnf-based). Lista equivalente ao --with-deps',
    '#    do Playwright para distros Debian, adaptada para Fedora/RHEL/AL2023.',
    'dnf install -y ' +
      'nss nspr atk at-spi2-atk cups-libs libdrm libxkbcommon ' +
      'libXcomposite libXdamage libXfixes libXrandr mesa-libgbm ' +
      'alsa-lib pango cairo at-spi2-core || true',
    '',
    '# 3) Lê o secret do MongoDB Atlas (atua/mongodb-atlas).',
    'for _retry in 1 2 3 4 5; do',
    `  ATUA_MONGO_SECRET_JSON=$(aws secretsmanager get-secret-value --secret-id 'atua/mongodb-atlas' --query 'SecretString' --output text --region sa-east-1 2>/dev/null || true)`,
    '  if [[ -n "$ATUA_MONGO_SECRET_JSON" && "$ATUA_MONGO_SECRET_JSON" != "None" ]]; then',
    '    break',
    '  fi',
    '  echo "WARN: tentativa $_retry/5 — secret atua/mongodb-atlas vazio ou None, aguardando 5s..."',
    '  sleep 5',
    'done',
    '[[ -n "$ATUA_MONGO_SECRET_JSON" && "$ATUA_MONGO_SECRET_JSON" != "None" ]] || {',
    '  echo "FATAL: nao foi possivel obter o secret atua/mongodb-atlas do Secrets Manager apos 5 tentativas."',
    '  exit 1',
    '}',
    'ATUA_MONGO_URI=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'uri\'])" <<< "$ATUA_MONGO_SECRET_JSON")',
    '',
    '# 4) Lê o secret do RDS Postgres (atua/rds-postgres) — mesmo padrão da API.',
    'for _retry in 1 2 3 4 5; do',
    `  ATUA_RDS_SECRET_JSON=$(aws secretsmanager get-secret-value --secret-id 'atua/rds-postgres' --query 'SecretString' --output text --region sa-east-1 2>/dev/null || true)`,
    '  if [[ -n "$ATUA_RDS_SECRET_JSON" && "$ATUA_RDS_SECRET_JSON" != "None" ]]; then',
    '    break',
    '  fi',
    '  echo "WARN: tentativa $_retry/5 — secret atua/rds-postgres vazio ou None, aguardando 5s..."',
    '  sleep 5',
    'done',
    '[[ -n "$ATUA_RDS_SECRET_JSON" && "$ATUA_RDS_SECRET_JSON" != "None" ]] || {',
    '  echo "FATAL: nao foi possivel obter o secret atua/rds-postgres do Secrets Manager apos 5 tentativas."',
    '  exit 1',
    '}',
    'ATUA_DB_HOST=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'host\'])" <<< "$ATUA_RDS_SECRET_JSON")',
    'ATUA_DB_PORT=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'port\'])" <<< "$ATUA_RDS_SECRET_JSON")',
    'ATUA_DB_NAME=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'dbname\'])" <<< "$ATUA_RDS_SECRET_JSON")',
    'ATUA_DB_USER=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'username\'])" <<< "$ATUA_RDS_SECRET_JSON")',
    'ATUA_DB_PASS=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'password\'])" <<< "$ATUA_RDS_SECRET_JSON")',
    '',
    '# Escreve as variáveis de ambiente da aplicação em /etc/atua-collector.env.',
    '# Mesmo racional documentado no user-data da API: EnvironmentFile= é a',
    '# forma confiável de injetar variáveis em um serviço systemd.',
    'cat > /etc/atua-collector.env <<EOF',
    '# Gerado pelo user-data no boot. Não editar manualmente.',
    '# Recarregado a cada `make up` (instância efêmera).',
    'DOTNET_ENVIRONMENT=Production',
    'MongoDB__ConnectionString=${ATUA_MONGO_URI}',
    'Postgres__ConnectionString=Host=${ATUA_DB_HOST};Port=${ATUA_DB_PORT};Database=${ATUA_DB_NAME};Username=${ATUA_DB_USER};Password=${ATUA_DB_PASS}',
    // BUG CORRIGIDO (topologia de duas instâncias): usar instancePublicIp
    // aqui falha silenciosamente — duas instâncias na mesma VPC/subnet
    // pública tentando se comunicar via IP PÚBLICO uma da outra não
    // funciona (a AWS não suporta esse "hairpin NAT" através do Internet
    // Gateway por padrão). Confirmado via teste real: TCP para o IP
    // público trava (timeout), TCP para o IP privado funciona
    // imediatamente. Na topologia colocated, o chamador passa
    // `http://127.0.0.1` (mesma instância, sem travessia de rede).
    `CollectorWorker__ApiBaseUrl=${apiBaseUrl}`,
    '# DÉBITO TÉCNICO CONHECIDO: tokens de ServiceCredential vazios por padrão —',
    '# nenhuma credencial de serviço é provisionada automaticamente no boot.',
    '# Precisam ser inseridos manualmente (ver docs/decisions/ADR-021, seção',
    '# de ServiceCredential) após o tenant/integration/credencial iService',
    '# real existirem. O Worker sobe normalmente sem eles, mas /claim',
    '# retornará 401, tratado como erro recuperável no loop principal',
    '# (Worker.cs), sem crash.',
    'CollectorWorker__ClaimServiceToken=',
    'CollectorWorker__CompleteServiceToken=',
    'CollectorWorker__EligibilityServiceToken=',
    'CollectorWorker__PollingIntervalSeconds=60',
    'CollectorWorker__Headless=true',
    '# Timeout base (ms) para navegação/espera de seletor do Playwright.',
    '# Descoberto empiricamente (2026-09-01) que o servidor Midea real pode',
    '# levar 15s+ só para responder um GET simples a partir de sa-east-1',
    '# (latência real do provedor — TLS handshake completo confirmado via',
    '# curl -v, não é bloqueio de rede). O default de 60s cobre essa',
    '# latência com folga.',
    'CollectorWorker__PageTimeoutMs=60000',
    'EOF',
    'chmod 640 /etc/atua-collector.env',
    '',
    '# 5) Instala o unit file do systemd.',
    '#    Fonte de verdade: infra/systemd/atua-collector.service no repositório.',
    'cat > /etc/systemd/system/atua-collector.service <<\'UNIT_EOF\'',
    '# Gerado pelo user-data no boot. Não editar manualmente na instância.',
    '# Fonte de verdade: infra/systemd/atua-collector.service no repositório.',
    '',
    '[Unit]',
    'Description=ATUA Collector Worker (RF-009/016/017, ADR-023)',
    'After=network.target',
    '',
    '[Service]',
    'Type=simple',
    'EnvironmentFile=/etc/atua-collector.env',
    'WorkingDirectory=/opt/atua-collector',
    'ExecStart=/usr/bin/dotnet /opt/atua-collector/Atua.Collector.dll',
    'Restart=always',
    'RestartSec=10',
    'User=ec2-user',
    '',
    '[Install]',
    'WantedBy=multi-user.target',
    'UNIT_EOF',
    'systemctl daemon-reload',
    'systemctl enable atua-collector',
    '',
    '# 6) Tenta baixar o artefato de deploy mais recente do bucket de releases.',
    '#    Prefixo dedicado (collector-latest/) para não colidir com o da API.',
    ...(declareReleasesBucketVar
      ? [`RELEASES_BUCKET="${props.releasesBucket.bucketName}"`]
      : [
          '# RELEASES_BUCKET já exportado pelo UserData da API antes de baixar',
          '# e executar este script (topologia colocada, ADR-034) — não redeclarar',
          '# aqui com um Token do CDK, que não seria resolvido neste arquivo estático.',
        ]),
    'mkdir -p /opt/atua-collector',
    'if aws s3 ls "s3://$RELEASES_BUCKET/collector-latest/" >/dev/null 2>&1; then',
    '  aws s3 sync "s3://$RELEASES_BUCKET/collector-latest/" /opt/atua-collector/',
    'else',
    '  echo "WARN: Nenhum artefato publicado ainda em s3://$RELEASES_BUCKET/collector-latest/. Bootstrap de rede/OS concluído; deploy da aplicacao fica pendente — rode make deploy-collector e depois make up."',
    'fi',
    '',
    '# 7) Instala os browsers do Playwright (versão deve casar com a do',
    '#    Microsoft.Playwright referenciado em Atua.Collector.csproj). Usa o',
    '#    driver Node embutido no publish (.playwright/), sem precisar de pwsh.',
    'if [ -f /opt/atua-collector/.playwright/node/linux-x64/node ] && [ -f /opt/atua-collector/.playwright/package/cli.js ]; then',
    '  chmod +x /opt/atua-collector/.playwright/node/linux-x64/node',
    '  /opt/atua-collector/.playwright/node/linux-x64/node /opt/atua-collector/.playwright/package/cli.js install chromium || echo "WARN: falha ao instalar o browser do Playwright — coleta real vai falhar ate isso ser corrigido."',
    '  # BUG CORRIGIDO (2026-09-01): user-data roda como root, então o cache do',
    '  # Playwright é instalado em /root/.cache/ms-playwright. O serviço systemd',
    '  # roda como User=ec2-user (não-root), que não enxerga esse cache — o',
    '  # Chromium falhava com "Executable doesn\'t exist" mesmo com o browser',
    '  # instalado. Copia o cache para o home do ec2-user com dono correto.',
    '  if [ -d /root/.cache/ms-playwright ]; then',
    '    mkdir -p /home/ec2-user/.cache',
    '    cp -r /root/.cache/ms-playwright /home/ec2-user/.cache/',
    '    chown -R ec2-user:ec2-user /home/ec2-user/.cache/ms-playwright',
    '  fi',
    'else',
    '  echo "WARN: driver do Playwright nao encontrado em /opt/atua-collector/.playwright — artefato deve ser publicado com \'dotnet publish -r linux-x64\'."',
    'fi',
    '',
    '# 8) Inicia o serviço somente se o artefato estiver presente.',
    'if [ -f /opt/atua-collector/Atua.Collector.dll ]; then',
    '  systemctl restart atua-collector',
    '  echo "atua-collector iniciado/reiniciado com sucesso."',
    'else',
    '  echo "WARN: /opt/atua-collector/Atua.Collector.dll não encontrado — serviço habilitado mas NÃO iniciado. Publique o artefato (make deploy-collector) e reinicie a instância ou rode: systemctl restart atua-collector"',
    'fi',
  );

  return commands;
}

/**
 * Cria um swapfile de 1GiB e ajusta `vm.swappiness`.
 *
 * Aplicado INCONDICIONALMENTE ao `apiInstance` desde o ADR-050 (Channel
 * WhatsApp Node/Baileys publicado na MESMA instância da API, nas DUAS
 * topologias — ver `infra/lib/atua-compute-stack.ts`, chamada logo após o
 * bootstrap da API). Antes, só era aplicado na topologia colocated
 * (API + Collector + Chromium headless dividindo os mesmos 1GiB de RAM
 * da t3.micro). Agora cobre também a topologia padrão (duas instâncias),
 * onde a t3.micro da API passa a hospedar também o processo Node do
 * Channel. O swap é uma rede de segurança contra OOM-kill em picos
 * transitórios de memória — NÃO substitui o monitoramento de memória
 * livre recomendado na validação pós-rollout (ver plano de validação).
 *
 * Idempotente: seguro de rodar novamente em cada boot (`make up`), pois
 * a instância é efêmera e o disco raiz é recriado do zero a cada ciclo.
 */
function swapFileCommands(): string[] {
  return [
    '# X) Swap de 1GiB — rede de segurança contra OOM-kill na t3.micro',
    '#    (1GiB de RAM física) que hospeda a API e, a partir do ADR-050,',
    '#    também o processo Node do Channel WhatsApp (Baileys). Nas',
    '#    instâncias colocated (ADR-034), ainda compartilha RAM com o',
    '#    Collector + Chromium headless. Não substitui o monitoramento de',
    '#    memória livre recomendado na validação.',
    'if [ ! -f /swapfile ]; then',
    '  fallocate -l 1G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=1024',
    '  chmod 600 /swapfile',
    '  mkswap /swapfile',
    '  swapon /swapfile',
    '  grep -q "^/swapfile" /etc/fstab || echo "/swapfile none swap sw 0 0" >> /etc/fstab',
    '  # Swappiness baixo: prefere liberar cache de página antes de recorrer',
    '  # ao swap (reduz a latência sob pressão moderada de memória).',
    '  sysctl -w vm.swappiness=10 || true',
    'fi',
  ];
}

/**
 * Monta os comandos de bootstrap do Channel WhatsApp (Node/Baileys,
 * ADR-035/038/049/050): runtime Node 22, usuário dedicado sem privilégio,
 * diretórios (bundle + sessão Baileys), unit systemd e restauração do
 * bundle de produção via S3 (reaproveitando a permissão `ReadReleasesBucket`
 * já concedida à apiRole — nenhuma permissão IAM nova).
 *
 * Executado SEMPRE no `apiInstance` (nas duas topologias), pois a premissa
 * do PO é publicar o Channel na MESMA EC2 da API (ADR-049/050).
 *
 * Deliberadamente NÃO escreve `/etc/atua-channel.env` (credenciais de
 * serviço) nem inicia o serviço quando esse arquivo não existe — ver
 * `CHANNEL_RELEASES_S3_PREFIX`. Após cada `make up`, rode
 * `make deploy-channel` para reinjetar os tokens e iniciar o serviço (o
 * runtime, o bundle e a unit já estarão prontos, restaurados por este
 * UserData).
 *
 * @param releasesBucketName Nome do bucket de releases (Token do CDK já
 *                            resolvido no pipeline de síntese — este bloco
 *                            SEMPRE fica inline no UserData real da CDK,
 *                            nunca em um arquivo estático gerado por
 *                            `fs.writeFileSync`, diferente do bootstrap do
 *                            Collector na topologia colocada).
 */
function channelBootstrapCommands(releasesBucketName: string): string[] {
  return [
    '# ============================================================',
    '# CHANNEL WHATSAPP (Node/Baileys) — ADR-035/038/049/050',
    '# ============================================================',
    '# 1) Runtime Node 22 (pacote versionado do AL2023 — nenhum module',
    '#    stream necessário). Idempotente: só instala se "node" ausente',
    '#    ou se a versão instalada não for a 22.x.',
    'if ! command -v node >/dev/null 2>&1 || ! node -v | grep -q "^v22\\."; then',
    '  dnf install -y nodejs22 || yum install -y nodejs22 || true',
    'fi',
    '',
    '# 2) Usuário de sistema sem privilégio, dedicado ao Channel (não',
    '#    reutiliza o ec2-user da API — menor privilégio/isolamento).',
    'id -u atua-channel >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin atua-channel',
    '',
    '# 3) Diretórios: bundle (lido pelo processo, escrito pelo deploy via',
    '#    SSH como ec2-user) e sessão Baileys (lido/escrito SOMENTE pelo',
    '#    processo do Channel, fora da árvore do bundle — ver nota sobre',
    '#    ChannelWorker__SessionPath em docs/guides/channel-node-deploy-ec2.md).',
    'mkdir -p /opt/atua-channel/app',
    'chown ec2-user:ec2-user /opt/atua-channel/app',
    'mkdir -p /var/lib/atua-channel/session',
    'chown atua-channel:atua-channel /var/lib/atua-channel/session',
    'chmod 700 /var/lib/atua-channel/session',
    '',
    '# 4) Unit systemd. Fonte de verdade: infra/systemd/atua-channel.service.',
    'cat > /etc/systemd/system/atua-channel.service <<\'UNIT_EOF\'',
    '# Gerado pelo user-data no boot. Não editar manualmente na instância.',
    '# Fonte de verdade: infra/systemd/atua-channel.service no repositório.',
    '',
    '[Unit]',
    'Description=ATUA Channel WhatsApp (Node/Baileys, ADR-035/038/049/050)',
    'After=network-online.target',
    'Wants=network-online.target',
    '',
    '[Service]',
    'Type=simple',
    'EnvironmentFile=/etc/atua-channel.env',
    'WorkingDirectory=/opt/atua-channel/app',
    'ExecStart=/usr/bin/node /opt/atua-channel/app/dist/main.js',
    'Restart=always',
    'RestartSec=10',
    'User=atua-channel',
    'Group=atua-channel',
    'NoNewPrivileges=true',
    'ProtectSystem=strict',
    'ProtectHome=true',
    'PrivateTmp=true',
    'ReadWritePaths=/var/lib/atua-channel/session',
    '',
    '[Install]',
    'WantedBy=multi-user.target',
    'UNIT_EOF',
    'systemctl daemon-reload',
    'systemctl enable atua-channel',
    '',
    '# 5) Restaura o bundle de produção mais recente do bucket de releases',
    '#    (mesmo bucket/permissão já usados pela API — sem IAM novo).',
    `if aws s3 ls "s3://${releasesBucketName}/${CHANNEL_RELEASES_S3_PREFIX}/" >/dev/null 2>&1; then`,
    `  aws s3 sync "s3://${releasesBucketName}/${CHANNEL_RELEASES_S3_PREFIX}/" /opt/atua-channel/app/`,
    '  chown -R ec2-user:ec2-user /opt/atua-channel/app',
    'else',
    `  echo "WARN: Nenhum bundle publicado ainda em s3://${releasesBucketName}/${CHANNEL_RELEASES_S3_PREFIX}/. Rode 'make deploy-channel' apos este 'make up'."`,
    'fi',
    '',
    '# 6) Inicia o serviço SOMENTE se o bundle E o arquivo de credenciais',
    '#    (/etc/atua-channel.env, NUNCA restaurado automaticamente — ver',
    '#    CHANNEL_RELEASES_S3_PREFIX) estiverem presentes. Evita um',
    '#    crash-loop de "token obrigatório ausente" logo após o boot.',
    'if [ -f /opt/atua-channel/app/dist/main.js ] && [ -f /etc/atua-channel.env ]; then',
    '  systemctl restart atua-channel',
    '  echo "atua-channel iniciado/reiniciado com sucesso."',
    'else',
    '  echo "WARN: atua-channel habilitado mas NAO iniciado (bundle e/ou /etc/atua-channel.env ausentes). Rode \'make deploy-channel\' apos este make up."',
    'fi',
  ];
}

/**
 * Gera o script completo de bootstrap do Collector em disco
 * (`infra/dist/collector-bootstrap.sh`), para publicação no S3 pelo
 * alvo `make deploy-collector-bootstrap`.
 *
 * Correção do defeito registrado no ADR-034 ("Tentativa de ativação
 * (2026-09-30) — bloqueada por limite técnico"): concatenar o bootstrap
 * do Collector (runtime + libs do Chromium/Playwright + secrets + unit
 * systemd) inline no `UserData` da instância única, junto com o
 * bootstrap da API, estourava o limite rígido de 16 KiB do
 * `AWS::EC2::Instance` (`"User data is limited to 16384 bytes"`,
 * `UPDATE_FAILED` confirmado em produção). Em vez de inline, o script
 * completo é gerado aqui, publicado como um objeto dedicado no
 * `releasesBucket`, e o `UserData` da instância única passa a apenas
 * baixá-lo (`aws s3 cp`) e executá-lo no boot — mesmo padrão já usado
 * pelo projeto para os artefatos de deploy (API/Collector) e para a
 * leitura de secrets via CLI.
 *
 * SOMENTE chamada na topologia colocada (`collocateCollector: true`);
 * não tem efeito nenhum sobre o `UserData` da topologia padrão (duas
 * instâncias), que continua com os bootstraps inline, sem alteração.
 *
 * Reproduzível: basta rodar `cdk synth AtuaComputeStack -c
 * collocateCollector=true` (feito automaticamente por
 * `make deploy-collector-bootstrap`) para regerar o arquivo antes de
 * publicá-lo.
 */
function writeCollectorBootstrapScript(commands: string[]): void {
  const scriptContent = ['#!/bin/bash', 'set -euxo pipefail', '', ...commands].join('\n') + '\n';
  const outDir = path.join(__dirname, '..', 'dist');
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, 'collector-bootstrap.sh'), scriptContent, { mode: 0o644 });
}

/**
 * AtuaComputeStack
 *
 * Camada de COMPUTAÇÃO do MVP - EFÊMERA por design (Plano v3).
 *
 * Topologia PADRÃO (`collocateCollector` ausente ou `false`): DUAS
 * instâncias EC2 t3.micro, que nascem e morrem juntas no mesmo ciclo
 * up/down:
 *  - `atua-api-master`: API Master (ASP.NET Core), com bootstrap real
 *    (runtime .NET + tentativa de sincronizar o artefato de deploy).
 *  - `atua-collector-base`: Agente Coletor real (RF-009/016/017,
 *    ADR-023) — Worker .NET com Microsoft.Playwright (Chromium headless)
 *    e consumer de Change Streams do MongoDB. Gate assistido aberto
 *    explicitamente pelo usuário em 2026-09-01. Fila de tokens de
 *    ServiceCredential ainda vazia (bloqueado por D7) — o Worker sobe e
 *    faz polling normalmente, mas /claim retorna 401 até uma credencial
 *    real existir (comportamento tratado, sem crash).
 *
 * Topologia ALTERNATIVA (`collocateCollector: true`, Recomendação 1 de
 * corte de custo, análise Cost Explorer set/2026): UMA única instância
 * `atua-api-master`, rodando os dois serviços systemd (`atua-api` e
 * `atua-collector`) lado a lado, com os dois Security Groups
 * (`sgApi` + `sgCollector`) anexados e um swapfile de 1GiB para
 * absorver picos de memória. `collectorInstance` aponta para a MESMA
 * instância de `apiInstance` nesse caso (ver getters), preservando a
 * compatibilidade dos CfnOutputs `CollectorInstanceId`/`CollectorPublicIp`
 * consumidos por `.github/skills/atua-deploy` (redeploy-live.sh,
 * tail-logs.sh) sem exigir alteração nesses scripts.
 *
 * Ciclo de vida: esta é a stack destruída em `make down` e recriada em
 * `make up` (via `cdk destroy AtuaComputeStack` / `cdk deploy
 * AtuaComputeStack`), eliminando 100% do custo de EBS das instâncias
 * quando ociosas (t3.micro só suporta root volume em EBS - não há
 * instance store).
 *
 * Consequência arquitetural assumida (confirmada pelo orchestrator):
 * a API Master é stateless - sessão via JWT persistida no PostgreSQL,
 * sem estado em memória/disco local que precise sobreviver a um ciclo
 * destroy/recreate. O Collector segue a mesma regra (progresso/fila de
 * coleta em RDS/Mongo, nunca em disco local).
 *
 * ⚠️ RISCO DE CUSTO DOCUMENTADO (Free Tier compartilhado, topologia de
 * DUAS instâncias): o Free Tier de EC2 concede 750h/mês COMBINADAS de
 * instâncias t2/t3.micro por conta, não 750h por instância. Rodar as
 * DUAS instâncias (API + Collector) 24/7 no mesmo ciclo consome
 * ~1.460h/mês combinadas, excedendo o Free Tier em ~710h/mês. Isso pode
 * gerar um custo extra estimado em ~US$ 8-9/mês (não coberto pelos
 * números do ADR-012), que soma-se ao residual normal. A topologia
 * colocated (`collocateCollector: true`) elimina esse excedente por
 * completo, além da economia direta de EC2+IP+EBS da segunda instância.
 * Ver README.md §4 para o detalhamento.
 */
export class AtuaComputeStack extends cdk.Stack {
  public readonly apiInstance: ec2.Instance;
  public readonly collectorInstance: ec2.Instance;

  constructor(scope: Construct, id: string, props: AtuaComputeStackProps) {
    super(scope, id, props);

    const collocate = props.collocateCollector === true;

    // --- AMI fixada (pin) ---
    // Por padrão o CDK resolveria `ec2.MachineImage.latestAmazonLinux2023()`
    // em TEMPO DE SYNTH, consultando um SSM Parameter público da AWS. Isso é
    // perigoso para instâncias já vivas e com estado local (dados, chaves,
    // configuração): se a AWS publicar uma nova AMI do AL2023 entre dois
    // `cdk deploy`, o CDK troca o `ImageId` do LaunchTemplate/Instance e o
    // CloudFormation RECRIA a instância (replacement), destruindo o disco
    // raiz (apesar do `deleteOnTermination: true` no EBS, que é o oposto do
    // desejado aqui) e perdendo todo o estado não coberto por backup.
    //
    // Por isso a AMI é fixada (pinned) via contexto CDK, com default igual
    // à AMI atualmente em uso pela instância viva (i-0f32f8f0cd771c01f),
    // confirmada via `aws ec2 describe-instances` em 2026-09-30. Isso vale
    // para as duas topologias (padrão e colocada).
    //
    // Para atualizar a AMI DELIBERADAMENTE (ex.: patch de segurança do SO):
    //   1. Obtenha a nova AMI desejada (ex.:
    //      `aws ssm get-parameter --name
    //      /aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64`).
    //   2. Rode `cdk deploy -c pinnedAmiId=ami-XXXXXXXXXXXXXXXXX` (ou
    //      atualize `cdk.context.json` / `infra/Makefile`), SABENDO que isso
    //      vai RECRIAR a(s) instância(s) EC2 (replacement) — planeje backup,
    //      janela de manutenção e restore de dados antes de aplicar.
    //   3. Depois de validar, atualize também o default abaixo e o ADR-034.
    const pinnedAmiId: string =
      (this.node.tryGetContext('pinnedAmiId') as string | undefined) ?? 'ami-069bb7e5cd028c614';
    const pinnedMachineImage = ec2.MachineImage.genericLinux({
      'sa-east-1': pinnedAmiId,
    });

    const mediaTriagemBucket = props.mediaTriagemBucket;
    const mediaTriagemBucketName = mediaTriagemBucket.bucketName;

    // ============================================================
    // API MASTER
    // ============================================================

    // --- IAM Role de menor privilégio para a API Master ---
    // NOTA (topologia colocated): esta role já lê os 3 secrets (RDS, Mongo,
    // App) e o bucket de releases — ou seja, já cobre tudo que o Collector
    // precisa (Mongo + RDS + releases). Por isso, na topologia colocated a
    // MESMA apiRole é usada para a instância única, sem precisar de união
    // manual de policies. Trade-off aceito e documentado: nessa topologia,
    // o processo do Collector roda numa instância cuja role tem acesso a
    // KMS e ao secret atua/app-secrets, permissões que ele não usa e que,
    // na topologia de duas instâncias, a collectorRole propositalmente não
    // tem (menor privilégio mais estrito). Ver seção "Segurança" do
    // relatório entregue ao orchestrator para o racional dessa concessão.
    const apiRole = new iam.Role(this, 'ApiInstanceRole', {
      roleName: 'atua-api-ec2-role',
      assumedBy: new iam.ServicePrincipal('ec2.amazonaws.com'),
      description: 'Permissões mínimas da EC2 da API Master: ler 3 secrets específicos e o bucket de releases.',
    });

    // Secrets Manager: apenas GetSecretValue, apenas nos 3 ARNs específicos.
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'ReadOnlySpecificSecrets',
        effect: iam.Effect.ALLOW,
        actions: ['secretsmanager:GetSecretValue'],
        resources: [props.rdsSecret.secretArn, props.mongoSecret.secretArn, props.appSecret.secretArn],
      }),
    );

    // S3: ler o bucket de releases (para o user-data baixar o artefato de deploy).
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'ReadReleasesBucket',
        effect: iam.Effect.ALLOW,
        actions: ['s3:GetObject', 's3:ListBucket'],
        resources: [props.releasesBucket.bucketArn, `${props.releasesBucket.bucketArn}/*`],
      }),
    );

    // S3: acesso ao bucket de mídia de triagem, apenas em ARNs de OBJETO
    // (`.../*`) — sem ListBucket e sem acesso ao ARN do bucket em si.
    // PutObject/DeleteObject: a API grava e remove a mídia recebida.
    // GetObject: as URLs pré-assinadas que a API gera para o pool de triagem
    // (S3TriagemInboundMediaStorage) só funcionam se a role que assina puder ler.
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'MediaTriagemBucketObjectAccess',
        effect: iam.Effect.ALLOW,
        actions: ['s3:PutObject', 's3:DeleteObject', 's3:GetObject'],
        resources: [`${mediaTriagemBucket.bucketArn}/*`],
      }),
    );

    // KMS (D9 — Variante B aprovada): exatamente 3 ações, apenas nesta CMK.
    // GenerateDataKey: gerar DEK por integração (envelope encryption).
    // Decrypt: desembrulhar DEK cifrada para uso em runtime.
    // DescribeKey: verificar metadados da chave (alias, estado, rotação).
    // A collectorRole (topologia de duas instâncias) NÃO recebe nenhuma
    // permissão KMS — verificado explicitamente abaixo.
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'KmsCredentialCipher',
        effect: iam.Effect.ALLOW,
        actions: ['kms:GenerateDataKey', 'kms:Decrypt', 'kms:DescribeKey'],
        resources: [props.credentialCipherKey.keyArn],
      }),
    );

    // SSM: leitura do parâmetro com o ARN da CMK (necessário para o user-data).
    // O ARN não é segredo, por isso está em SSM Parameter Standard (gratuito).
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'ReadKmsArnParam',
        effect: iam.Effect.ALLOW,
        actions: ['ssm:GetParameter'],
        resources: [
          `arn:aws:ssm:sa-east-1:${cdk.Stack.of(this).account}:parameter/atua/dev-mvp/kms/credential-cipher-key-arn`,
        ],
      }),
    );

    // Bedrock (ADR-048, adendo): LLM da triagem via perfil de inferência global.
    // O perfil global exige permissão no próprio perfil, no modelo da região de
    // origem e no ARN global do modelo (sem região). Converse usa InvokeModel.
    const triagemLlmModelId = 'anthropic.claude-haiku-4-5-20251001-v1:0';
    apiRole.addToPolicy(
      new iam.PolicyStatement({
        sid: 'BedrockTriagemLlmInvoke',
        effect: iam.Effect.ALLOW,
        actions: ['bedrock:InvokeModel'],
        resources: [
          `arn:aws:bedrock:sa-east-1:${cdk.Stack.of(this).account}:inference-profile/global.${triagemLlmModelId}`,
          `arn:aws:bedrock:sa-east-1::foundation-model/${triagemLlmModelId}`,
          `arn:aws:bedrock:::foundation-model/${triagemLlmModelId}`,
        ],
      }),
    );

    // Nenhuma permissão com Action:"*"/Resource:"*" - princípio de menor privilégio.

    // --- User data: bootstrap da EC2 (runtime + env + systemd + artefato) ---
    const apiUserData = ec2.UserData.forLinux();
    apiUserData.addCommands(
      '#!/bin/bash',
      'set -euxo pipefail',
      '',
      '# 1) Runtime .NET (ASP.NET Core) - instalado no boot, pois a instância é efêmera.',
      '#    Versão 10 corresponde ao TargetFramework net10.0 de Atua.Api.csproj.',
      '#    BUG CORRIGIDO: dotnet-runtime-10.0 instala apenas o runtime base',
      '#    (Microsoft.NETCore.App), mas a API ASP.NET Core exige também o runtime',
      '#    Microsoft.AspNetCore.App, empacotado separadamente como aspnetcore-runtime-10.0.',
      '#    Sem ele, "dotnet Atua.Api.dll" falha com "No frameworks were found" (status 150).',
      'dnf install -y aspnetcore-runtime-10.0 || yum install -y aspnetcore-runtime-10.0 || true',
      '',
      '# 2) Variável de ambiente: ARN da CMK de cifragem de credenciais (D9).',
      '#    O ARN não é segredo; é lido do SSM Parameter Standard (gratuito).',
      '#',
      '#    Retry de até 5 tentativas com backoff de 5s cada:',
      '#    - IAM/SSM pode ter latência de propagação nos primeiros segundos do boot.',
      '#    - AWS CLI v2 com --query pode retornar "None" (string) com exit 0',
      '#      quando o parâmetro não foi encontrado; o `set -e` não captura isso.',
      '#    - A validação explícita abaixo detecta string vazia OU "None" e aborta.',
      'for _retry in 1 2 3 4 5; do',
      `  ATUA_KMS_KEY_ARN=$(aws ssm get-parameter --name '/atua/dev-mvp/kms/credential-cipher-key-arn' --query 'Parameter.Value' --output text --region sa-east-1 2>/dev/null || true)`,
      '  if [[ -n "$ATUA_KMS_KEY_ARN" && "$ATUA_KMS_KEY_ARN" != "None" ]]; then',
      '    break',
      '  fi',
      '  echo "WARN: tentativa $_retry/5 — ARN da CMK vazio ou None, aguardando 5s..."',
      '  sleep 5',
      'done',
      '[[ -n "$ATUA_KMS_KEY_ARN" && "$ATUA_KMS_KEY_ARN" != "None" ]] || {',
      '  echo "FATAL: nao foi possivel obter o ARN da CMK do SSM apos 5 tentativas."',
      '  echo "FATAL: parametro=/atua/dev-mvp/kms/credential-cipher-key-arn regiao=sa-east-1"',
      '  echo "FATAL: verifique a permissao ssm:GetParameter na role da instancia e a existencia do parametro."',
      '  exit 1',
      '}',
      '',
      '# Escreve as variáveis de ambiente da aplicação em /etc/atua-api.env.',
      '#',
      '# POR QUE /etc/atua-api.env e NÃO /etc/environment:',
      '#   /etc/environment é lido por PAM em sessões de login interativo,',
      '#   mas NÃO é herdado por units systemd (o systemd não usa PAM no',
      '#   ExecStart). A forma confiável de injetar variáveis em um serviço',
      '#   systemd é EnvironmentFile= na unit, que lê pares chave=valor',
      '#   de um arquivo dedicado — exatamente este.',
      '#',
      '# INSTRUÇÃO PARA O BACKEND-ENGINEER:',
      '#   Inclua na unit do serviço atua-api.service:',
      '#     [Service]',
      '#     EnvironmentFile=/etc/atua-api.env',
      '#   Isso garante que o processo da API herde as variáveis abaixo.',
      '#',
      '# CONVENÇÃO DE NOMES .NET:',
      '#   O ASP.NET Core usa duplo underscore (__) como separador de nível',
      '#   hierárquico em variáveis de ambiente. A chave de configuração',
      '#   `Integrations:CredentialCipher:KmsKeyArn` é lida da variável',
      '#   `Integrations__CredentialCipher__KmsKeyArn`.',
      '#   O nome ATUA_KMS_KEY_ARN é mantido como alias de diagnóstico',
      '#   (útil em shell/ssh, não consumido pela aplicação).',
      'mkdir -p /etc/atua-api.env.d',
      '',
      '# Lê o secret RDS (atua/rds-postgres) para montar a connection string.',
      '#',
      '#    Retry de até 5 tentativas com backoff de 5s cada (mesmo padrão do KMS ARN).',
      '#    O secret é JSON com campos: host, port, dbname, username, password.',
      'for _retry in 1 2 3 4 5; do',
      `  ATUA_RDS_SECRET_JSON=$(aws secretsmanager get-secret-value --secret-id 'atua/rds-postgres' --query 'SecretString' --output text --region sa-east-1 2>/dev/null || true)`,
      '  if [[ -n "$ATUA_RDS_SECRET_JSON" && "$ATUA_RDS_SECRET_JSON" != "None" ]]; then',
      '    break',
      '  fi',
      '  echo "WARN: tentativa $_retry/5 — secret atua/rds-postgres vazio ou None, aguardando 5s..."',
      '  sleep 5',
      'done',
      '[[ -n "$ATUA_RDS_SECRET_JSON" && "$ATUA_RDS_SECRET_JSON" != "None" ]] || {',
      '  echo "FATAL: nao foi possivel obter o secret atua/rds-postgres do Secrets Manager apos 5 tentativas."',
      '  exit 1',
      '}',
      'ATUA_DB_HOST=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'host\'])" <<< "$ATUA_RDS_SECRET_JSON")',
      'ATUA_DB_PORT=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'port\'])" <<< "$ATUA_RDS_SECRET_JSON")',
      'ATUA_DB_NAME=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'dbname\'])" <<< "$ATUA_RDS_SECRET_JSON")',
      'ATUA_DB_USER=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'username\'])" <<< "$ATUA_RDS_SECRET_JSON")',
      'ATUA_DB_PASS=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'password\'])" <<< "$ATUA_RDS_SECRET_JSON")',
      '',
      '# Lê o secret de app (atua/app-secrets) para obter o JWT signing key.',
      'for _retry in 1 2 3 4 5; do',
      `  ATUA_APP_SECRET_JSON=$(aws secretsmanager get-secret-value --secret-id 'atua/app-secrets' --query 'SecretString' --output text --region sa-east-1 2>/dev/null || true)`,
      '  if [[ -n "$ATUA_APP_SECRET_JSON" && "$ATUA_APP_SECRET_JSON" != "None" ]]; then',
      '    break',
      '  fi',
      '  echo "WARN: tentativa $_retry/5 — secret atua/app-secrets vazio ou None, aguardando 5s..."',
      '  sleep 5',
      'done',
      '[[ -n "$ATUA_APP_SECRET_JSON" && "$ATUA_APP_SECRET_JSON" != "None" ]] || {',
      '  echo "FATAL: nao foi possivel obter o secret atua/app-secrets do Secrets Manager apos 5 tentativas."',
      '  exit 1',
      '}',
      'ATUA_JWT_SIGNING_KEY=$(python3 -c "import sys,json; d=json.load(sys.stdin); print(d[\'jwtSigningKey\'])" <<< "$ATUA_APP_SECRET_JSON")',
      '',
      '# Escreve as variáveis de ambiente da aplicação em /etc/atua-api.env.',
      'cat > /etc/atua-api.env <<EOF',
      '# Gerado pelo user-data no boot. Não editar manualmente.',
      '# Recarregado a cada `make up` (instância efêmera).',
      'ASPNETCORE_ENVIRONMENT=Production',
      'Integrations__CredentialCipher__KmsKeyArn=${ATUA_KMS_KEY_ARN}',
      'ATUA_KMS_KEY_ARN=${ATUA_KMS_KEY_ARN}',
      '# Padrão do projeto (igual ao Collector); ConnectionStrings__Atua é o alias legado.',
      'Postgres__ConnectionString=Host=${ATUA_DB_HOST};Port=${ATUA_DB_PORT};Database=${ATUA_DB_NAME};Username=${ATUA_DB_USER};Password=${ATUA_DB_PASS}',
      'ConnectionStrings__Atua=Host=${ATUA_DB_HOST};Port=${ATUA_DB_PORT};Database=${ATUA_DB_NAME};Username=${ATUA_DB_USER};Password=${ATUA_DB_PASS}',
      'Authentication__SigningKey=${ATUA_JWT_SIGNING_KEY}',
      'Cors__AllowedOrigins__0=https://atyno.com.br',
      'Cors__AllowedOrigins__1=https://office.atyno.com.br',
      'Cors__AllowedOrigins__2=https://manager.atyno.com.br',
      'Cors__AllowedOrigins__3=https://tecnica.atyno.com.br',
      // Placeholder ate existir dominio + identidade verificada no SES.
      // O envio real de e-mail (confirmacao de cadastro) provavelmente falhara
      // (SES em sandbox / sem identidade verificada), mas isso e uma falha
      // tratada dentro do fluxo de envio, nao uma excecao de configuracao
      // ausente na inicializacao (ver Atua.Api/Program.cs).
      'Email__SenderAddress=noreply@atua-mvp.local',
      `TriagemInboundMedia__BucketName=${mediaTriagemBucketName}`,
      // Triagem conduzida por LLM (ADR-048): ligada apenas para o tenant e o
      // telefone de teste até a liberação para clientes reais.
      'TriagemLlm__Enabled=true',
      'TriagemLlm__Region=sa-east-1',
      `TriagemLlm__ModelId=global.${triagemLlmModelId}`,
      'TriagemLlm__ConversationEnabled=true',
      'TriagemLlm__AllowedTenantIds__0=01a0888b-acd8-773b-93b7-7362973d7ea8',
      'TriagemLlm__AllowedPhones__0=+5584994241248',
      'TriagemLlm__MaxLlmCallsPerTriagem=20',
      'TriagemLlm__MaxProviderRetries=2',
      'TriagemLlm__RetryBackoffBaseMs=1000',
      'EOF',
      'chmod 640 /etc/atua-api.env',
      '# Apenas root e membros do grupo que executar a API lêm o arquivo.',
      '# O ARN não é segredo, mas seguimos o princípio de menor exposição.',
      '',
      '# 3) Instala o unit file do systemd.',
      '#    Fonte de verdade: infra/systemd/atua-api.service no repositório.',
      '#    O conteúdo abaixo deve permanecer idêntico ao arquivo-fonte.',
      'cat > /etc/systemd/system/atua-api.service <<\'UNIT_EOF\'',
      '# Gerado pelo user-data no boot. Não editar manualmente na instância.',
      '# Fonte de verdade: infra/systemd/atua-api.service no repositório.',
      '',
      '[Unit]',
      'Description=ATUA Master API',
      'After=network.target',
      '',
      '[Service]',
      'Type=simple',
      'EnvironmentFile=/etc/atua-api.env',
      'WorkingDirectory=/opt/atua-api',
      'ExecStart=/usr/bin/dotnet /opt/atua-api/Atua.Api.dll',
      'Restart=always',
      'RestartSec=10',
      'User=ec2-user',
      '# CAP_NET_BIND_SERVICE: permite ao processo (rodando como ec2-user, não-root)',
      '# bindar na porta 80 (< 1024). Sem essa capability, o Kestrel falha com',
      '# "SocketException: Permission denied" ao tentar abrir o listener.',
      'AmbientCapabilities=CAP_NET_BIND_SERVICE',
      'Environment=ASPNETCORE_URLS=http://0.0.0.0:80',
      '',
      '[Install]',
      'WantedBy=multi-user.target',
      'UNIT_EOF',
      'systemctl daemon-reload',
      'systemctl enable atua-api',
      '',
      '# 4) Tenta baixar o artefato de deploy mais recente do bucket de releases.',
      `RELEASES_BUCKET="${props.releasesBucket.bucketName}"`,
      'mkdir -p /opt/atua-api',
      'if aws s3 ls "s3://$RELEASES_BUCKET/latest/" >/dev/null 2>&1; then',
      '  aws s3 sync "s3://$RELEASES_BUCKET/latest/" /opt/atua-api/',
      'else',
      '  echo "WARN: Nenhum artefato publicado ainda em s3://$RELEASES_BUCKET/latest/. Bootstrap de rede/OS concluído; deploy da aplicacao fica pendente — rode make deploy-api e depois make up."',
      'fi',
      '',
      '# 5) Inicia o serviço somente se o artefato estiver presente.',
      '#    Evita subir um processo incompleto quando o bucket ainda está vazio.',
      'if [ -f /opt/atua-api/Atua.Api.dll ]; then',
      '  systemctl restart atua-api',
      '  echo "atua-api iniciado/reiniciado com sucesso."',
      'else',
      '  echo "WARN: /opt/atua-api/Atua.Api.dll não encontrado — serviço habilitado mas NÃO iniciado. Publique o artefato (make deploy-api) e reinicie a instância ou rode: systemctl restart atua-api"',
      'fi',
    );

    // Swap de 1GiB — INCONDICIONAL a partir do ADR-050 (Channel WhatsApp
    // Node/Baileys publicado na MESMA instância da API, nas DUAS
    // topologias). Antes, só era aplicado na topologia colocated
    // (API+Collector+Chromium). Agora a t3.micro (1 GiB físico) também
    // hospeda o processo Node do Channel (~150-250 MB estimados,
    // ADR-035) além da API .NET — rede de segurança adicional contra
    // OOM-kill, sem custo extra (usa espaço já alocado no EBS de 20GiB
    // do root volume, que já morre com a instância no `make down`).
    // Medição real de 2026-10-22 (duas instâncias, API sozinha):
    // 916MB totais, 517MB "available" — folga confortável mesmo sem
    // swap, mas mantido como defesa em profundidade.
    apiUserData.addCommands(...swapFileCommands());

    // Channel WhatsApp (ADR-050): runtime + usuário + diretórios + unit +
    // restauração do bundle via S3 — ver channelBootstrapCommands(). Roda
    // nas DUAS topologias, sempre no apiInstance (mesma EC2 da API).
    apiUserData.addCommands(...channelBootstrapCommands(props.releasesBucket.bucketName));

    if (collocate) {
      // ============================================================
      // TOPOLOGIA COLOCATED (Recomendação 1 de corte de custo)
      // ============================================================
      // O bootstrap do Collector NÃO é mais concatenado inline (ver
      // ADR-034 — estourava os 16 KiB de limite do UserData): ele é
      // gerado em disco e publicado como objeto dedicado no
      // releasesBucket; o UserData só o baixa e executa, apontando
      // para 127.0.0.1 (mesma instância) e sem reinstalar o runtime
      // .NET (já instalado pelo bootstrap da API).
      writeCollectorBootstrapScript(
        collectorBootstrapCommands(props, 'http://127.0.0.1', /* installRuntime */ false, /* declareReleasesBucketVar */ false),
      );

      // Permissão explícita e restrita (SEM wildcard amplo) ao objeto do
      // bootstrap do Collector. Nota: já coberta, na prática, pelo
      // wildcard mais amplo da policy `ReadReleasesBucket` acima (débito
      // técnico preexistente sobre TODO o bucket de releases, fora do
      // escopo desta correção) — mas esta statement documenta e garante,
      // de forma independente e mínima, exatamente a permissão que esta
      // nova capacidade (download do bootstrap no boot) precisa.
      apiRole.addToPolicy(
        new iam.PolicyStatement({
          sid: 'ReadCollectorBootstrapScript',
          effect: iam.Effect.ALLOW,
          actions: ['s3:GetObject'],
          resources: [`${props.releasesBucket.bucketArn}/${COLLECTOR_BOOTSTRAP_S3_KEY}`],
        }),
      );

      apiUserData.addCommands(
        '# 9) Bootstrap do Collector (ADR-034): baixado do S3 em vez de',
        '#    concatenado inline no UserData, para não estourar o limite',
        '#    de 16 KiB do AWS::EC2::Instance. Objeto publicado por',
        '#    `make deploy-collector-bootstrap` (gerado por `cdk synth` a',
        '#    partir de infra/lib/atua-compute-stack.ts).',
        '#',
        '# IMPORTANTE: RELEASES_BUCKET é exportado AQUI (ainda dentro do',
        '# UserData real da CDK, onde o Token do bucket resolve',
        '# corretamente) e herdado pelo script baixado do S3 — que é um',
        '# arquivo ESTÁTICO gerado em disco (fs.writeFileSync), incapaz de',
        '# resolver Tokens do CDK por si só (bug real encontrado e',
        '# corrigido na validação de 30/09/2026).',
        `export RELEASES_BUCKET="${props.releasesBucket.bucketName}"`,
        'for _retry in 1 2 3 4 5; do',
        `  aws s3 cp "s3://${props.releasesBucket.bucketName}/${COLLECTOR_BOOTSTRAP_S3_KEY}" /tmp/collector-bootstrap.sh --region sa-east-1 && break`,
        '  echo "WARN: tentativa $_retry/5 — falha ao baixar collector-bootstrap.sh do S3, aguardando 5s..."',
        '  sleep 5',
        'done',
        '[ -s /tmp/collector-bootstrap.sh ] || {',
        '  echo "FATAL: nao foi possivel baixar s3://' +
          `${props.releasesBucket.bucketName}/${COLLECTOR_BOOTSTRAP_S3_KEY}` +
          '. Rode \'make deploy-collector-bootstrap\' antes do cdk deploy/make up."',
        '  exit 1',
        '}',
        'chmod +x /tmp/collector-bootstrap.sh',
        'bash /tmp/collector-bootstrap.sh',
      );

      this.apiInstance = new ec2.Instance(this, 'ApiInstance', {
        instanceName: 'atua-api-master',
        vpc: props.vpc,
        vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
        instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
        machineImage: pinnedMachineImage,
        // SG primário da API; o SG do Collector é anexado logo abaixo,
        // já que uma única instância agora hospeda os dois serviços e
        // precisa das regras de egress documentadas em sgCollector
        // (MongoDB Atlas 27017, RDS 5432, etc. — ver atua-network-stack.ts).
        securityGroup: props.sgApi,
        role: apiRole,
        keyPair: props.keyPair,
        userData: apiUserData,
        // Sem Elastic IP: aceitamos IP público dinâmico (muda a cada
        // start/recreate) para evitar cobrança de EIP ocioso.
        associatePublicIpAddress: true,
        blockDevices: [
          {
            deviceName: '/dev/xvda',
            volume: ec2.BlockDeviceVolume.ebs(20, {
              volumeType: ec2.EbsDeviceVolumeType.GP3,
              deleteOnTermination: true, // o EBS deve morrer junto com a instância (é isso que zera o custo no down)
            }),
          },
        ],
      });
      this.apiInstance.addSecurityGroup(props.sgCollector);

      // Alias: mesma instância física, para que os CfnOutputs
      // CollectorInstanceId/CollectorPublicIp (consumidos por
      // .github/skills/atua-deploy/scripts/redeploy-live.sh e
      // tail-logs.sh) continuem resolvendo corretamente sem alterar
      // esses scripts.
      this.collectorInstance = this.apiInstance;
    } else {
      // ============================================================
      // TOPOLOGIA PADRÃO (duas instâncias) — comportamento inalterado
      // ============================================================
      this.apiInstance = new ec2.Instance(this, 'ApiInstance', {
        instanceName: 'atua-api-master',
        vpc: props.vpc,
        vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
        instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
        machineImage: pinnedMachineImage,
        securityGroup: props.sgApi,
        role: apiRole,
        keyPair: props.keyPair,
        userData: apiUserData,
        // Sem Elastic IP: aceitamos IP público dinâmico (muda a cada
        // start/recreate) para evitar cobrança de EIP ocioso.
        associatePublicIpAddress: true,
        blockDevices: [
          {
            deviceName: '/dev/xvda',
            volume: ec2.BlockDeviceVolume.ebs(20, {
              volumeType: ec2.EbsDeviceVolumeType.GP3,
              deleteOnTermination: true, // o EBS deve morrer junto com a instância (é isso que zera o custo no down)
            }),
          },
        ],
      });

      // ============================================================
      // COLLECTOR - Agente Coletor real (RF-009/016/017, ADR-023)
      // ============================================================
      // Gate assistido aberto explicitamente pelo usuário em 2026-09-01.
      // Worker .NET (Microsoft.Playwright + consumer de Change Streams do
      // MongoDB) rodando como serviço systemd, mesmo padrão da API Master.

      // --- IAM Role de menor privilégio: lê apenas os 2 secrets que usa
      // (Mongo Atlas, RDS Postgres) e o bucket de releases. NUNCA o
      // atua/app-secrets (JWT signing key não é usado pelo Collector).
      //
      // IMPORTANTE (D9 — Variante B): a collectorRole NÃO recebe NENHUMA
      // permissão KMS. A CMK e as operações de cifragem/decifragem de DEKs
      // são responsabilidade exclusiva da API. O Collector nunca chama
      // GenerateDataKey, Decrypt ou DescribeKey sobre a CMK — ele recebe a
      // credencial do iService já decifrada no corpo da resposta do /claim.
      const collectorRole = new iam.Role(this, 'CollectorInstanceRole', {
        roleName: 'atua-collector-ec2-role',
        assumedBy: new iam.ServicePrincipal('ec2.amazonaws.com'),
        description:
          'Permissões mínimas da EC2 do Collector: ler os secrets de Mongo/RDS ' +
          'e o bucket de releases (prefixo collector-latest/). Sem acesso a KMS ' +
          'nem ao secret atua/app-secrets.',
      });

      collectorRole.addToPolicy(
        new iam.PolicyStatement({
          sid: 'ReadOnlySpecificSecrets',
          effect: iam.Effect.ALLOW,
          actions: ['secretsmanager:GetSecretValue'],
          resources: [props.mongoSecret.secretArn, props.rdsSecret.secretArn],
        }),
      );

      collectorRole.addToPolicy(
        new iam.PolicyStatement({
          sid: 'ReadReleasesBucket',
          effect: iam.Effect.ALLOW,
          actions: ['s3:GetObject', 's3:ListBucket'],
          resources: [props.releasesBucket.bucketArn, `${props.releasesBucket.bucketArn}/*`],
        }),
      );

      // --- User data: runtime .NET + Playwright/Chromium + env + systemd + artefato.
      const collectorUserData = ec2.UserData.forLinux();
      collectorUserData.addCommands(
        '#!/bin/bash',
        'set -euxo pipefail',
        '',
        ...collectorBootstrapCommands(
          props,
          `http://${this.apiInstance.instancePrivateIp}`,
          /* installRuntime */ true,
          /* declareReleasesBucketVar */ true,
        ),
      );

      this.collectorInstance = new ec2.Instance(this, 'CollectorInstance', {
        instanceName: 'atua-collector-base',
        vpc: props.vpc,
        vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
        instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
        machineImage: pinnedMachineImage,
        securityGroup: props.sgCollector,
        role: collectorRole,
        keyPair: props.keyPair,
        userData: collectorUserData,
        associatePublicIpAddress: true, // sem Elastic IP, mesmo racional da API
        blockDevices: [
          {
            deviceName: '/dev/xvda',
            volume: ec2.BlockDeviceVolume.ebs(20, {
              volumeType: ec2.EbsDeviceVolumeType.GP3,
              deleteOnTermination: true,
            }),
          },
        ],
      });
    }

    new cdk.CfnOutput(this, 'ApiInstanceId', { value: this.apiInstance.instanceId });
    new cdk.CfnOutput(this, 'ApiPublicIp', { value: this.apiInstance.instancePublicIp });
    new cdk.CfnOutput(this, 'CollectorInstanceId', { value: this.collectorInstance.instanceId });
    new cdk.CfnOutput(this, 'CollectorPublicIp', { value: this.collectorInstance.instancePublicIp });
    new cdk.CfnOutput(this, 'ComputeTopology', {
      value: collocate ? 'collocated (1 instancia: API + Collector)' : 'duas-instancias (API e Collector separados)',
    });
  }
}
