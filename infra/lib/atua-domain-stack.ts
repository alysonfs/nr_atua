import * as cdk from 'aws-cdk-lib';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as cloudfront from 'aws-cdk-lib/aws-cloudfront';
import * as origins from 'aws-cdk-lib/aws-cloudfront-origins';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as route53_targets from 'aws-cdk-lib/aws-route53-targets';
import * as s3 from 'aws-cdk-lib/aws-s3';
import { Construct } from 'constructs';

const DOMAIN = 'atyno.com.br';

export interface AtuaDomainStackProps extends cdk.StackProps {
  frontendsBucketName: string;
  frontendsBucketRegion: string;
}

/**
 * Public edge for the ATUA web applications.
 *
 * The .com.br registration remains at GoDaddy. Route 53 is authoritative
 * after the operator delegates the nameservers there.
 */
export class AtuaDomainStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: AtuaDomainStackProps) {
    super(scope, id, props);

    const zone = new route53.PublicHostedZone(this, 'HostedZone', {
      zoneName: DOMAIN,
      comment: 'ATUA public DNS; registration remains at GoDaddy',
    });

    const certificate = new acm.Certificate(this, 'WebCertificate', {
      domainName: DOMAIN,
      subjectAlternativeNames: [
        `www.${DOMAIN}`,
        `office.${DOMAIN}`,
        `manager.${DOMAIN}`,
        `tecnica.${DOMAIN}`,
        `api.${DOMAIN}`,
      ],
      validation: acm.CertificateValidation.fromDns(zone),
    });

    new route53.TxtRecord(this, 'DmarcRecord', {
      zone,
      recordName: `_dmarc.${DOMAIN}`,
      ttl: cdk.Duration.hours(1),
      values: ['v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;'],
    });

    new route53.CnameRecord(this, 'DomainConnectRecord', {
      zone,
      recordName: `_domainconnect.${DOMAIN}`,
      ttl: cdk.Duration.hours(1),
      domainName: '_domainconnect.gd.domaincontrol.com',
    });

    const bucket = s3.Bucket.fromBucketAttributes(this, 'FrontendsBucket', {
      bucketName: props.frontendsBucketName,
      region: props.frontendsBucketRegion,
    });

    const applications = [
      { id: 'Landing', hostname: DOMAIN, originPath: '/landing' },
      { id: 'Office', hostname: `office.${DOMAIN}`, originPath: '/office' },
      { id: 'Manager', hostname: `manager.${DOMAIN}`, originPath: '/manager' },
      { id: 'Tecnica', hostname: `tecnica.${DOMAIN}`, originPath: '/tecnica' },
    ];

    const apiDistribution = new cloudfront.Distribution(this, 'ApiDistribution', {
      comment: `ATUA API — api.${DOMAIN}`,
      certificate,
      domainNames: [`api.${DOMAIN}`],
      defaultBehavior: {
        origin: new origins.HttpOrigin(`api-origin.${DOMAIN}`, {
          httpPort: 80,
          protocolPolicy: cloudfront.OriginProtocolPolicy.HTTP_ONLY,
        }),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL,
        cachedMethods: cloudfront.CachedMethods.CACHE_GET_HEAD_OPTIONS,
        cachePolicy: cloudfront.CachePolicy.CACHING_DISABLED,
        originRequestPolicy: cloudfront.OriginRequestPolicy.ALL_VIEWER,
      },
    });

    new route53.ARecord(this, 'ApiAliasA', {
      zone,
      recordName: `api.${DOMAIN}`,
      target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(apiDistribution)),
    });
    new route53.AaaaRecord(this, 'ApiAliasAAAA', {
      zone,
      recordName: `api.${DOMAIN}`,
      target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(apiDistribution)),
    });

    for (const application of applications) {
      const distribution = new cloudfront.Distribution(this, `${application.id}Distribution`, {
        comment: `ATUA ${application.id} — ${application.hostname}`,
        certificate,
        domainNames: application.hostname === DOMAIN
          ? [DOMAIN, `www.${DOMAIN}`]
          : [application.hostname],
        defaultRootObject: 'index.html',
        defaultBehavior: {
          origin: origins.S3BucketOrigin.withOriginAccessControl(bucket, {
            originPath: application.originPath,
          }),
          viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
          cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
        },
        errorResponses: [
          { httpStatus: 403, responseHttpStatus: 200, responsePagePath: '/index.html', ttl: cdk.Duration.minutes(1) },
          { httpStatus: 404, responseHttpStatus: 200, responsePagePath: '/index.html', ttl: cdk.Duration.minutes(1) },
        ],
      });

      new route53.ARecord(this, `${application.id}AliasA`, {
        zone,
        recordName: application.hostname,
        target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(distribution)),
      });
      new route53.AaaaRecord(this, `${application.id}AliasAAAA`, {
        zone,
        recordName: application.hostname,
        target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(distribution)),
      });

      if (application.hostname === DOMAIN) {
        new route53.ARecord(this, 'WwwAliasA', {
          zone,
          recordName: `www.${DOMAIN}`,
          target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(distribution)),
        });
        new route53.AaaaRecord(this, 'WwwAliasAAAA', {
          zone,
          recordName: `www.${DOMAIN}`,
          target: route53.RecordTarget.fromAlias(new route53_targets.CloudFrontTarget(distribution)),
        });
      }
    }

    new cdk.CfnOutput(this, 'HostedZoneId', { value: zone.hostedZoneId });
    new cdk.CfnOutput(this, 'NameServers', { value: cdk.Fn.join(',', zone.hostedZoneNameServers ?? []) });
    new cdk.CfnOutput(this, 'CertificateArn', { value: certificate.certificateArn });
  }
}
