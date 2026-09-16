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

// eslint-disable-next-line @typescript-eslint/no-unused-vars
const computeStack = new AtuaComputeStack(app, 'AtuaComputeStack', {
  env,
  description: 'ATUA MVP - Compute (API Master). EFÊMERO: destruído/recriado no ciclo down/up. Sem Collector nesta leva.',
  vpc: networkStack.vpc,
  sgApi: networkStack.sgApi,
  sgCollector: networkStack.sgCollector,
  releasesBucket: dataStack.releasesBucket,
  rdsSecret: dataStack.rdsSecret,
  mongoSecret: dataStack.mongoSecret,
  appSecret: dataStack.appSecret,
  keyPair: networkStack.keyPair,
  credentialCipherKey: dataStack.credentialCipherKey,
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
