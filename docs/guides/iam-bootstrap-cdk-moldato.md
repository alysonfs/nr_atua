# Recuperação de IAM para `cdk bootstrap` — perfil `moldato`

## Objetivo e escopo

Este guia trata exclusivamente da recuperação do bootstrap AWS CDK na região
`sa-east-1`, quando executado pelo perfil `moldato`. Não contém credenciais,
IDs de conta nem executa alterações. A infraestrutura atual é CDK v2/TypeScript
em `infra/`, com três stacks (`AtuaNetworkStack`, `AtuaDataStack` e
`AtuaComputeStack`); o RDS é operacional e fica fora do CDK.

O administrador deve reconciliar a stack `CDKToolkit` **antes de excluir roles
manualmente**. Uma stack em `CREATE_FAILED` ou `DELETE_FAILED` e seus eventos
identificam a ação e a dependência que falharam; remover roles primeiro pode
agravar o estado ou eliminar a evidência.

## 1. Diagnóstico somente leitura

Execute os comandos abaixo somente com autorização para consulta, usando
explicitamente o perfil e a região:

```bash
# Identifica o principal efetivo (usuário IAM ou role assumida) e a conta-alvo.
aws sts get-caller-identity --profile moldato --region sa-east-1

# Estado da stack. "não encontrada" também é um resultado válido.
aws cloudformation describe-stacks \
  --stack-name CDKToolkit \
  --profile moldato --region sa-east-1

# Eventos em ordem cronológica; examine especialmente ResourceStatusReason.
aws cloudformation describe-stack-events \
  --stack-name CDKToolkit \
  --profile moldato --region sa-east-1 \
  --query 'StackEvents[].[Timestamp,LogicalResourceId,ResourceType,ResourceStatus,ResourceStatusReason]' \
  --output table

# Roles com o prefixo padrão do bootstrap, sem alterar nada.
aws iam list-roles --profile moldato \
  --query 'Roles[?starts_with(RoleName, `cdk-hnb659fds-`)].[RoleName,Arn,CreateDate]' \
  --output table
```

Para cada role listada, o administrador pode inspecionar trust policy e
policies anexadas/inline:

```bash
aws iam get-role --role-name <nome-da-role> --profile moldato
aws iam list-attached-role-policies --role-name <nome-da-role> --profile moldato
aws iam list-role-policies --role-name <nome-da-role> --profile moldato
```

Registre o ARN retornado pelo STS, o status da `CDKToolkit`, os eventos com
falha e as roles residuais. Só então o administrador deve decidir entre
concluir a exclusão/reversão da stack ou permitir que o bootstrap a reconcilie.

## 2. Permissão temporária para o bootstrap

O administrador pode anexar temporariamente a policy abaixo **ao principal
identificado pelo STS**, na mesma conta que receberá o bootstrap. O IAM console
não aceita `${aws:Partition}`/`${aws:PrincipalAccount}` como policy variables
no campo `Resource` de policies identity-based; por isso os ARNs usam a
partição literal `aws` e o ID de conta literal `462991286554` (conta-alvo
`moldato`/`admin-devops`, região `sa-east-1`). Caso a policy precise ser
reutilizada em outra conta, substitua `462991286554` pelo ID correspondente
antes de anexar.
O nome `AtuaCdkBootstrapExecutionPolicy` é uma policy gerenciada pelo cliente
que o administrador deve criar previamente, com permissões de execução
restritas (ver limites).

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "ManageCdkBootstrapRoles",
      "Effect": "Allow",
      "Action": [
        "iam:CreateRole",
        "iam:DeleteRole",
        "iam:GetRole",
        "iam:UpdateAssumeRolePolicy",
        "iam:TagRole",
        "iam:UntagRole",
        "iam:PassRole",
        "iam:AttachRolePolicy",
        "iam:DetachRolePolicy",
        "iam:ListAttachedRolePolicies",
        "iam:ListRolePolicies",
        "iam:GetRolePolicy",
        "iam:PutRolePolicy",
        "iam:DeleteRolePolicy"
      ],
      "Resource": "arn:aws:iam::462991286554:role/cdk-hnb659fds-*"
    },
    {
      "Sid": "AttachOnlyRestrictedBootstrapExecutionPolicy",
      "Effect": "Allow",
      "Action": [
        "iam:AttachRolePolicy",
        "iam:DetachRolePolicy"
      ],
      "Resource": [
        "arn:aws:iam::462991286554:role/cdk-hnb659fds-*",
        "arn:aws:iam::462991286554:policy/AtuaCdkBootstrapExecutionPolicy"
      ]
    },
    {
      "Sid": "ListBootstrapRolesForDiagnosis",
      "Effect": "Allow",
      "Action": "iam:ListRoles",
      "Resource": "*"
    },
    {
      "Sid": "ManageCdkBootstrapAssetsBucket",
      "Effect": "Allow",
      "Action": [
        "s3:CreateBucket",
        "s3:DeleteBucket",
        "s3:GetBucketLocation",
        "s3:GetBucketPolicy",
        "s3:PutBucketPolicy",
        "s3:DeleteBucketPolicy",
        "s3:GetBucketEncryption",
        "s3:PutBucketEncryption",
        "s3:GetBucketPublicAccessBlock",
        "s3:PutBucketPublicAccessBlock",
        "s3:GetBucketVersioning",
        "s3:PutBucketVersioning",
        "s3:GetBucketTagging",
        "s3:PutBucketTagging",
        "s3:GetLifecycleConfiguration",
        "s3:PutLifecycleConfiguration",
        "s3:DeleteLifecycleConfiguration",
        "s3:ListBucket",
        "s3:DeleteObject",
        "s3:DeleteObjectVersion"
      ],
      "Resource": [
        "arn:aws:s3:::cdk-hnb659fds-assets-*",
        "arn:aws:s3:::cdk-hnb659fds-assets-*/*"
      ]
    },
    {
      "Sid": "ListBucketsRequiredByBootstrap",
      "Effect": "Allow",
      "Action": "s3:ListAllMyBuckets",
      "Resource": "*"
    },
    {
      "Sid": "ManageCdkBootstrapEcrRepository",
      "Effect": "Allow",
      "Action": [
        "ecr:CreateRepository",
        "ecr:DeleteRepository",
        "ecr:DescribeRepositories",
        "ecr:GetRepositoryPolicy",
        "ecr:SetRepositoryPolicy",
        "ecr:DeleteRepositoryPolicy",
        "ecr:GetLifecyclePolicy",
        "ecr:PutLifecyclePolicy",
        "ecr:DeleteLifecyclePolicy",
        "ecr:TagResource",
        "ecr:UntagResource"
      ],
      "Resource": "arn:aws:ecr:sa-east-1:462991286554:repository/cdk-hnb659fds-container-assets-*"
    },
    {
      "Sid": "ManageCdkBootstrapVersionParameter",
      "Effect": "Allow",
      "Action": [
        "ssm:GetParameter",
        "ssm:PutParameter",
        "ssm:DeleteParameter"
      ],
      "Resource": "arn:aws:ssm:sa-east-1:462991286554:parameter/cdk-bootstrap/hnb659fds/version"
    },
    {
      "Sid": "ReconcileCdkToolkit",
      "Effect": "Allow",
      "Action": [
        "cloudformation:CreateStack",
        "cloudformation:UpdateStack",
        "cloudformation:DeleteStack",
        "cloudformation:DescribeStacks",
        "cloudformation:DescribeStackEvents",
        "cloudformation:DescribeStackResources",
        "cloudformation:DescribeChangeSet",
        "cloudformation:CreateChangeSet",
        "cloudformation:ExecuteChangeSet",
        "cloudformation:DeleteChangeSet",
        "cloudformation:GetTemplate",
        "cloudformation:GetTemplateSummary"
      ],
      "Resource": "*"
    }
  ]
}
```

### Limites importantes

- `cloudformation:CreateStack` e algumas operações de change set não aceitam
  restrição confiável ao ARN de uma stack que ainda não existe; por isso o
  statement de CloudFormation usa `Resource: "*"`. O uso deve ser temporário,
  acompanhado pelo administrador e limitado operacionalmente ao
  `CDKToolkit` em `sa-east-1`.
- A policy não concede publicação de assets (`s3:PutObject`,
  `ecr:PutImage`) nem permissões para `make up`, RDS ou a aplicação.
- O bootstrap padrão do CDK cria a role
  `cdk-hnb659fds-cfn-exec-role-*`. Se usado sem configuração adicional, ele
  tende a associar uma execution policy muito poderosa (com frequência
  `AdministratorAccess`). Isso não é apropriado como padrão do ATUA.
- Quando viável, o administrador deve criar previamente
  `AtuaCdkBootstrapExecutionPolicy`, limitada aos recursos necessários pelos
  templates sintetizados do ATUA: EC2/VPC e security groups, roles e instance
  profiles IAM do ATUA, buckets S3 `atua-*`, Secrets Manager `atua/*` e
  CloudFormation para as stacks `Atua*`. Essa policy deve ser refinada a partir
  de `make synth`; não substituí-la por `AdministratorAccess`.
- Execute o bootstrap apontando para essa execution policy gerenciada pelo
  cliente, por exemplo:

```bash
pnpm --filter @atua/infra exec cdk bootstrap \
  --cloudformation-execution-policies <arn-da-policy-gerenciada-restrita> \
  aws://<conta-alvo>/sa-east-1
```

Se o evento registrar outro `AccessDenied`, o administrador deve acrescentar
somente a ação e o recurso comprovadamente necessários e repetir a revisão;
não ampliar para permissões administrativas genéricas.

## 3. Fora deste grant: `make rds-bootstrap` e `make up`

Não conceda essas permissões junto com o bootstrap. Elas exigem avaliação
separada após a `CDKToolkit` estar saudável:

- `make rds-bootstrap` cria DB subnet group e instância RDS, consulta e grava
  o secret `atua/rds-postgres`, e consulta outputs da `AtuaNetworkStack`.
- `make up` implanta `AtuaNetworkStack`, `AtuaDataStack` e
  `AtuaComputeStack`; portanto demanda permissões específicas de
  CloudFormation e dos recursos VPC/EC2, S3, Secrets Manager, IAM roles e
  instance profiles definidos em `infra/lib/`. Também pode restaurar ou
  consultar RDS conforme o estado operacional.

A policy de deployment deve ser derivada do `make synth` atual e revisada
separadamente, com escopo de recursos ATUA e menor privilégio.

## 4. Checklist após bootstrap

- [ ] Confirmar `CDKToolkit` em `CREATE_COMPLETE` ou `UPDATE_COMPLETE`.
- [ ] Conferir eventos finais e as roles `cdk-hnb659fds-*`; não deixar roles
  inesperadas.
- [ ] Confirmar o parâmetro SSM de versão do bootstrap e bucket/ECR esperados.
- [ ] Registrar as ações adicionais exigidas, se houver, com seus eventos
  `AccessDenied`.
- [ ] Remover a policy temporária do principal `moldato`.
- [ ] Remover/deletar a policy temporária, se ela não for mais necessária.
- [ ] Manter somente a execution policy restrita anexada à role de execução
  CloudFormation, após revisão do template sintetizado.
- [ ] Avaliar e conceder em documento/policy separado as permissões de RDS e
  `make up`, antes de qualquer deploy.
