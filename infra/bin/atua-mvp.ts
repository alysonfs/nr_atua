#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { AtuaNetworkStack } from '../lib/atua-network-stack';
import { AtuaDataStack } from '../lib/atua-data-stack';
import { AtuaComputeStack } from '../lib/atua-compute-stack';
import { AtuaDomainStack } from '../lib/atua-domain-stack';

const app = new cdk.App();

// Região confirmada pelo orchestrator: sa-east-1.
// Conta: resolvida do ambiente/credenciais configuradas (nunca hardcoded).
const env: cdk.Environment = {
  account: process.env.CDK_DEFAULT_ACCOUNT,
  region: process.env.CDK_DEPLOY_REGION ?? 'sa-east-1',
};

const networkStack = new AtuaNetworkStack(app, 'AtuaNetworkStack', {
  env,
  description: 'ATUA MVP - Rede (VPC, subnets, Security Groups). Persistente, custo $0.',
  tags: {
    Project: 'atua',
    Environment: 'dev-mvp',
    ManagedBy: 'aws-architect',
    CostCenter: 'mvp-adr012',
    Component: 'network',
  },
});

const dataStack = new AtuaDataStack(app, 'AtuaDataStack', {
  env,
  description: 'ATUA MVP - Dados persistentes (S3, Secrets Manager). RDS é gerenciado fora do CDK (ver README).',
  tags: {
    Project: 'atua',
    Environment: 'dev-mvp',
    ManagedBy: 'aws-architect',
    CostCenter: 'mvp-adr012',
    Component: 'data',
  },
});

// Recomendação 1 de corte de custo (Cost Explorer, set/2026): permite rodar
// o Collector na mesma instância da API (uma única t3.micro) em vez de duas.
// Default false — preserva a topologia atual de duas instâncias. Ativação
// explícita via contexto CDK: `-c collocateCollector=true` (nunca por
// alteração silenciosa deste arquivo). Ver infra/lib/atua-compute-stack.ts e
// docs/architecture/aws-ambiente-mvp.md.
const collocateCollector = app.node.tryGetContext('collocateCollector') === 'true' || app.node.tryGetContext('collocateCollector') === true;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
const computeStack = new AtuaComputeStack(app, 'AtuaComputeStack', {
  env,
  description: collocateCollector
    ? 'ATUA MVP - Compute (API Master + Collector colocados na mesma instância t3.micro). EFÊMERO: destruído/recriado no ciclo down/up.'
    : 'ATUA MVP - Compute (API Master + Collector em instâncias separadas). EFÊMERO: destruído/recriado no ciclo down/up.',
  vpc: networkStack.vpc,
  sgApi: networkStack.sgApi,
  sgCollector: networkStack.sgCollector,
  releasesBucket: dataStack.releasesBucket,
  mediaTriagemBucket: dataStack.mediaTriagemBucket,
  rdsSecret: dataStack.rdsSecret,
  mongoSecret: dataStack.mongoSecret,
  appSecret: dataStack.appSecret,
  keyPair: networkStack.keyPair,
  credentialCipherKey: dataStack.credentialCipherKey,
  collocateCollector,
  tags: {
    Project: 'atua',
    Environment: 'dev-mvp',
    ManagedBy: 'aws-architect',
    CostCenter: 'mvp-adr012',
    Component: 'compute',
  },
});

computeStack.addDependency(networkStack);
computeStack.addDependency(dataStack);

// CloudFront certificates must be issued in us-east-1. Route 53 is global,
// so the hosted zone and its aliases can be managed by this same stack.
const domainStack = new AtuaDomainStack(app, 'AtuaDomainStack', {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: 'us-east-1',
  },
  description: 'ATUA - Route 53, ACM and CloudFront for atyno.com.br.',
  frontendsBucketName: `atua-${cdk.Aws.ACCOUNT_ID}-frontends`,
  frontendsBucketRegion: env.region ?? 'sa-east-1',
  tags: {
    Project: 'atua',
    Environment: 'dev-mvp',
    ManagedBy: 'aws-architect',
    CostCenter: 'mvp-domain',
    Component: 'domain',
  },
});

domainStack.addDependency(dataStack);
