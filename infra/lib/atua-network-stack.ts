import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import { Construct } from 'constructs';

/**
 * AtuaNetworkStack
 *
 * Camada de REDE do MVP (ADR-012 / Plano v3).
 *
 * Custo esperado: US$ 0,00/mês (VPC, subnets, Internet Gateway,
 * route tables e Security Groups não têm custo próprio na AWS).
 *
 * Ciclo de vida: PERSISTENTE. Não faz parte do fluxo "down"/"up" de
 * economia de custo — não há razão para destruir esta stack entre
 * usos, pois ela não gera cobrança quando ociosa.
 *
 * Topologia:
 *  - 1 VPC (10.0.0.0/16), sem NAT Gateway (restrição vigente).
 *  - 2 subnets públicas (2 AZs, exigido pelo DB Subnet Group do RDS
 *    mesmo em topologia Single-AZ).
 *  - 2 subnets privadas isoladas, dedicadas ao RDS (sem rota para a
 *    internet; RDS não precisa de saída, então não requer NAT).
 *  - Internet Gateway (gratuito) para as subnets públicas.
 *
 * Security Groups definidos aqui (não em Compute/Data) para que a
 * topologia de rede/segurança sobreviva a um "down" de Compute.
 *
 * Key Pair: também definido aqui (persistente, sem custo) via o
 * construct nativo `ec2.KeyPair`. A AWS gera o par de chaves e
 * armazena a chave PRIVADA automaticamente em SSM Parameter Store
 * (SecureString, `/ec2/keypair/<key-pair-id>`) - o material privado
 * NUNCA passa pelo código/repositório. Recuperação documentada no
 * README.md.
 */
export class AtuaNetworkStack extends cdk.Stack {
  public readonly vpc: ec2.Vpc;
  public readonly sgApi: ec2.SecurityGroup;
  public readonly sgCollector: ec2.SecurityGroup;
  public readonly sgRds: ec2.SecurityGroup;
  public readonly keyPair: ec2.KeyPair;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    this.vpc = new ec2.Vpc(this, 'AtuaVpc', {
      vpcName: 'atua-mvp-vpc',
      ipAddresses: ec2.IpAddresses.cidr('10.0.0.0/16'),
      maxAzs: 2,
      natGateways: 0, // restrição vigente: SEM NAT Gateway
      subnetConfiguration: [
        {
          name: 'atua-mvp-public',
          subnetType: ec2.SubnetType.PUBLIC,
          cidrMask: 24,
        },
        {
          name: 'atua-mvp-private-rds',
          subnetType: ec2.SubnetType.PRIVATE_ISOLATED,
          cidrMask: 24,
        },
      ],
    });

    // --- Security Group: API Master (EC2, subnet pública) ---
    this.sgApi = new ec2.SecurityGroup(this, 'SgApi', {
      securityGroupName: 'atua-mvp-sg-api',
      vpc: this.vpc,
      description: 'API Master (ASP.NET Core) - ingress HTTP/HTTPS/SSH restrito',
      allowAllOutbound: true,
    });
    this.sgApi.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'HTTPS publico');
    this.sgApi.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(80), 'HTTP publico (redirect para HTTPS)');
    // SSH administrativo restrito ao IP autorizado do operador.
    this.sgApi.addIngressRule(ec2.Peer.ipv4('186.236.211.36/32'), ec2.Port.tcp(22), 'SSH administrativo');

    // --- Security Group: Collector (instância BASE, SEM logica de coleta) ---
    // A instância do Collector nasce e morre junto com a API Master no
    // mesmo ciclo up/down (ver atua-compute-stack.ts), mas NAO roda
    // nenhum software de scraping/Playwright/agendamento - apenas SO+rede.
    // Egress restrito a HTTPS (443), unico protocolo que a futura
    // integracao com o iService vai precisar.
    this.sgCollector = new ec2.SecurityGroup(this, 'SgCollector', {
      securityGroupName: 'atua-mvp-sg-collector',
      vpc: this.vpc,
      description:
        'Collector (instancia BASE, sem logica). Egress restrito a HTTPS (443). ' +
        'Sem ingress - nenhum acesso de entrada previsto ate a implementacao futura.',
      allowAllOutbound: false,
    });
    this.sgCollector.addEgressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'Egress HTTPS apenas (futuro iService)');
    // Sem regras de ingress: nenhum acesso de entrada previsto para o Collector.

    // --- Security Group: RDS PostgreSQL (subnet privada isolada) ---
    this.sgRds = new ec2.SecurityGroup(this, 'SgRds', {
      securityGroupName: 'atua-mvp-sg-rds',
      vpc: this.vpc,
      description: 'RDS PostgreSQL - acesso somente da API (e, no futuro, do Collector)',
      allowAllOutbound: false,
    });
    this.sgRds.addIngressRule(this.sgApi, ec2.Port.tcp(5432), 'Postgres a partir da API Master');
    this.sgRds.addIngressRule(this.sgCollector, ec2.Port.tcp(5432), 'Postgres a partir do Collector (quando ativado)');

    // --- Key Pair dedicado do projeto ---
    // Gerado automaticamente pelo CDK (sem publicKeyMaterial => a AWS
    // cria o par e guarda a chave privada em SSM Parameter Store,
    // SecureString, nunca em código/repositório).
    this.keyPair = new ec2.KeyPair(this, 'AtuaMvpKeyPair', {
      keyPairName: 'atua-mvp-key',
      type: ec2.KeyPairType.RSA,
      format: ec2.KeyPairFormat.PEM,
    });

    // Outputs úteis para os scripts do Makefile (ex.: bootstrap/restore
    // do RDS, que é gerenciado fora do CDK - ver atua-data-stack.ts).
    new cdk.CfnOutput(this, 'VpcId', { value: this.vpc.vpcId, exportName: 'AtuaVpcId' });
    new cdk.CfnOutput(this, 'RdsSecurityGroupId', { value: this.sgRds.securityGroupId, exportName: 'AtuaRdsSgId' });
    new cdk.CfnOutput(this, 'RdsSubnetIds', {
      value: this.vpc.isolatedSubnets.map((s) => s.subnetId).join(','),
      exportName: 'AtuaRdsSubnetIds',
    });
    new cdk.CfnOutput(this, 'KeyPairName', { value: this.keyPair.keyPairName });
  }
}
