import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import { Construct } from 'constructs';

/**
 * AtuaDataStack
 *
 * Camada de DADOS PERSISTENTES do MVP (ADR-012 / Plano v3): tudo aqui
 * sobrevive aos ciclos "down"/"up" de Compute e NÃO é destruído por um
 * `make down` normal. Só é removido com `make destroy-all-data`
 * (irreversível, ação explícita e separada).
 *
 * Contém:
 *  - S3 "frontends": hospedagem estática dos frontends (sem CloudFront).
 *  - S3 "backups": export de RDS/MongoDB (lifecycle -> Glacier em 35 dias,
 *    conforme retenção de 5 anos da ADR-008).
 *  - S3 "releases": artefatos de deploy da API, consumidos pelo user-data
 *    da EC2 no boot (mecanismo de bootstrap - ver AtuaComputeStack).
 *    Fica vazio nesta entrega; publicação do artefato é responsabilidade
 *    futura do backend-engineer / release-versioning.
 *  - Secrets Manager: placeholders para credenciais (RDS, MongoDB Atlas,
 *    segredos de app). NENHUM valor real é definido em código - os
 *    valores reais são escritos via `aws secretsmanager put-secret-value`
 *    fora do repositório, nunca commitados.
 *
 * Observação de design importante:
 *  O RDS PostgreSQL NÃO é gerenciado por esta stack (nem por nenhuma
 *  stack CDK). Motivo: o Plano v3 usa "destroy + snapshot" no down e
 *  "restore-from-snapshot" no up, com um identificador de snapshot
 *  variável a cada ciclo. Gerenciar esse padrão dentro do CloudFormation
 *  causaria "drift" (o recurso seria destruído/recriado por fora do
 *  CDK, e o próximo `cdk deploy` tentaria reconciliar um recurso cuja
 *  identidade física mudou, com alto risco de falha ou recriação
 *  indesejada). Por isso o RDS é tratado como um recurso operacional,
 *  administrado diretamente via AWS CLI nos scripts em `infra/scripts/`,
 *  chamados pelo Makefile. Esta decisão é register-ável como nota de
 *  arquitetura (ver README.md), mas não muda nenhum custo ou requisito
 *  já aprovado - é apenas a forma mais segura de implementar a
 *  Opção 2 (destroy+snapshot) sem colocar em risco o estado do CDK.
 *
 * Custo aproximado desta stack: ~US$ 1,20/mês (3 secrets) + ~US$ 0,10-0,60/mês
 * (S3, dependendo do volume). Nenhum item aqui depende de Free Tier de
 * computação, então o custo é praticamente constante, ligado ou não.
 */
export class AtuaDataStack extends cdk.Stack {
  public readonly frontendsBucket: s3.Bucket;
  public readonly backupsBucket: s3.Bucket;
  public readonly releasesBucket: s3.Bucket;

  public readonly rdsSecret: secretsmanager.Secret;
  public readonly mongoSecret: secretsmanager.Secret;
  public readonly appSecret: secretsmanager.Secret;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const accountId = cdk.Stack.of(this).account;

    // --- S3: frontends (estático, leitura pública apenas dos objetos publicados) ---
    this.frontendsBucket = new s3.Bucket(this, 'FrontendsBucket', {
      bucketName: `atua-${accountId}-frontends`,
      removalPolicy: cdk.RemovalPolicy.RETAIN, // não remove dados em `cdk destroy` simples
      autoDeleteObjects: false,
      encryption: s3.BucketEncryption.S3_MANAGED, // chave padrão AWS, sem CMK
      blockPublicAccess: new s3.BlockPublicAccess({
        blockPublicAcls: false,
        blockPublicPolicy: false,
        ignorePublicAcls: false,
        restrictPublicBuckets: false,
      }),
      websiteIndexDocument: 'index.html',
      websiteErrorDocument: 'index.html',
      versioned: false,
    });

    // Política pública: somente leitura de objetos nos prefixos landing/*, office/* e manager/*
    // Raiz do bucket e demais prefixos permanecem privados (403).
    this.frontendsBucket.addToResourcePolicy(
      new iam.PolicyStatement({
        sid: 'PublicReadFrontends',
        effect: iam.Effect.ALLOW,
        principals: [new iam.StarPrincipal()],
        actions: ['s3:GetObject'],
        resources: [
          this.frontendsBucket.arnForObjects('landing/*'),
          this.frontendsBucket.arnForObjects('office/*'),
          this.frontendsBucket.arnForObjects('manager/*'),
          this.frontendsBucket.arnForObjects('tecnica/*'),
        ],
      }),
    );

    // --- S3: backups (privado, com lifecycle para Glacier em 35 dias) ---
    this.backupsBucket = new s3.Bucket(this, 'BackupsBucket', {
      bucketName: `atua-${accountId}-backups`,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      autoDeleteObjects: false,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      versioned: true,
      lifecycleRules: [
        {
          id: 'atua-backups-to-glacier',
          enabled: true,
          transitions: [
            {
              storageClass: s3.StorageClass.GLACIER,
              transitionAfter: cdk.Duration.days(35),
            },
          ],
        },
      ],
    });

    // --- S3: releases (artefatos de deploy da API, consumidos pelo user-data) ---
    // Fica vazio nesta entrega - nenhum artefato é publicado agora.
    this.releasesBucket = new s3.Bucket(this, 'ReleasesBucket', {
      bucketName: `atua-${accountId}-releases`,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      autoDeleteObjects: false,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      versioned: true,
    });

    // --- Secrets Manager: placeholders, sem valores reais em código ---
    this.rdsSecret = new secretsmanager.Secret(this, 'RdsSecret', {
      secretName: 'atua/rds-postgres',
      description:
        'Credenciais/endpoint do RDS PostgreSQL. Valor real preenchido ' +
        'operacionalmente pelos scripts de bootstrap/restore (nunca em código).',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ username: 'atua_app', host: 'PENDING_BOOTSTRAP', port: 5432 }),
        generateStringKey: 'password',
        excludePunctuation: true,
        passwordLength: 32,
      },
    });

    this.mongoSecret = new secretsmanager.Secret(this, 'MongoSecret', {
      secretName: 'atua/mongodb-atlas',
      description:
        'Connection string do MongoDB Atlas (fornecida pelo usuário). ' +
        'Valor real (com senha) deve ser escrito via ' +
        '`aws secretsmanager put-secret-value`, nunca commitado.',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      secretStringValue: cdk.SecretValue.unsafePlainText(
        JSON.stringify({
          uri: 'PENDING_BOOTSTRAP',
          note:
            'Nenhum dado real (usuario, host ou senha) fica em código. ' +
            'O valor completo da connection string do MongoDB Atlas deve ' +
            'ser escrito aqui via `aws secretsmanager put-secret-value`, ' +
            'fora do repositório, a partir da string fornecida pelo usuário.',
        }),
      ),
    });

    this.appSecret = new secretsmanager.Secret(this, 'AppSecret', {
      secretName: 'atua/app-secrets',
      description: 'Segredos de aplicação (ex.: chave de assinatura JWT). Preenchido operacionalmente.',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ note: 'placeholder - substituir operacionalmente' }),
        generateStringKey: 'jwtSigningKey',
        excludePunctuation: true,
        passwordLength: 64,
      },
    });

    new cdk.CfnOutput(this, 'FrontendsBucketName', { value: this.frontendsBucket.bucketName });
    new cdk.CfnOutput(this, 'BackupsBucketName', { value: this.backupsBucket.bucketName });
    new cdk.CfnOutput(this, 'ReleasesBucketName', { value: this.releasesBucket.bucketName });
  }
}
