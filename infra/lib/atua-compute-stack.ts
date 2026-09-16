import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as kms from 'aws-cdk-lib/aws-kms';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import { Construct } from 'constructs';

export interface AtuaComputeStackProps extends cdk.StackProps {
  vpc: ec2.IVpc;
  sgApi: ec2.ISecurityGroup;
  sgCollector: ec2.ISecurityGroup;
  releasesBucket: s3.IBucket;
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
}

/**
 * AtuaComputeStack
 *
 * Camada de COMPUTAÇÃO do MVP - EFÊMERA por design (Plano v3).
 *
 * Contém DUAS instâncias EC2 t3.micro, que nascem e morrem juntas no
 * mesmo ciclo up/down:
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
 * Ciclo de vida: esta é a stack destruída em `make down` e recriada em
 * `make up` (via `cdk destroy AtuaComputeStack` / `cdk deploy
 * AtuaComputeStack`), eliminando 100% do custo de EBS de AMBAS as
 * instâncias quando ociosas (t3.micro só suporta root volume em EBS -
 * não há instance store).
 *
 * Consequência arquitetural assumida (confirmada pelo orchestrator):
 * a API Master é stateless - sessão via JWT persistida no PostgreSQL,
 * sem estado em memória/disco local que precise sobreviver a um ciclo
 * destroy/recreate. O Collector segue a mesma regra (progresso/fila de
 * coleta em RDS/Mongo, nunca em disco local).
 *
 * ⚠️ RISCO DE CUSTO DOCUMENTADO (Free Tier compartilhado):
 * O Free Tier de EC2 concede 750h/mês COMBINADAS de instâncias
 * t2/t3.micro por conta, não 750h por instância. Rodar as DUAS
 * instâncias (API + Collector) 24/7 no mesmo ciclo consome ~1.460h/mês
 * combinadas, excedendo o Free Tier em ~710h/mês. Isso pode gerar um
 * custo extra estimado em ~US$ 8-9/mês (não coberto pelos números do
 * ADR-012), que soma-se ao residual normal. Ver README.md §4 para o
 * detalhamento e para a decisão explícita do usuário sobre esse risco.
 */
export class AtuaComputeStack extends cdk.Stack {
  public readonly apiInstance: ec2.Instance;
  public readonly collectorInstance: ec2.Instance;

  constructor(scope: Construct, id: string, props: AtuaComputeStackProps) {
    super(scope, id, props);

    // ============================================================
    // API MASTER
    // ============================================================

    // --- IAM Role de menor privilégio para a API Master ---
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

    // KMS (D9 — Variante B aprovada): exatamente 3 ações, apenas nesta CMK.
    // GenerateDataKey: gerar DEK por integração (envelope encryption).
    // Decrypt: desembrulhar DEK cifrada para uso em runtime.
    // DescribeKey: verificar metadados da chave (alias, estado, rotação).
    // A collectorRole NÃO recebe nenhuma permissão KMS — verificado explicitamente abaixo.
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
      '  systemctl start atua-api',
      '  echo "atua-api iniciado com sucesso."',
      'else',
      '  echo "WARN: /opt/atua-api/Atua.Api.dll não encontrado — serviço habilitado mas NÃO iniciado. Publique o artefato (make deploy-api) e reinicie a instância ou rode: systemctl start atua-api"',
      'fi',
    );

    this.apiInstance = new ec2.Instance(this, 'ApiInstance', {
      instanceName: 'atua-api-master',
      vpc: props.vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
      machineImage: ec2.MachineImage.latestAmazonLinux2023(),
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
      '# 1) Runtime .NET (worker service puro — sem Kestrel/ASP.NET Core,',
      '#    por isso dotnet-runtime, não aspnetcore-runtime, diferente da API).',
      'dnf install -y dotnet-runtime-10.0 || yum install -y dotnet-runtime-10.0 || true',
      '',
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
      // BUG CORRIGIDO: usar instancePublicIp aqui falha silenciosamente —
      // duas instâncias na mesma VPC/subnet pública tentando se comunicar
      // via IP PÚBLICO uma da outra não funciona (a AWS não suporta esse
      // "hairpin NAT" através do Internet Gateway por padrão). Confirmado
      // via teste real: TCP para o IP público trava (timeout), TCP para o
      // IP privado funciona imediatamente. Usar sempre o IP privado para
      // tráfego intra-VPC.
      `CollectorWorker__ApiBaseUrl=http://${this.apiInstance.instancePrivateIp}`,
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
      `RELEASES_BUCKET="${props.releasesBucket.bucketName}"`,
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
      '  systemctl start atua-collector',
      '  echo "atua-collector iniciado com sucesso."',
      'else',
      '  echo "WARN: /opt/atua-collector/Atua.Collector.dll não encontrado — serviço habilitado mas NÃO iniciado. Publique o artefato (make deploy-collector) e reinicie a instância ou rode: systemctl start atua-collector"',
      'fi',
    );

    this.collectorInstance = new ec2.Instance(this, 'CollectorInstance', {
      instanceName: 'atua-collector-base',
      vpc: props.vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
      machineImage: ec2.MachineImage.latestAmazonLinux2023(),
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

    new cdk.CfnOutput(this, 'ApiInstanceId', { value: this.apiInstance.instanceId });
    new cdk.CfnOutput(this, 'ApiPublicIp', { value: this.apiInstance.instancePublicIp });
    new cdk.CfnOutput(this, 'CollectorInstanceId', { value: this.collectorInstance.instanceId });
    new cdk.CfnOutput(this, 'CollectorPublicIp', { value: this.collectorInstance.instancePublicIp });
  }
}
