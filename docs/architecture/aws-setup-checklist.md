# AWS Setup Checklist - Moldato

**Empresa:** Moldato Soluções Tecnológicas (J J Soluções Tecnológicas)  
**CNPJ:** 53.353.865/0001-06  
**Versão:** 1.1  
**Data:** 2026-08-29  
**Responsável:** DevOps Team  
**Budget:** $10/mês (alerta crítico) | $20/mês (projeto cai)

---

## 📋 Visão Geral

Este documento orienta a configuração inicial completa da infraestrutura AWS da **Moldato**, seguindo as melhores práticas de segurança, governança e auditoria.

**Abordagem:**
1. ✅ Setup manual no Console (conta, MFA, grupos IAM, primeiro usuário)
2. ⚙️ Configuração AWS CLI local
3. 🏗️ Infraestrutura como Código (IaC) para tudo o mais

---

## 🎯 Fase 1: Setup Manual Obrigatório (AWS Console)

### 1.1 Criar Conta AWS Root

**Status:** ✅ Concluído

- [x] Acessar [aws.amazon.com/console](https://aws.amazon.com/console)
- [x] Criar conta com e-mail corporativo da Moldato
- [x] Validar cartão de crédito
- [x] Confirmar telefone
- [x] Selecionar plano de suporte

**⚠️ Segurança:**
- E-mail exclusivo para root (não compartilhar)
- Senha forte (min. 16 caracteres, símbolos, números)
- Guardar credenciais em cofre seguro (ex: 1Password, Vault)

---

### 1.2 Configurar MFA no Root User

**Status:** ✅ Concluído  
**Obrigatório para segurança**

- [x] Acessar Console → Security Credentials
- [x] Ativar MFA (hardware token ou Authy/Google Authenticator)
- [x] Testar logout/login com MFA
- [x] Guardar códigos de backup em cofre seguro

**📌 Importante:**
- **NUNCA** usar root para operações do dia-a-dia
- Root só para emergências e tarefas administrativas específicas

---

### 1.3 Criar Grupos IAM

**Status:** ✅ Concluído

```plaintext
Console → IAM → User Groups → Create Group

Grupos criados:
├── Administrators
│   └── Política: PowerUserAccess (ou AdministratorAccess temporário)
├── Developers (futuro)
│   └── Políticas: Lambda, RDS, S3 read/write limitado
└── ReadOnly (futuro)
    └── Política: ReadOnlyAccess
```

**Checklist:**
- [x] Criar grupo `Administrators`
- [x] Anexar política `PowerUserAccess` ou `AdministratorAccess`
- [ ] Documentar políticas granulares para substituir depois (via IaC)

---

### 1.4 Criar Primeiro Usuário IAM Admin

**Status:** ✅ Concluído

#### Passo a passo executado:

```plaintext
Console → IAM → Users → Add Users

Nome: admin-devops
Tipo: Programmatic + Console access
Senha: Auto-generated (complexa)
[x] Require password reset on first login (se aplicável)

Adicionar ao grupo: Administrators

Tags (recomendadas):
  Environment: production
  Role: admin
  Team: devops
  Company: moldato
```

**Checklist:**
- [x] Criar usuário IAM `admin-devops`
- [x] Adicionar ao grupo `Administrators`
- [x] Escolher tipo de acesso:
  - **Opção A:** Apenas programmatic (Access Keys) → CLI/SDK apenas
  - **Opção B:** Console + programmatic → Console web + CLI/SDK
- [x] Gerar Access Key ID + Secret Access Key
- [ ] **Salvar credenciais (Access Keys) imediatamente em local seguro**

**Se escolheu Opção B (console access):**
- [ ] Configurar MFA no usuário admin-devops (recomendado)
- [ ] Testar login no Console com usuário IAM
- [ ] **Fazer logout do root e nunca mais usar**

**⚠️ Próximos Passos Críticos:**
1. **Salvar Access Keys em local seguro** (1Password/Vault) - OBRIGATÓRIO
2. **Configurar MFA no admin-devops** - apenas se tiver console access
3. **Nunca mais usar root account**

---

## ⚙️ Fase 2: Configuração Local AWS CLI

### 2.1 Instalar AWS CLI v2

```bash
# macOS (download direto - sem Homebrew)
curl "https://awscli.amazonaws.com/AWSCLIV2.pkg" -o "AWSCLIV2.pkg"
sudo installer -pkg AWSCLIV2.pkg -target /

# Linux
curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
unzip awscliv2.zip
sudo ./aws/install

# Windows
# Download: https://awscli.amazonaws.com/AWSCLIV2.msi
# Executar instalador

# Verificar instalação
aws --version  # Esperado: aws-cli/2.x.x

# Cleanup (macOS)
rm AWSCLIV2.pkg
```

---

### 2.2 Configurar Perfil AWS

```bash
# Configurar perfil nomeado para conta Moldato
aws configure --profile moldato

# Preencher com credenciais do usuário IAM admin-devops:
AWS Access Key ID: AKIA****************
AWS Secret Access Key: ****************************************
Default region name: sa-east-1  # São Paulo (mais próximo)
Default output format: json
```

**Testar configuração:**

```bash
# Verificar identidade
aws sts get-caller-identity --profile moldato

# Esperado:
# {
#   "UserId": "AIDA...",
#   "Account": "123456789012",
#   "Arn": "arn:aws:iam::123456789012:user/admin-devops"
# }

# Listar buckets S3 (deve retornar vazio ou erro se nenhum bucket)
aws s3 ls --profile moldato
```

---

### 2.3 Configurar Variável de Ambiente (Opcional)

```bash
# Adicionar ao ~/.zshrc ou ~/.bashrc
export AWS_PROFILE=moldato

# Recarregar
source ~/.zshrc

# Testar sem --profile
aws sts get-caller-identity
```

---

## 🏗️ Fase 3: Infraestrutura como Código (IaC)

**Escolha da ferramenta IaC:**

| Ferramenta       | Prós                                      | Contras                          |
|------------------|-------------------------------------------|----------------------------------|
| **CloudFormation** | Nativo AWS, gratuito, sem dependências    | YAML verboso, curva de aprendizado |
| **AWS CDK**        | TypeScript/Python, high-level, type-safe  | Requer Node/Python, compila para CFN |
| **Terraform**      | Multi-cloud, comunidade grande, HCL       | Estado externo, custo S3/DynamoDB |

**Recomendação:** 
- **AWS CDK (TypeScript)** — projeto já usa TypeScript (pnpm workspace), integra bem com monorepo.

---

### 3.1 Instalar AWS CDK

```bash
# Instalar globalmente
npm install -g aws-cdk

# Ou no projeto (recomendado para versionamento)
pnpm add -Dw aws-cdk

# Verificar instalação
cdk --version  # Esperado: 2.x.x
```

---

### 3.2 Inicializar Projeto CDK

```bash
# Na raiz do projeto Atua
mkdir infra
cd infra

# Inicializar CDK TypeScript
cdk init app --language=typescript

# Estrutura gerada:
# infra/
#   bin/
#     infra.ts         # Entry point
#   lib/
#     infra-stack.ts   # Stacks principais
#   cdk.json           # Configuração CDK
#   package.json
#   tsconfig.json
```

**Integrar com pnpm workspace:**

```json
// infra/package.json - adicionar:
{
  "name": "@atua/infra",
  "private": true
}

// raiz/pnpm-workspace.yaml - já deve incluir:
packages:
  - 'apps/*'
  - 'infra'    # <-- adicionar
```

---

### 3.3 Bootstrap CDK na Conta AWS

```bash
# Descobrir Account ID
aws sts get-caller-identity --profile moldato --query Account --output text
# Exemplo output: 123456789012

# Bootstrap CDK (uma vez por conta/região)
cdk bootstrap aws://123456789012/sa-east-1 --profile moldato

# Output esperado:
# ✅ Environment aws://123456789012/sa-east-1 bootstrapped
```

**O que o bootstrap faz:**
- Cria bucket S3 para assets CDK
- Cria roles IAM para CloudFormation
- Cria stack `CDKToolkit`

---

## 🛡️ Fase 4: Configurações de Segurança e Governança

### 4.1 IAM: Estrutura de Usuários e Roles (via CDK)

**Criar stack:** `infra/lib/iam-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as iam from 'aws-cdk-lib/aws-iam';
import { Construct } from 'constructs';

export class IamStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Grupo Developers (para futuros membros)
    const developersGroup = new iam.Group(this, 'DevelopersGroup', {
      groupName: 'Developers'
    });

    // Políticas granulares para Developers
    developersGroup.addManagedPolicy(
      iam.ManagedPolicy.fromAwsManagedPolicyName('ReadOnlyAccess')
    );

    // Adicionar políticas inline específicas (ex: Lambda deploy)
    developersGroup.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: [
        'lambda:UpdateFunctionCode',
        'lambda:UpdateFunctionConfiguration',
        's3:PutObject',
        's3:GetObject'
      ],
      resources: ['arn:aws:lambda:sa-east-1:*:function/atua-*']
    }));

    // Role para Lambda execution
    const lambdaExecutionRole = new iam.Role(this, 'LambdaExecRole', {
      roleName: 'AtuaLambdaExecutionRole',
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
      managedPolicies: [
        iam.ManagedPolicy.fromAwsManagedPolicyName(
          'service-role/AWSLambdaBasicExecutionRole'
        ),
        iam.ManagedPolicy.fromAwsManagedPolicyName(
          'service-role/AWSLambdaVPCAccessExecutionRole'
        )
      ]
    });

    // Exportar ARN da role
    new cdk.CfnOutput(this, 'LambdaRoleArn', {
      value: lambdaExecutionRole.roleArn,
      exportName: 'AtuaLambdaExecutionRoleArn'
    });
  }
}
```

**Checklist:**
- [ ] Criar stack IAM via CDK
- [ ] Criar grupo `Developers` com políticas granulares
- [ ] Criar roles: `LambdaExecutionRole`, `EC2InstanceRole`, `RDSMonitoringRole`
- [ ] Após validar IaC, remover `AdministratorAccess` do grupo Administrators
- [ ] Aplicar least privilege (políticas mínimas necessárias)
- [ ] Configurar Password Policy (14+ chars, rotação 90 dias)

---

### 4.2 AWS Budgets: Controle de Custos

**Criar stack:** `infra/lib/budgets-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as budgets from 'aws-cdk-lib/aws-budgets';
import { Construct } from 'constructs';

export class BudgetsStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Budget CRÍTICO: $10/mês (projeto tem apenas $100 de crédito)
    new budgets.CfnBudget(this, 'MoldatoCriticalBudget', {
      budget: {
        budgetName: 'moldato-atua-critical',
        budgetLimit: {
          amount: 10, // USD por mês - ALERTA CRÍTICO
          unit: 'USD'
        },
        budgetType: 'COST',
        timeUnit: 'MONTHLY',
        costFilters: {
          TagKeyValue: ['Project$Atua']
        }
      },
      notificationsWithSubscribers: [
        {
          notification: {
            notificationType: 'ACTUAL',
            comparisonOperator: 'GREATER_THAN',
            threshold: 50, // $5 - primeiro alerta
            thresholdType: 'PERCENTAGE'
          },
          subscribers: [{
            subscriptionType: 'EMAIL',
            address: 'financeiro@moldato.com'
          }]
        },
        {
          notification: {
            notificationType: 'ACTUAL',
            comparisonOperator: 'GREATER_THAN',
            threshold: 80, // $8 - alerta severo
            thresholdType: 'PERCENTAGE'
          },
          subscribers: [{
            subscriptionType: 'EMAIL',
            address: 'financeiro@moldato.com'
          }]
        },
        {
          notification: {
            notificationType: 'ACTUAL',
            comparisonOperator: 'GREATER_THAN',
            threshold: 100, // $10 - CRÍTICO
            thresholdType: 'PERCENTAGE'
          },
          subscribers: [{
            subscriptionType: 'EMAIL',
            address: 'financeiro@moldato.com'
          }]
        },
        {
          notification: {
            notificationType: 'FORECASTED',
            comparisonOperator: 'GREATER_THAN',
            threshold: 90, // Projeção de estouro
            thresholdType: 'PERCENTAGE'
          },
          subscribers: [{
            subscriptionType: 'EMAIL',
            address: 'financeiro@moldato.com'
          }]
        }
      ]
    });

    // Budget MÁXIMO ABSOLUTO: $20/mês (projeto CAI se ultrapassar)
    new budgets.CfnBudget(this, 'MoldatoMaxBudget', {
      budget: {
        budgetName: 'moldato-atua-maximum',
        budgetLimit: {
          amount: 20, // USD por mês - MÁXIMO ABSOLUTO
          unit: 'USD'
        },
        budgetType: 'COST',
        timeUnit: 'MONTHLY'
      },
      notificationsWithSubscribers: [
        {
          notification: {
            notificationType: 'ACTUAL',
            comparisonOperator: 'GREATER_THAN',
            threshold: 100, // $20 - PROJETO CAI
            thresholdType: 'PERCENTAGE'
          },
          subscribers: [{
            subscriptionType: 'EMAIL',
            address: 'financeiro@moldato.com'
          }]
        }
      ]
    });
  }
}
```

**Checklist:**
- [ ] Criar budget crítico: **$10/mês** (alertas em $5, $8, $10)
- [ ] Criar budget máximo: **$20/mês** (projeto CAI se ultrapassar)
- [ ] Configurar alertas: 50%, 80%, 100% (actual + forecasted)
- [ ] E-mail: financeiro@moldato.com
- [ ] Monitorar créditos AWS ($100 disponíveis)
- [ ] Configurar tags para rastreamento (Project:Atua)
- [ ] ⚠️ **USAR APENAS FREE TIER QUANDO POSSÍVEL**

---

### 4.3 CloudTrail: Auditoria e Compliance

**Criar stack:** `infra/lib/security-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as cloudtrail from 'aws-cdk-lib/aws-cloudtrail';
import * as s3 from 'aws-cdk-lib/aws-s3';
import { Construct } from 'constructs';

export class SecurityStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Bucket para logs CloudTrail
    const trailBucket = new s3.Bucket(this, 'CloudTrailBucket', {
      bucketName: `moldato-cloudtrail-${cdk.Stack.of(this).account}`,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      versioned: true,
      lifecycleRules: [{
        expiration: cdk.Duration.days(365), // Reter 1 ano
        transitions: [{
          storageClass: s3.StorageClass.GLACIER,
          transitionAfter: cdk.Duration.days(90) // Mover para Glacier após 90 dias
        }]
      }],
      removalPolicy: cdk.RemovalPolicy.RETAIN // Não deletar logs por engano
    });

    // CloudTrail multi-region
    const trail = new cloudtrail.Trail(this, 'MoldatoTrail', {
      trailName: 'moldato-audit-trail',
      bucket: trailBucket,
      includeGlobalServiceEvents: true,
      isMultiRegionTrail: true,
      managementEvents: cloudtrail.ReadWriteType.ALL,
      enableFileValidation: true // Integridade dos logs
    });

    // Registrar eventos de data (S3, Lambda)
    trail.addS3EventSelector([{
      bucket: s3.Bucket.fromBucketName(this, 'AllBuckets', '*')
    }], {
      readWriteType: cloudtrail.ReadWriteType.ALL
    });
  }
}
```

**Checklist:**
- [ ] Criar S3 bucket para logs CloudTrail (criptografado + versionado)
- [ ] Ativar CloudTrail multi-region
- [ ] Configurar retenção: 365 dias (transição Glacier 90 dias)
- [ ] Habilitar file validation (integridade)
- [ ] Registrar eventos de gerenciamento (read/write)
- [ ] Integrar com CloudWatch Logs (opcional, para alertas)

---

### 4.4 AWS Config: Compliance

```typescript
// Adicionar ao SecurityStack
import * as config from 'aws-cdk-lib/aws-config';

// Regras de compliance
new config.ManagedRule(this, 'S3BucketPublicReadProhibited', {
  identifier: config.ManagedRuleIdentifiers.S3_BUCKET_PUBLIC_READ_PROHIBITED,
  configRuleName: 's3-no-public-read'
});

new config.ManagedRule(this, 'RDSEncryptionEnabled', {
  identifier: config.ManagedRuleIdentifiers.RDS_STORAGE_ENCRYPTED,
  configRuleName: 'rds-encryption-required'
});

new config.ManagedRule(this, 'IAMMFAEnabled', {
  identifier: config.ManagedRuleIdentifiers.IAM_USER_MFA_ENABLED,
  configRuleName: 'iam-mfa-required'
});
```

**Checklist:**
- [ ] Ativar AWS Config
- [ ] Configurar regras: S3 público proibido, RDS criptografado, MFA obrigatório
- [ ] Criar S3 bucket para configuration snapshots
- [ ] Configurar notificações SNS para non-compliance

---

### 4.5 GuardDuty: Detecção de Ameaças

```typescript
// Adicionar ao SecurityStack
import * as guardduty from 'aws-cdk-lib/aws-guardduty';

new guardduty.CfnDetector(this, 'MoldatoGuardDuty', {
  enable: true,
  findingPublishingFrequency: 'FIFTEEN_MINUTES'
});
```

**Checklist:**
- [ ] Ativar GuardDuty em sa-east-1
- [ ] Configurar notificações SNS para findings HIGH/CRITICAL
- [ ] Revisar findings semanalmente
- [ ] Integrar com Slack/PagerDuty (opcional)

---

## 🚀 Fase 5: Provisionamento de Serviços da Aplicação

### 5.1 Networking: VPC

**Criar stack:** `infra/lib/network-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import { Construct } from 'constructs';

export class NetworkStack extends cdk.Stack {
  public readonly vpc: ec2.Vpc;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // VPC MÍNIMA - SEM NAT GATEWAY (economia de $32/mês!)
    this.vpc = new ec2.Vpc(this, 'AtuaVPC', {
      vpcName: 'atua-vpc',
      maxAzs: 2,
      natGateways: 0, // ⚠️ SEM NAT = Lambda em subnet pública (economia crítica)
      subnetConfiguration: [
        {
          name: 'Public',
          subnetType: ec2.SubnetType.PUBLIC,
          cidrMask: 24
        },
        {
          name: 'Isolated',
          subnetType: ec2.SubnetType.PRIVATE_ISOLATED,
          cidrMask: 24
        }
      ]
    });

    // Security Group para Lambda
    const lambdaSG = new ec2.SecurityGroup(this, 'LambdaSG', {
      vpc: this.vpc,
      description: 'Security group for Lambda functions',
      allowAllOutbound: true
    });

    // Security Group para RDS
    const rdsSG = new ec2.SecurityGroup(this, 'RDSSG', {
      vpc: this.vpc,
      description: 'Security group for RDS PostgreSQL',
      allowAllOutbound: false
    });

    // Permitir Lambda -> RDS
    rdsSG.addIngressRule(
      lambdaSG,
      ec2.Port.tcp(5432),
      'Allow Lambda to access RDS'
    );

    // Exportar para outras stacks
    new cdk.CfnOutput(this, 'VpcId', {
      value: this.vpc.vpcId,
      exportName: 'AtuaVpcId'
    });
  }
}
```

**Checklist (MODO ECONOMIA):**
- [ ] Criar VPC com CIDR 10.0.0.0/16
- [ ] 2 AZs (sa-east-1a, sa-east-1b)
- [ ] Subnets: Public (Lambda + ALB), Isolated (RDS)
- [ ] ⚠️ **SEM NAT Gateway** (economia de $32/mês!)
- [ ] Lambda em subnet pública com acesso internet direto
- [ ] Security Groups: Lambda, RDS
- [ ] ⚠️ Considerar VPC Endpoints para S3 (grátis, evita tráfego internet)

---

### 5.2 Backend: Lambda + API Gateway

**Criar stack:** `infra/lib/backend-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as apigateway from 'aws-cdk-lib/aws-apigateway';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import { Construct } from 'constructs';

export class BackendStack extends cdk.Stack {
  constructor(scope: Construct, id: string, vpc: ec2.Vpc, props?: cdk.StackProps) {
    super(scope, id, props);

    // Lambda Function (MODO FREE TIER)
    const backendFunction = new lambda.Function(this, 'BackendAPI', {
      functionName: 'atua-backend-api',
      runtime: lambda.Runtime.NODEJS_20_X,
      handler: 'index.handler',
      code: lambda.Code.fromAsset('../backend/dist'),
      // ⚠️ SEM VPC = grátis, mas sem acesso direto a RDS
      // Opção 1: RDS público com Security Group restrito (não recomendado)
      // Opção 2: RDS Proxy público (adiciona custo)
      // Opção 3: Lambda em VPC subnet pública (sem NAT)
      memorySize: 256, // MÍNIMO (Free Tier: 400k GB-s/mês)
      timeout: cdk.Duration.seconds(10), // Reduzido
      environment: {
        NODE_ENV: 'production',
        DB_HOST: cdk.Fn.importValue('AtuaDBEndpoint'),
        DB_NAME: 'atua',
        DB_USER: 'atuaadmin'
      },
      tracing: lambda.Tracing.DISABLED // X-Ray tem custo
    });

    // API Gateway REST
    const api = new apigateway.RestApi(this, 'AtuaAPI', {
      restApiName: 'Atua REST API',
      description: 'API Gateway para backend Atua',
      deployOptions: {
        stageName: 'prod',
        throttlingRateLimit: 100,
        throttlingBurstLimit: 200,
        tracingEnabled: true
      },
      defaultCorsPreflightOptions: {
        allowOrigins: apigateway.Cors.ALL_ORIGINS, // Ajustar para domínio específico
        allowMethods: apigateway.Cors.ALL_METHODS
      }
    });

    // Proxy tudo para Lambda
    api.root.addProxy({
      defaultIntegration: new apigateway.LambdaIntegration(backendFunction)
    });

    // Output
    new cdk.CfnOutput(this, 'ApiUrl', {
      value: api.url,
      description: 'API Gateway URL'
    });
  }
}
```

**Checklist (FREE TIER):**
- [ ] Criar Lambda para backend (Node.js 20, 256MB RAM)
- [ ] **Free Tier:** 1M requests/mês + 400k GB-s compute
- [ ] SEM VPC ou em subnet pública (sem NAT = $0)
- [ ] Configurar variáveis de ambiente (DB_HOST)
- [ ] Criar API Gateway REST (**Free Tier:** 1M requests/mês nos primeiros 12 meses)
- [ ] Configurar CORS (ajustar origins)
- [ ] CloudWatch Logs (Free Tier: 5GB ingest)
- [ ] ⚠️ **Desabilitar X-Ray** (tem custo)
- [ ] Throttling: 10 req/s (proteger do estouro)

---

### 5.3 Database: RDS PostgreSQL

**Criar stack:** `infra/lib/database-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as rds from 'aws-cdk-lib/aws-rds';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import { Construct } from 'constructs';

export class DatabaseStack extends cdk.Stack {
  constructor(scope: Construct, id: string, vpc: ec2.Vpc, props?: cdk.StackProps) {
    super(scope, id, props);

    // Secret para credenciais RDS
    const dbSecret = new secretsmanager.Secret(this, 'DBSecret', {
      secretName: 'atua/prod/db-credentials',
      generateSecretString: {
        secretStringTemplate: JSON.stringify({ username: 'atuaadmin' }),
        generateStringKey: 'password',
        excludePunctuation: true,
        passwordLength: 32
      }
    });

    // ⚠️ ALTERNATIVA FREE TIER: RDS Free Tier (750h/mês t2.micro/t3.micro por 12 meses)
    const dbInstance = new rds.DatabaseInstance(this, 'AtuaDB', {
      instanceIdentifier: 'atua-postgres',
      engine: rds.DatabaseInstanceEngine.postgres({
        version: rds.PostgresEngineVersion.VER_15_4
      }),
      instanceType: ec2.InstanceType.of(
        ec2.InstanceClass.T3,
        ec2.InstanceSize.MICRO // FREE TIER: 750h/mês (24/7 = 720h)
      ),
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_ISOLATED },
      credentials: rds.Credentials.fromSecret(dbSecret),
      databaseName: 'atua',
      multiAz: false, // ⚠️ Multi-AZ = custo 2x (fora do Free Tier)
      allocatedStorage: 20, // FREE TIER: 20GB SSD (gp2)
      maxAllocatedStorage: 20, // ⚠️ Desabilitar autoscaling (evitar custo)
      storageEncrypted: false, // ⚠️ Encryption adiciona custo KMS
      backupRetention: cdk.Duration.days(1), // Mínimo (Free Tier: backups = storage usado)
      deletionProtection: false, // Facilitar cleanup se necessário
      enablePerformanceInsights: false, // ⚠️ Performance Insights tem custo
      publiclyAccessible: false // Manter privado
    });

    // Permitir acesso de Lambda (já configurado no NetworkStack)

    // Output
    new cdk.CfnOutput(this, 'DBEndpoint', {
      value: dbInstance.dbInstanceEndpointAddress,
      exportName: 'AtuaDBEndpoint'
    });

    new cdk.CfnOutput(this, 'DBSecretArn', {
      value: dbSecret.secretArn,
      exportName: 'AtuaDBSecretArn'
    });
  }
}
```

**Checklist (FREE TIER - 12 meses):**
- [ ] **Criar RDS PostgreSQL 15 (t3.micro)** - FREE TIER: 750h/mês
- [ ] **Storage: 20GB** - FREE TIER (não aumentar!)
- [ ] Deploy em subnet isolada
- [ ] Credenciais via Secrets Manager (⚠️ $0.40/secret/mês)
- [ ] ⚠️ **SEM criptografia** (KMS tem custo)
- [ ] Backups: 1 dia (mínimo, Free Tier = storage usado)
- [ ] ⚠️ **SEM Multi-AZ** (dobra custo)
- [ ] ⚠️ **SEM Performance Insights** (tem custo)
- [ ] Security Group: acesso apenas de Lambda
- [ ] ⚠️ **MONITORAR: Free Tier expira após 12 meses!**

---

### 5.4 Storage: S3

**Criar stack:** `infra/lib/storage-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import { Construct } from 'constructs';

export class StorageStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Bucket para uploads de usuários
    const assetsBucket = new s3.Bucket(this, 'AssetsBucket', {
      bucketName: `moldato-atua-assets-${cdk.Stack.of(this).account}`,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      versioned: true,
      lifecycleRules: [{
        transitions: [{
          storageClass: s3.StorageClass.INTELLIGENT_TIERING,
          transitionAfter: cdk.Duration.days(30)
        }]
      }],
      cors: [{
        allowedMethods: [
          s3.HttpMethods.GET,
          s3.HttpMethods.PUT,
          s3.HttpMethods.POST
        ],
        allowedOrigins: ['https://app.moldato.com'], // Ajustar domínio
        allowedHeaders: ['*'],
        maxAge: 3000
      }]
    });

    // Output
    new cdk.CfnOutput(this, 'AssetsBucketName', {
      value: assetsBucket.bucketName,
      exportName: 'AtuaAssetsBucketName'
    });
  }
}
```

**Checklist:**
- [ ] Criar bucket para uploads (fotos, documentos)
- [ ] Criptografia S3-Managed (SSE-S3)
- [ ] Bloquear acesso público (usar presigned URLs)
- [ ] Habilitar versionamento
- [ ] Lifecycle: Intelligent Tiering após 30 dias
- [ ] Configurar CORS (ajustar origin)
- [ ] Integrar com Lambda (presigned URLs para upload)

---

### 5.5 Frontend: S3 + CloudFront

**Criar stack:** `infra/lib/frontend-stack.ts`

```typescript
import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as s3deploy from 'aws-cdk-lib/aws-s3-deployment';
import { Construct } from 'constructs';

export class FrontendStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Bucket para frontend (React build)
    const frontendBucket = new s3.Bucket(this, 'FrontendBucket', {
      bucketName: `moldato-atua-frontend-${cdk.Stack.of(this).account}`,
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      removalPolicy: cdk.RemovalPolicy.RETAIN
    });

    // Origin Access Identity (OAI) para CloudFront acessar S3
    const oai = new cloudfront.OriginAccessIdentity(this, 'OAI');
    frontendBucket.grantRead(oai);

    // CloudFront Distribution
    const distribution = new cloudfront.Distribution(this, 'FrontendCDN', {
      defaultBehavior: {
        origin: new origins.S3Origin(frontendBucket, {
          originAccessIdentity: oai
        }),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        compress: true,
        allowedMethods: cloudfront.AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
        cachedMethods: cloudfront.CachedMethods.CACHE_GET_HEAD_OPTIONS
      },
      defaultRootObject: 'index.html',
      errorResponses: [
        {
          httpStatus: 404,
          responseHttpStatus: 200,
          responsePagePath: '/index.html', // SPA routing
          ttl: cdk.Duration.seconds(0)
        }
      ],
      priceClass: cloudfront.PriceClass.PRICE_CLASS_100 // Apenas América + Europa
      // certificate: acmCertificate, // ACM certificate para domínio customizado
      // domainNames: ['app.moldato.com']
    });

    // Deploy automático do build
    new s3deploy.BucketDeployment(this, 'DeployFrontend', {
      sources: [s3deploy.Source.asset('../frontend/dist')],
      destinationBucket: frontendBucket,
      distribution,
      distributionPaths: ['/*'] // Invalidar cache
    });

    // Output
    new cdk.CfnOutput(this, 'CloudFrontURL', {
      value: `https://${distribution.distributionDomainName}`,
      description: 'CloudFront URL'
    });
  }
}
```

**Checklist:**
- [ ] Criar S3 bucket para frontend (React build)
- [ ] Configurar CloudFront distribution
- [ ] Habilitar compressão (Gzip/Brotli)
- [ ] Configurar error pages (SPA routing)
- [ ] Solicitar ACM certificate para domínio (app.moldato.com)
- [ ] Configurar domínio personalizado no CloudFront
- [ ] Adicionar Route53 record (CNAME → CloudFront)

---

## 📊 Fase 6: Monitoramento e Observabilidade

### 6.1 CloudWatch Dashboards

```typescript
// Adicionar ao backend-stack.ts ou criar monitoring-stack.ts
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';

const dashboard = new cloudwatch.Dashboard(this, 'AtuaDashboard', {
  dashboardName: 'Moldato-Atua-Production'
});

dashboard.addWidgets(
  new cloudwatch.GraphWidget({
    title: 'API Gateway Requests',
    left: [api.metricCount()],
    width: 12
  }),
  new cloudwatch.GraphWidget({
    title: 'Lambda Invocations',
    left: [backendFunction.metricInvocations()],
    width: 12
  }),
  new cloudwatch.GraphWidget({
    title: 'Lambda Errors',
    left: [backendFunction.metricErrors()],
    width: 12
  }),
  new cloudwatch.GraphWidget({
    title: 'RDS CPU Utilization',
    left: [dbInstance.metricCPUUtilization()],
    width: 12
  })
);

// Alarmes críticos
const errorAlarm = new cloudwatch.Alarm(this, 'LambdaErrorAlarm', {
  metric: backendFunction.metricErrors(),
  threshold: 10,
  evaluationPeriods: 2,
  datapointsToAlarm: 2,
  alarmDescription: 'Lambda errors > 10 em 2 períodos consecutivos'
});

// TODO: Integrar com SNS para notificações
```

**Checklist:**
- [ ] Criar dashboard CloudWatch (API Gateway, Lambda, RDS)
- [ ] Configurar alarmes: Lambda errors > 5%, RDS CPU > 80%, RDS Storage < 20%
- [ ] Criar SNS topic para alertas
- [ ] Subscrever e-mail financeiro@moldato.com
- [ ] Integrar com Slack/PagerDuty (opcional)

---

### 6.2 X-Ray (Distributed Tracing)

```typescript
// Já configurado no backend-stack.ts
tracing: lambda.Tracing.ACTIVE

// No código Lambda (Node.js)
import AWSXRay from 'aws-xray-sdk-core';
const AWS = AWSXRay.captureAWS(require('aws-sdk'));

// Instrumentar chamadas HTTP
AWSXRay.captureHTTPsGlobal(require('http'));
AWSXRay.captureHTTPsGlobal(require('https'));
```

**Checklist:**
- [ ] Ativar X-Ray no Lambda (já feito via CDK)
- [ ] Ativar X-Ray no API Gateway (já feito via CDK)
- [ ] Instrumentar código backend com X-Ray SDK
- [ ] Analisar service map e traces no console

---

## 🔐 Fase 7: Secrets Management

**Checklist:**
- [x] Credenciais RDS → Secrets Manager (já feito no DatabaseStack)
- [ ] Criar secrets adicionais:
  - JWT_SECRET (autenticação)
  - SMTP credentials (e-mails transacionais)
  - API keys de terceiros (Google Maps, etc.)
- [ ] Configurar rotação automática (Lambda rotation)
- [ ] Conceder acesso Lambda via IAM policy
- [ ] Referenciar secrets no Lambda via environment variables

```typescript
// No backend-stack.ts
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';

const jwtSecret = new secretsmanager.Secret(this, 'JWTSecret', {
  secretName: 'atua/prod/jwt-secret',
  generateSecretString: {
    passwordLength: 64,
    excludePunctuation: true
  }
});

// Conceder acesso ao Lambda
jwtSecret.grantRead(backendFunction);

// No código Lambda
const AWS = require('aws-sdk');
const secretsManager = new AWS.SecretsManager();

const getSecret = async (secretName) => {
  const data = await secretsManager.getSecretValue({ SecretId: secretName }).promise();
  return JSON.parse(data.SecretString);
};
```

---

## 📝 Fase 8: Documentação e Compliance

**Checklist:**
- [ ] Documentar arquitetura AWS (diagrama C4 + AWS Architecture Icons)
- [ ] Documentar processo de deploy (CI/CD com GitHub Actions)
- [ ] Criar runbook de incidentes (RDS down, Lambda throttling)
- [ ] Política de backup e disaster recovery (RPO/RTO)
- [ ] Compliance LGPD (armazenamento de dados pessoais)
- [ ] Documentar custos mensais estimados
- [ ] Criar guia de troubleshooting

---

## ✅ Status Geral

### ✅ Concluído
1. ✅ Criar conta AWS Moldato
2. ✅ Configurar MFA no root user
3. ✅ Criar grupos IAM (Administrators)
4. ✅ Criar usuário IAM admin-devops

### ⚠️ Próximos Passos Imediatos
5. ⚠️ **Configurar MFA no admin-devops** (URGENTE)
6. ⚠️ **Salvar Access Keys em cofre seguro**
7. ⚠️ Configurar AWS CLI local (`aws configure --profile moldato`)
8. ⚠️ Testar autenticação CLI
9. ⚠️ Instalar e fazer bootstrap do CDK
10. ⚠️ Criar budgets e alertas de custo

### 🔄 Em Planejamento
11. Provisionar VPC e networking via CDK
12. Deploy Lambda + API Gateway
13. Provisionar RDS PostgreSQL
14. Configurar S3 para assets
15. Deploy frontend (S3 + CloudFront)
16. Configurar monitoramento (CloudWatch + X-Ray)
17. Ativar CloudTrail, GuardDuty, Config

---

## 🚨 Custos Estimados (MODO ECONOMIA - Moldato)

### 💰 Usando Free Tier (primeiros 12 meses):

| Serviço            | Configuração       | Free Tier           | Custo Real |
|--------------------|--------------------|--------------------|------------|
| RDS t3.micro       | 20GB, single-AZ    | 750h/mês (24/7)    | **$0**     |
| Lambda             | 256MB, <1M req/mês | 1M req + 400k GB-s | **$0**     |
| API Gateway        | <1M req/mês        | 1M req (12 meses)  | **$0**     |
| NAT Gateway        | ⚠️ **REMOVIDO**    | N/A                | **$0**     |
| CloudFront         | <10GB/mês          | 1TB + 10M requests | **$0**     |
| S3                 | <5GB storage       | 5GB + 20k GET      | **$0**     |
| CloudTrail         | 1 trail            | 1 trail grátis     | **$0**     |
| Secrets Manager    | 2 secrets          | Nenhum             | **$0.80**  |
| CloudWatch Logs    | <5GB ingest        | 5GB                | **$0**     |
| **TOTAL**          |                    |                    | **~$0.80/mês** |

### 📊 Após expirar Free Tier (12 meses):

| Serviço            | Custo Pós-Free Tier |
|--------------------|---------------------|
| RDS t3.micro       | ~$15/mês            |
| Lambda             | ~$0.20/mês          |
| API Gateway        | ~$3.50/mês          |
| S3 + CloudFront    | ~$2/mês             |
| Secrets Manager    | ~$0.80/mês          |
| **TOTAL**          | **~$22/mês**        |

### ⚠️ Estratégia de Custo Crítica:

**Créditos AWS:** $100 disponíveis  
**Budget Alerta:** $10/mês (alerta em $5, $8, $10)  
**Budget Máximo:** $20/mês (**PROJETO CAI**)  

**Ações obrigatórias:**
1. ✅ **Usar APENAS Free Tier** nos primeiros 12 meses
2. ✅ **Remover NAT Gateway** ($32 → $0)
3. ✅ **Lambda sem VPC** ou subnet pública
4. ✅ **Desabilitar X-Ray, Performance Insights**
5. ✅ **Minimal Secrets Manager** (apenas o essencial)
6. ✅ **Monitorar diariamente** (Cost Explorer)
7. ⚠️ **Planejar migração** antes de expirar Free Tier

**⚠️ Configurar alertas:**
- $5 (50% do budget crítico)
- $8 (80% do budget crítico)
- $10 (100% - ALERTA MÁXIMO)
- $20 (PROJETO CAI - DESLIGAR TUDO)

### 🎯 Como manter em $0-1/mês:

1. **RDS Free Tier:** t3.micro, 20GB, 750h/mês (nunca exceder)
2. **Lambda Free Tier:** 256MB RAM, <1M requests
3. **API Gateway Free Tier:** <1M requests (12 meses)
4. **S3 Free Tier:** <5GB storage, <20k GET
5. **CloudFront Free Tier:** <1TB transferência
6. **CloudWatch:** <5GB logs
7. **Secrets Manager:** Apenas 2 secrets ($0.80)

**Total estimado com Free Tier:** **$0.80-2/mês**  
**Total após 12 meses:** **$22/mês** (replanejar antes!)

---

## 🔗 Próximos Passos

1. ✅ **Configurar MFA no admin-devops** (AGORA)
2. ⚙️ **Setup AWS CLI** + testar autenticação
3. 🏗️ **Inicializar CDK** + bootstrap na conta
4. 💰 **Criar budgets** e alertas de custo
5. 🛡️ **Provisionar segurança** (CloudTrail, GuardDuty)
6. 🚀 **Deploy infraestrutura** base (VPC, RDS, Lambda)

---

## 📚 Referências

- [AWS Well-Architected Framework](https://aws.amazon.com/architecture/well-architected/)
- [AWS CDK Best Practices](https://docs.aws.amazon.com/cdk/latest/guide/best-practices.html)
- [AWS Security Best Practices](https://aws.amazon.com/security/best-practices/)
- [CIS AWS Foundations Benchmark](https://www.cisecurity.org/benchmark/amazon_web_services)
- [AWS Cost Optimization](https://aws.amazon.com/pricing/cost-optimization/)

---

**Mantido por:** DevOps Team Moldato  
**Última atualização:** 2025-01-29  
**Contato:** financeiro@moldato.com | +55 (84) 9 9407-7677
