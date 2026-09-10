import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as kms from 'aws-cdk-lib/aws-kms';
import * as ssm from 'aws-cdk-lib/aws-ssm';
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
 *  - S3 "frontends": origem privada para CloudFront/OAC.
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

  /**
   * CMK para cifragem de credenciais de integração (DEK envelope).
   * Uma chave por ambiente (não por tenant — o isolamento por tenant vem
   * da DEK por integração, que já existe no código da aplicação).
   * removalPolicy: RETAIN — obrigatório. Perder a CMK = perder TODAS as
   * credenciais cifradas de forma irreversível.
   */
  public readonly credentialCipherKey: kms.Key;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const accountId = cdk.Stack.of(this).account;

    // ================================================================
    // CMK — Credential Cipher Key (D9)
    // ================================================================
    // Uma chave simétrica por ambiente. Usada para envelope encryption:
    // a API gera uma DEK por integração, cifra-a com esta CMK (GenerateDataKey),
    // e persiste apenas a DEK cifrada. Para decifrar, chama Decrypt.
    // O Worker/Collector NÃO recebe nenhuma permissão KMS (Variante B aprovada).
    //
    // Por que na DataStack e não em stack separada:
    //  - A CMK é um dado persistente, tal como secrets e buckets — deve
    //    sobreviver a qualquer ciclo down/destroy/up.
    //  - removalPolicy: RETAIN é mandatório: sem a CMK, todas as credenciais
    //    cifradas tornam-se irrecuperáveis.
    //  - Criar stack de segurança separada adicionaria overhead de deploy
    //    sem benefício no MVP (único ambiente, sem isolamento de conta).
    //  - A dependência compute → data já existe, então a CMK é visível à
    //    ComputeStack sem criar dependências circulares.
    //
    // Custo: ~US$1,00/mês (1 CMK × US$1,00) + US$0,03/10.000 chamadas API.
    this.credentialCipherKey = new kms.Key(this, 'CredentialCipherKey', {
      description: 'CMK para envelope encryption de credenciais de integração (DEK por tenant). NÃO DESTRUIR.',
      enableKeyRotation: true, // rotação automática anual — AWS gera novo material, mantém versões anteriores para decrypt
      removalPolicy: cdk.RemovalPolicy.RETAIN, // OBRIGATÓRIO — perder = perder todas as credenciais cifradas irreversivelmente
      // Alias NÃO declarado aqui. O CDK cria o AWS::KMS::Alias como recurso
      // separado, mas ele NÃO herda o removalPolicy da chave — padrão CFN é Delete.
      // Num ciclo destroy+up o alias seria deletado enquanto a chave persiste,
      // e o próximo `cdk deploy` falharia com AlreadyExistsException (se a chave
      // retida ainda tiver o alias apontando para ela no KMS) ou deixaria o alias
      // órfão (se deletado). O alias é criado abaixo via `addAlias` com RETAIN
      // aplicado explicitamente no CfnAlias.
    });

    // Alias com removalPolicy: RETAIN explícito no recurso CloudFormation.
    // `key.addAlias()` retorna um `kms.Alias` construído como filho da chave —
    // seu `node.defaultChild` é o `CfnAlias`, e é nele que aplicamos o RETAIN.
    // Isso garante que num ciclo `cdk destroy AtuaDataStack` + `cdk deploy`:
    //  1. A chave é retida (DeletionPolicy: Retain na CfnKey).
    //  2. O alias também é retido (DeletionPolicy: Retain no CfnAlias).
    //  3. O próximo deploy encontra o alias já existente e não tenta recriá-lo
    //     (CDK detecta o recurso físico existente via describe-key/list-aliases).
    // Comportamento esperado se o alias sobrar sem stack:
    //  - O alias "solto" no KMS ainda aponta para a chave retida.
    //  - O próximo `cdk deploy AtuaDataStack` re-adota o recurso existente
    //    sem AlreadyExistsException, porque o CDK usa o mesmo logical ID
    //    (derivado de 'CredentialCipherKey/Alias/Resource') e encontra o
    //    recurso físico `alias/atua-credential-cipher-dev-mvp` já presente.
    const credentialCipherKeyAlias = this.credentialCipherKey.addAlias('alias/atua-credential-cipher-dev-mvp');
    const cfnAlias = credentialCipherKeyAlias.node.defaultChild as kms.CfnAlias;
    cfnAlias.applyRemovalPolicy(cdk.RemovalPolicy.RETAIN);

    // SSM Parameter Standard (gratuito) com o ARN da CMK.
    // O ARN não é segredo — é apenas o identificador público da chave.
    // A aplicação lê este parâmetro no boot via user-data e o injeta como
    // variável de ambiente ATUA_KMS_KEY_ARN.
    // Alternativas consideradas e descartadas:
    //  - Novo Secret: US$0,40/mês extra sem necessidade (ARN não é dado sensível).
    //  - Hardcode no user-data: quebraria se a chave fosse recriada (RETAIN impede,
    //    mas seria má prática mesmo assim).
    //  - Variável de ambiente direto no user-data CDK: o ARN é token resolvido
    //    em deploy time, então funciona — mas SSM documenta o valor na AWS Console
    //    e facilita auditoria.
    new ssm.StringParameter(this, 'CredentialCipherKeyArnParam', {
      parameterName: '/atua/dev-mvp/kms/credential-cipher-key-arn',
      stringValue: this.credentialCipherKey.keyArn,
      description: 'ARN da CMK usada para envelope encryption de credenciais de integração (D9). Não é segredo.',
      tier: ssm.ParameterTier.STANDARD, // gratuito
    });

    new cdk.CfnOutput(this, 'CredentialCipherKeyArn', {
      exportName: 'AtuaCredentialCipherKeyArn',
      value: this.credentialCipherKey.keyArn,
      description: 'ARN da CMK de cifragem de credenciais (D9)',
    });

    new cdk.CfnOutput(this, 'CredentialCipherKeyAlias', {
      value: 'alias/atua-credential-cipher-dev-mvp',
      description: 'Alias da CMK de cifragem de credenciais (D9)',
    });

    // --- S3: frontends (origem privada do CloudFront/OAC) ---
    this.frontendsBucket = new s3.Bucket(this, 'FrontendsBucket', {
      bucketName: `atua-${accountId}-frontends`,
      removalPolicy: cdk.RemovalPolicy.RETAIN, // não remove dados em `cdk destroy` simples
      autoDeleteObjects: false,
      encryption: s3.BucketEncryption.S3_MANAGED, // chave padrão AWS, sem CMK
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      websiteIndexDocument: 'index.html',
      websiteErrorDocument: 'index.html',
      versioned: false,
    });

    // A distribuição CloudFront usa OAC. A condição por conta impede que uma
    // distribuição de outra conta leia o bucket, sem acoplar DataStack ao
    // stack de edge.
    this.frontendsBucket.addToResourcePolicy(
      new iam.PolicyStatement({
        sid: 'CloudFrontReadFrontends',
        effect: iam.Effect.ALLOW,
        principals: [new iam.ServicePrincipal('cloudfront.amazonaws.com')],
        actions: ['s3:GetObject'],
        resources: [this.frontendsBucket.arnForObjects('*')],
        conditions: {
          StringEquals: {
            'AWS:SourceAccount': accountId,
          },
        },
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
