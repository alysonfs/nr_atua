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

    // --- Security Group: Collector (Agente Coletor, RF-009/016/017/023) ---
    // A instância do Collector nasce e morre junto com a API Master no
    // mesmo ciclo up/down (ver atua-compute-stack.ts). Roda o Worker .NET
    // (Playwright + consumer de Change Streams) implementado a partir de
    // 2026-09-01 (gate assistido aberto explicitamente pelo usuário).
    // Egress restrito ao mínimo necessário: HTTPS (443) para Secrets
    // Manager/SSM/S3 e o futuro iService; MongoDB (27017) para o Atlas
    // (conexão direta aos shards do replica set, distinta do DNS SRV lookup);
    // HTTP (80) apenas para a API Master (mesma VPC, sem domínio/HTTPS
    // ainda); Postgres (5432) apenas para o RDS (consumer do ADR-023).
    this.sgCollector = new ec2.SecurityGroup(this, 'SgCollector', {
      securityGroupName: 'atua-mvp-sg-collector',
      vpc: this.vpc,
      // NOTA: GroupDescription do AWS::EC2::SecurityGroup é imutável — mudar
      // este texto força replacement, e como o nome é explícito
      // (securityGroupName), o CloudFormation falha com "already exists"
      // (precisa deletar antes de criar, sem downtime-safe create-before-delete
      // possível com nome fixo). Por isso o texto abaixo é mantido IDÊNTICO ao
      // já deployado (legado, refere-se ao design antigo "instância base sem
      // lógica") mesmo após a implementação real do Worker — ver o comentário
      // acima para a descrição real e atualizada do propósito deste SG.
      description:
        'Collector (instancia BASE, sem logica). Egress restrito a HTTPS (443). Sem ingress - nenhum acesso de entrada previsto ate a implementacao futura.',
      allowAllOutbound: false,
    });
    this.sgCollector.addEgressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'Egress HTTPS (AWS APIs, iService)');
    // MongoDB Atlas usa a porta 27017 para as conexões diretas aos shards do
    // replica set, mesmo com o esquema mongodb+srv:// (o SRV/TXT lookup via
    // DNS usa 53/443, mas a conexão de dados em si é sempre 27017). BUG
    // CORRIGIDO: a regra original só liberava 443, bloqueando toda conexão
    // real com o Atlas (confirmado via teste de TCP direto na instância).
    this.sgCollector.addEgressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(27017), 'Egress MongoDB Atlas (conexao direta aos shards do replica set)');
    this.sgCollector.addEgressRule(this.sgApi, ec2.Port.tcp(80), 'Egress HTTP para API Master (claim/complete/eligibility)');
    // SSH administrativo restrito ao IP autorizado do operador (mesmo padrão da API,
    // usado apenas para diagnóstico manual — não faz parte do fluxo normal).
    this.sgCollector.addIngressRule(ec2.Peer.ipv4('186.236.211.36/32'), ec2.Port.tcp(22), 'SSH administrativo');
    // Sem outras regras de ingress: nenhum outro acesso de entrada previsto.

    // --- Security Group: RDS PostgreSQL (subnet privada isolada) ---
    this.sgRds = new ec2.SecurityGroup(this, 'SgRds', {
      securityGroupName: 'atua-mvp-sg-rds',
      vpc: this.vpc,
      description: 'RDS PostgreSQL - acesso somente da API (e, no futuro, do Collector)',
      allowAllOutbound: false,
    });
    this.sgRds.addIngressRule(this.sgApi, ec2.Port.tcp(5432), 'Postgres a partir da API Master');
    this.sgRds.addIngressRule(this.sgCollector, ec2.Port.tcp(5432), 'Postgres a partir do Collector (quando ativado)');
    this.sgCollector.addEgressRule(this.sgRds, ec2.Port.tcp(5432), 'Egress Postgres para o RDS (consumer ADR-023)');
    // Acesso local do operador (desenvolvimento, RDS com PubliclyAccessible=true).
    // IP dinâmico do provedor — atualizar este /32 quando o IP mudar.
    this.sgRds.addIngressRule(ec2.Peer.ipv4('186.236.211.186/32'), ec2.Port.tcp(5432), 'Postgres do operador (dev local)');

    // Rota para o IGW nas subnets do RDS: necessária para o acesso público
    // direto ao RDS (PubliclyAccessible=true) funcionar — sem esta rota o
    // tráfego de retorno não sai da VPC e a conexão dá timeout. As subnets
    // continuam sem NAT; instâncias sem IP público seguem inalcançáveis.
    this.vpc.isolatedSubnets.forEach((subnet, i) => {
      new ec2.CfnRoute(this, `RdsSubnetIgwRoute${i + 1}`, {
        routeTableId: (subnet as ec2.Subnet).routeTable.routeTableId,
        destinationCidrBlock: '0.0.0.0/0',
        gatewayId: this.vpc.internetGatewayId,
      });
    });


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
