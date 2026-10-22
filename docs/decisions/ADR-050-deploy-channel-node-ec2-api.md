# ADR-050 - Deploy do Channel WhatsApp na EC2 da API, sem EBS dedicado

## Status

**Accepted** — decisão do PO, operacionalizada nesta mudança de infra.

## Contexto

[ADR-035](./ADR-035-topologia-dedicada-always-on-para-baileys.md) propôs
uma instância EC2 dedicada e always-on para o Channel WhatsApp (Node/TS +
Baileys), mas foi **rejeitada pelo PO por custo** — permanece com status
`Proposed`, não implementada.

[ADR-038](./ADR-038-persistencia-sessao-baileys-dev-local-producao-ebs.md)
registrou persistência de sessão em arquivos locais para desenvolvimento, e
cogitou um volume EBS dedicado para produção — também não implementado.

[ADR-049](./ADR-049-pareamento-whatsapp-qr-office.md) já assumiu, como
premissa aceita e implementada do lado da aplicação, que: (1) o Channel
roda na **mesma EC2 da API**; (2) `make down` destrói a EC2 e seu EBS,
apagando a sessão Baileys; (3) o reparo é feito via QR exibido no Office
(escopo `channel.session.report`). ADR-049 explicitamente deixou "o
trabalho de infra/CDK fora do escopo desta implementação" — pendente.

Esta decisão fecha essa pendência: como o Channel é efetivamente publicado
e operado na infraestrutura AWS existente, sem criar nenhum recurso novo
além do necessário para rodar o processo Node ao lado da API.

## Decisão

- **Mesma instância, sem recurso dedicado novo.** O Channel roda como um
  processo systemd (`atua-channel`) na instância `atua-api-master`
  (t3.micro), em ambas as topologias hoje suportadas pela CDK (duas
  instâncias API+Collector, ou colocada API+Collector+Channel). **Nenhuma
  instância EC2, EBS, Elastic IP ou Security Group novo foi criado.**
- **Sem persistência além do disco raiz.** Não é criado nenhum EBS
  adicional para a sessão Baileys. `/var/lib/atua-channel/session` vive no
  disco raiz (`deleteOnTermination: true`, já configurado) e é destruída
  junto com a instância em `make down` — decisão deliberada do PO,
  compatível com o reparo via QR do ADR-049.
- **Restauração automática do runtime via UserData, nunca dos segredos.**
  O `UserData` da instância (gerado por `channelBootstrapCommands` em
  `infra/lib/atua-compute-stack.ts`) instala Node 22, cria o usuário de
  sistema `atua-channel`, os diretórios e a unit systemd, e tenta
  restaurar o último bundle publicado em
  `s3://<releases-bucket>/channel-latest/` — reaproveitando a MESMA
  permissão IAM de leitura (`ReadReleasesBucket`) já concedida à
  `apiRole`. **Nenhuma política IAM nova foi adicionada.** O arquivo de
  credenciais (`/etc/atua-channel.env`, com os tokens de serviço) nunca é
  restaurado automaticamente — isso exigiria acesso a Secrets Manager/SSM
  Parameter Store, permissão fora do escopo autorizado para o Channel.
  Depois de um `make up` "frio", o serviço fica habilitado mas parado até
  que `make deploy-channel` reinjete os tokens manualmente.
- **Swap tornado incondicional.** `swapFileCommands()` (antes só aplicado
  na topologia colocada, por causa do Chromium do Collector) passou a ser
  aplicado em **ambas** as topologias — rede de segurança contra OOM-kill
  agora que a instância da API também hospeda um segundo processo
  long-running (Channel), mesmo com folga de memória medida confortável
  (ver seção Consequências).
- **Layout de deploy fixo em `/opt/atua-channel/app/`.** Por uma
  característica de `apps/channel-node/src/config.ts` (validação de que
  `SessionPath` fique fora de uma raiz calculada 3 níveis acima do
  `dist/`), o bundle **precisa** ser instalado em
  `/opt/atua-channel/app/dist/main.js` (não achatado em
  `/opt/atua-channel/dist/`), para que
  `/var/lib/atua-channel/session` seja validado como genuinamente externo
  ao "repositório" calculado.
- **Ferramental de deploy reaproveita o padrão existente.** Os alvos
  `make deploy-channel` / `make redeploy-channel` / `make logs-channel` /
  `make logs-channel-follow` seguem o mesmo mecanismo de transporte
  (SSH/SCP, resolução de IP via CloudFormation Outputs) já usado por
  `deploy-api`/`redeploy-api`/`logs-api`, na skill
  `.github/skills/atua-deploy`.

## Consequências

- **Custo**: zero custo incremental de infraestrutura — nenhuma instância,
  EBS, IP ou IAM novo. O único custo adicional é o uso de CPU/RAM/rede do
  processo Channel na instância `t3.micro` já paga.
- **Memória**: medição real via SSH (somente leitura, antes desta
  mudança) na instância `atua-api-master`: ~916 MB totais, ~517 MB
  disponíveis, API usando ~124 MB, sem swap. Isso é bem mais folgado que
  a medição do ADR-035 (que era para a topologia colocada **com**
  Chromium do Collector, restando só ~119 MB). Adicionar o Channel
  (Node + Baileys, sem navegador) é esperado consumir algo entre
  ~100-250 MB — dentro da folga medida, mas com margem reduzida para
  picos simultâneos. O swap incondicional mitiga o risco de OOM-kill sem
  custo de infraestrutura (é um arquivo em disco, não um recurso AWS).
- **Disponibilidade da sessão WhatsApp**: a sessão Baileys **não
  sobrevive** a `make down`. Esta é uma escolha deliberada do PO
  (documentada primeiro em ADR-049) — o custo de manter um EBS dedicado
  só para persistir uma sessão de pareamento (reparável em minutos via
  QR) foi considerado desproporcional ao MVP.
- **Operação**: toda recriação de instância (`make down` + `make up`)
  exige rodar `make deploy-channel` manualmente para reinjetar tokens e
  iniciar o serviço — não há automação completa de ponta a ponta por
  decisão deliberada (evitar IAM novo). Isso está documentado em
  `docs/guides/channel-node-deploy-ec2.md`.
- **Escopo de credenciais**: o Channel usa 5 escopos de credencial de
  serviço (`channel.command.claim`, `channel.command.complete`,
  `channel.message.ingest`, `channel.media.ingest`, e o novo
  `channel.session.report` do ADR-049). O script
  `infra/scripts/add-channel-session-report-credential.sh` foi criado
  nesta mudança, mirando o padrão já existente de
  `add-channel-media-ingest-credential.sh` — nenhum seed foi executado por
  esta tarefa.
- **UserData**: o tamanho do `UserData` da instância ficou em ~12,4 KB
  (topologia padrão) / ~13,7 KB (topologia colocada), ambos dentro do
  limite de 16 KiB da AWS, mas a colocada tem menos folga (~2,7 KB) para
  comandos futuros.
- **Superfície de rede**: nenhuma alteração de Security Group — o Channel
  não expõe porta nenhuma (cliente HTTP/WebSocket outbound apenas) e fala
  com a API via loopback (`http://127.0.0.1:80`).
- **Não supersede ADR-035/038/049** — complementa e operacionaliza a
  parte de infraestrutura que ADR-049 deixou explicitamente pendente.
  ADR-035 continua `Proposed` (rejeitada por custo, mas preservada como
  registro histórico de alternativa considerada); ADR-038 continua válido
  para o cenário de desenvolvimento local (sessão em arquivo local), mas
  seu apontamento de "produção com EBS" é substituído, para a topologia
  real escolhida, por "produção sem EBS, sessão efêmera" conforme este
  ADR.

## Alternativas consideradas

- **EBS dedicado para a sessão Baileys** (cogitado em ADR-038): rejeitado
  pelo PO por custo e complexidade operacional (snapshot/anexação entre
  recriações de instância) desproporcionais para um dado reconstruível em
  minutos via QR.
- **Instância EC2 dedicada ao Channel** (ADR-035): rejeitada pelo PO por
  custo (uma `t3.micro` adicional + IP público + EBS, ~US$ 17-18/mês
  estimados).
- **Restaurar `/etc/atua-channel.env` via Secrets Manager/SSM no
  UserData**: rejeitada por exigir uma política IAM nova para a
  `apiRole`, fora do escopo explicitamente autorizado desta tarefa
  ("nenhuma permissão IAM nova é necessária para o Channel"). Mantém-se
  como melhoria futura possível, caso o custo operacional do passo manual
  (`make deploy-channel` pós-`make up`) se torne relevante.

## Referências

- [ADR-035](./ADR-035-topologia-dedicada-always-on-para-baileys.md)
- [ADR-038](./ADR-038-persistencia-sessao-baileys-dev-local-producao-ebs.md)
- [ADR-049](./ADR-049-pareamento-whatsapp-qr-office.md)
- `infra/lib/atua-compute-stack.ts` (`channelBootstrapCommands`,
  `CHANNEL_RELEASES_S3_PREFIX`, `swapFileCommands` incondicional)
- `infra/systemd/atua-channel.service`
- `.github/skills/atua-deploy/scripts/deploy-channel.sh`,
  `redeploy-channel.sh`
- `docs/guides/channel-node-deploy-ec2.md`
