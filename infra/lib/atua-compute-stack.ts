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
 *  - `atua-collector-base`: instância BASE do Collector - **apenas
 *    SO + rede + IAM role mínima**. NENHUM software de scraping,
 *    Playwright, cron ou agendamento é instalado ou referenciado no
 *    user-data desta instância. Ela existe apenas como máquina pronta
 *    para receber implementação futura, que só ocorrerá quando o
 *    usuário abrir explicitamente o gate assistido do Agente Coletor.
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
 * destroy/recreate. O Collector, quando implementado no futuro, deverá
 * seguir a mesma regra (fila/progresso de coleta em RDS/Mongo, nunca em
 * disco local).
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
      'dnf install -y dotnet-runtime-10.0 || yum install -y dotnet-runtime-10.0 || true',
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
      'ConnectionStrings__Atua=Host=${ATUA_DB_HOST};Port=${ATUA_DB_PORT};Database=${ATUA_DB_NAME};Username=${ATUA_DB_USER};Password=${ATUA_DB_PASS}',
      'Authentication__SigningKey=${ATUA_JWT_SIGNING_KEY}',
      'Cors__AllowedOrigins__0=http://atua-462991286554-frontends.s3-website-sa-east-1.amazonaws.com',
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
    // COLLECTOR - APENAS MÁQUINA BASE, SEM NENHUMA LÓGICA DE COLETA
    // ============================================================

    // --- IAM Role MÍNIMA: nenhuma policy anexada. A instância não
    // precisa (e não deve) acessar secrets, S3 ou qualquer outro
    // recurso até que sua implementação real seja aprovada explicitamente.
    //
    // IMPORTANTE (D9 — Variante B): a collectorRole NÃO recebe NENHUMA
    // permissão KMS. A CMK e as operações de cifragem/decifragem de DEKs
    // são responsabilidade exclusiva da API. O Collector nunca chama
    // GenerateDataKey, Decrypt ou DescribeKey sobre a CMK.
    const collectorRole = new iam.Role(this, 'CollectorInstanceRole', {
      roleName: 'atua-collector-ec2-role',
      assumedBy: new iam.ServicePrincipal('ec2.amazonaws.com'),
      description:
        'Permissões da EC2 base do Collector: NENHUMA por enquanto. ' +
        'Sem acesso a secrets/S3/RDS até a implementação real ser aprovada. ' +
        'Máquina existe apenas como SO + rede, pronta para receber lógica futura.',
    });

    // --- User data: ABSOLUTAMENTE mínimo. Nenhuma menção a Playwright,
    // scraping, agendamento ou cron. Apenas confirma que o SO subiu.
    const collectorUserData = ec2.UserData.forLinux();
    collectorUserData.addCommands(
      '#!/bin/bash',
      'set -euxo pipefail',
      '',
      '# Instância BASE do Collector - SEM lógica de coleta.',
      '# Nenhum software de scraping/Playwright/agendamento é instalado aqui.',
      '# Esta instância existe apenas como SO+rede, pronta para receber',
      '# implementação futura, mediante aprovação explícita separada.',
      'echo "atua-collector-base: boot ok, sem automacao instalada." > /var/log/atua-collector-boot.log',
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
