# Domínio atyno.com.br na AWS

**Status:** DNS autoritativo, certificado ACM, CloudFront dos frontends e
CloudFront da API aplicados em `2026-09-10`.

Este documento descreve como publicar:

- Landing: `https://atyno.com.br`
- Office: `https://office.atyno.com.br`
- Manager: `https://manager.atyno.com.br`
- Técnica: `https://tecnica.atyno.com.br`
- API: `https://api.atyno.com.br`

## Decisão de registro e DNS

O registro do domínio `.com.br` permanece no GoDaddy. A AWS Route 53 será
usada como DNS autoritativo por meio de uma **Public Hosted Zone**.

A transferência do registro para o Route 53 Domains não é o procedimento
aplicável para este domínio. Portanto, “passar o domínio para a AWS” significa
delegar os nameservers para a hosted zone da Route 53, sem alterar o
registrador.

## Estado atual conhecido

No ambiente atual:

- o bucket S3 hospeda os frontends por prefixos (`landing/`, `office/`,
  `manager/`, `tecnica/`);
- os frontends agora são servidos por CloudFront com HTTPS nos domínios
  públicos aprovados;
- a hosted zone pública da Route 53 `Z0081694EFE0L9DL5JBO` é autoritativa
  para `atyno.com.br`;
- o certificado ACM `arn:aws:acm:us-east-1:462991286554:certificate/36949ec5-82e5-4d7d-aa1c-bd975831b6df`
  está emitido em `us-east-1`;
- a API pública responde em `https://api.atyno.com.br` via CloudFront;
- a origem técnica `api-origin.atyno.com.br` aponta para a EC2 efêmera atual
  e deve ser atualizada após cada `make up`;
- o budget documentado é de US$ 5/mês;
- os builds dos frontends foram publicados no bucket S3 e validados via
  CloudFront.

## Inventário recebido do GoDaddy

Arquivo recebido: `Portfólio de Domínios atyno.com.br.txt`, exportado em
`2026-09-10 10:58:09`.

Registros encontrados:

| Nome | Tipo | TTL | Valor | Tratamento |
|---|---|---:|---|---|
| `@` | A | 3600 | `WebsiteBuilder Site` | Substituir pelo alias da distribuição CloudFront da landing; o valor exportado é uma referência do Website Builder, não um IPv4 para copiar. |
| `@` | NS | 3600 | `ns03.domaincontrol.com.` | Não copiar; será substituído pelos nameservers fornecidos pela Route 53 no registrador. |
| `@` | NS | 3600 | `ns04.domaincontrol.com.` | Não copiar; será substituído pelos nameservers fornecidos pela Route 53 no registrador. |
| `_dmarc` | TXT | 3600 | `v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;` | Preservar na Route 53 antes da troca de nameservers. |
| `www` | CNAME | 3600 | `@` | Recriar como alias para a landing ou redirecionar conforme a configuração final; não deixar apontando para o Website Builder. |
| `_domainconnect` | CNAME | 3600 | `_domainconnect.gd.domaincontrol.com.` | Manter somente se algum fluxo de integração do GoDaddy ainda depender dele; confirmar antes da migração. |

O arquivo não contém registros `MX`, `SPF`, `DKIM`, `CAA`, `AAAA` ou outros
subdomínios. Isso reduz o risco de perda de e-mail durante a migração, mas não
substitui a confirmação com o usuário de que o domínio não é usado para
recebimento/envio de e-mail fora do arquivo exportado.

O `SOA` e os registros `NS` exportados são autoritativos do GoDaddy. Eles não
devem ser importados manualmente na hosted zone Route 53: a própria Route 53
criará SOA e quatro NS novos.

## Implementação no repositório

A stack `AtuaDomainStack` foi adicionada ao CDK. Ela define a hosted zone,
certificado ACM em `us-east-1`, distribuições CloudFront para os frontends,
distribuição CloudFront para a API e aliases A/AAAA para a raiz e os
subdomínios públicos. A origem de cada distribuição frontend usa o prefixo
legado correspondente no bucket S3: `landing`, `office`, `manager` e
`tecnica`.

O deploy é separado do ciclo diário da infraestrutura e não é executado por
`make up`:

```bash
cd infra
AWS_PROFILE=moldato make deploy-domain
```

Antes de executar, faça o inventário DNS descrito abaixo. O comando cria
recursos reais e pode gerar cobrança.

## Arquitetura necessária

### Frontends

Usar CloudFront com origem S3 privada via Origin Access Control (OAC).
O website endpoint público atual não deve ser usado para HTTPS com domínio
customizado.

Os frontends usam distribuições separadas nesta primeira implementação para
que cada hostname tenha uma origem S3 explícita e não dependa de roteamento
por `Host` dentro de uma distribuição compartilhada.

O certificado ACM usado pelo CloudFront foi criado em `us-east-1` e
conter:

- `atyno.com.br`
- `www.atyno.com.br`
- `office.atyno.com.br`
- `manager.atyno.com.br`
- `tecnica.atyno.com.br`
- `api.atyno.com.br`

### API

`api.atyno.com.br` é o endpoint público HTTPS da API. Ele aponta para uma
distribuição CloudFront com cache desabilitado e métodos HTTP completos.

Para evitar Elastic IP e ALB no MVP, a origem da distribuição é o hostname
técnico `api-origin.atyno.com.br`, que aponta para o IP público atual da EC2.
Esse registro não deve ser usado por frontends nem divulgado como endpoint
público; ele existe apenas para evitar loop entre `api.atyno.com.br` e a
própria distribuição CloudFront.

Após cada `make up`, o alvo `make sync-api-domain` atualiza
`api-origin.atyno.com.br` com o novo IP público da API. O alvo `make up`
também executa essa sincronização ao final do deploy da `AtuaComputeStack`.

A implementação atual já prepara o CORS da API para:

- `https://atyno.com.br`
- `https://office.atyno.com.br`
- `https://manager.atyno.com.br`
- `https://tecnica.atyno.com.br`

## Procedimento no GoDaddy

Não altere os nameservers antes de concluir o inventário abaixo.

1. Exportar ou registrar todos os registros existentes no GoDaddy:
   `A`, `AAAA`, `CNAME`, `MX`, `TXT`, `CAA` e subdomínios.
2. Comparar o inventário recebido com a tela atual do GoDaddy e confirmar que
   não há registros de e-mail, especialmente MX, SPF e DKIM, ausentes no
   arquivo. O registro DMARC deve ser preservado.
3. Criar a hosted zone pública `atyno.com.br` na Route 53.
4. Recriar na Route 53 o DMARC e, se necessário, `_domainconnect`. Não recriar
   o A `WebsiteBuilder Site`; ele será substituído pelo alias CloudFront.
   Recriar `www` como alias da landing.
5. Solicitar o certificado ACM e criar os registros de validação DNS.
6. Confirmar que os registros de validação foram publicados.
7. Copiar os quatro nameservers fornecidos pela Route 53.
8. No GoDaddy, substituir os nameservers atuais pelos quatro nameservers da
   hosted zone.
9. Aguardar a propagação e validar com `dig`/`nslookup`.
10. Somente depois validar HTTPS, frontends e API.

Não remova registros antigos do GoDaddy até confirmar que a delegação está
ativa e que e-mail e demais serviços continuam funcionando.

## Checklist de aceite

- [x] Hosted zone pública criada na conta AWS correta e região adequada.
- [x] Nameservers da Route 53 copiados sem erro.
- [x] Export do GoDaddy recebido e inventariado.
- [ ] Tela atual do GoDaddy conferida contra o export.
- [x] MX, SPF, DKIM, DMARC e demais integrações foram preservados ou
      confirmados como não utilizados.
- [x] O A `WebsiteBuilder Site` foi identificado como legado e não deve ser
      copiado para a Route 53.
- [x] Certificado ACM emitido e validado em `us-east-1`.
- [x] S3 não está público quando CloudFront/OAC estiver ativo.
- [x] `atyno.com.br` abre a landing por HTTPS.
- [x] `www.atyno.com.br` abre a landing por HTTPS.
- [x] `office.atyno.com.br` abre o Office por HTTPS.
- [x] `manager.atyno.com.br` abre o Manager por HTTPS.
- [x] `tecnica.atyno.com.br` abre a Técnica por HTTPS.
- [x] `api.atyno.com.br/health` responde por HTTPS.
- [ ] Login e chamadas de API funcionam nos quatro frontends.
- [x] CORS não permite origens arbitrárias em produção.
- [x] O ciclo `make up` atualiza corretamente qualquer DNS de origem
  efêmera, se essa estratégia for escolhida.
- [ ] Custos foram revisados e o budget foi atualizado ou explicitamente
  aceito.

## Rollback

Se a delegação causar indisponibilidade:

1. restaurar no GoDaddy os nameservers anteriores;
2. manter a hosted zone Route 53 intacta para investigação;
3. verificar primeiro MX/TXT e depois os registros web;
4. não apagar o certificado, a distribuição ou a hosted zone durante o
   incidente;
5. repetir a migração somente após corrigir o inventário ou a configuração.

## Pendências atuais

Esta documentação não executa alterações externas. Ainda são necessários:

- aprovação do impacto de custo do CloudFront e Route 53.
