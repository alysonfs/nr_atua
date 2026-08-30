import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
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

    // Nenhuma permissão com Action:"*"/Resource:"*" - princípio de menor privilégio.

    // --- User data: bootstrap mínimo (sem publicar artefato real ainda) ---
    const apiUserData = ec2.UserData.forLinux();
    apiUserData.addCommands(
      '#!/bin/bash',
      'set -euxo pipefail',
      '',
      '# 1) Runtime .NET (ASP.NET Core) - instalado no boot, pois a instância é efêmera.',
      'dnf install -y dotnet-runtime-8.0 || yum install -y dotnet-runtime-8.0 || true',
      '',
      '# 2) Tenta baixar o artefato de deploy mais recente do bucket de releases.',
      `RELEASES_BUCKET="${props.releasesBucket.bucketName}"`,
      'mkdir -p /opt/atua-api',
      'if aws s3 ls "s3://$RELEASES_BUCKET/latest/" >/dev/null 2>&1; then',
      '  aws s3 sync "s3://$RELEASES_BUCKET/latest/" /opt/atua-api/',
      'else',
      '  echo "Nenhum artefato publicado ainda em s3://$RELEASES_BUCKET/latest/. Bootstrap de rede/OS concluído; deploy da aplicacao fica pendente (responsabilidade do backend-engineer/release-versioning)."',
      'fi',
      '',
      '# 3) Nenhum systemd service é habilitado automaticamente nesta entrega -',
      '#    isso evita subir um processo indefinido/incompleto sem artefato real.',
      '#    O backend-engineer deve fornecer o unit file e habilitá-lo quando o',
      '#    artefato de deploy estiver publicado.',
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
