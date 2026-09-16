# RF-019 - Upload de marca do cliente

Status: `Planejado` (não implementado — placeholder/fake em uso no Dashboard
mínimo até esta funcionalidade ser priorizada)

## Objetivo

Permitir que o cliente (tenant) faça upload de sua própria marca (logo) para
ser exibida no Office, reforçando que o ATUA é uma plataforma operacional a
serviço da operação do cliente, não uma ferramenta de marca própria isolada.

## Escopo

Upload, armazenamento, substituição e exibição da marca do cliente no header
do Office (Dashboard e demais telas autenticadas). Não inclui edição de
imagem (crop, redimensionamento avançado), múltiplas marcas por tenant, nem
biblioteca de assets de marca (banners, ícones customizados além do logo
principal) — fora do escopo deste requisito.

## Requisitos funcionais

### RF-019.1 - Upload de arquivo de imagem

O usuário (Owner do tenant) deve poder enviar um arquivo de imagem (PNG,
JPG ou SVG) como marca do seu tenant, a partir de uma tela de configurações
(a definir local exato — provavelmente dentro da área de "Settings" já
demarcada no Dashboard mínimo).

### RF-019.2 - Validação de formato e tamanho

O sistema deve validar o arquivo enviado quanto a:
- Formato aceito (PNG, JPG, SVG).
- Tamanho máximo de arquivo (limite exato a definir por
  `software-architect`/`aws-architect` considerando custo de armazenamento).
- Dimensões mínimas/proporção recomendada (a definir, para evitar marcas
  ilegíveis quando redimensionadas no header).

### RF-019.3 - Substituição da marca

O upload de uma nova imagem deve substituir a marca anterior do tenant. Não é
necessário versionamento/histórico de marcas neste requisito.

### RF-019.4 - Exibição no Office

Uma vez enviada, a marca do cliente deve ser exibida no header do Office
(Dashboard e demais telas autenticadas), em posição de destaque, ao lado da
marca do ATUA (que permanece sempre visível, reforçando a parceria
plataforma+cliente).

### RF-019.5 - Estado padrão (sem marca enviada)

Enquanto o tenant não tiver enviado uma marca própria, o Office deve exibir
um placeholder neutro (ex.: iniciais do nome do tenant, ou ícone genérico) —
nunca deve quebrar o layout nem exibir espaço vazio sem indicação.

### RF-019.6 - Remoção da marca

O usuário deve poder remover a marca enviada, retornando ao estado padrão
(RF-019.5).

## Regras de negócio

- RN-019.1: Apenas o Owner do tenant deve poder alterar a marca (mesmo nível
  de permissão de outras configurações sensíveis do tenant — a confirmar com
  `software-architect` se há papéis além de Owner no MVP).
- RN-019.2: A marca é uma propriedade do tenant, não do usuário individual —
  todos os membros do mesmo tenant devem ver a mesma marca.
- RN-019.3: Nenhuma marca deve ser aceita sem passar pela validação de
  RF-019.2 (não confiar apenas em validação client-side).

## Casos de borda

- Upload de arquivo corrompido ou com extensão incompatível com o conteúdo
  real → deve ser rejeitado com mensagem clara, sem quebrar a experiência.
- Upload de arquivo muito grande → deve ser rejeitado antes de consumir
  armazenamento, com mensagem informando o limite.
- Tenant sem marca enviada acessando o Dashboard pela primeira vez → deve ver
  o placeholder (RF-019.5), nunca um erro.
- Falha de rede durante o upload → o sistema deve informar a falha e manter
  a marca anterior (se houver) inalterada.

## Decisões pendentes (não bloqueiam a documentação deste requisito, mas
bloqueiam a implementação)

1. **Estratégia de armazenamento**: onde o arquivo será persistido (ex.: S3
   dedicado, mesmo bucket dos assets do frontend, outro serviço) — decisão de
   `software-architect`/`aws-architect`.
2. **Limites exatos de tamanho/dimensão** — a definir junto ao time de
   design/marketing para não comprometer a qualidade visual do header.
3. **Papéis autorizados a alterar a marca**, caso o modelo de permissões
   evolua além de Owner único (ver RF-005 / ADR-005).

## Critérios de aceite

1. Dado um Owner autenticado, quando enviar um arquivo de imagem válido
   (PNG/JPG/SVG dentro do limite de tamanho), então o sistema deve aceitar,
   armazenar e exibir a nova marca no header do Office.
2. Dado um Owner autenticado, quando enviar um arquivo inválido (formato não
   suportado ou acima do limite de tamanho), então o sistema deve rejeitar
   com mensagem clara, sem alterar a marca atual.
3. Dado um tenant sem marca enviada, quando qualquer membro acessar o
   Office, então deve ser exibido o placeholder padrão (RF-019.5), nunca um
   espaço vazio ou erro.
4. Dado um tenant com marca já enviada, quando um novo upload for realizado
   com sucesso, então a marca anterior deve ser substituída em todas as
   telas do Office para todos os membros do tenant.
5. Dado um Owner autenticado, quando solicitar a remoção da marca, então o
   sistema deve remover a marca enviada e voltar ao estado padrão (RF-019.5).
6. Dado um usuário que não seja Owner do tenant, quando tentar alterar a
   marca, então o sistema deve negar a operação (RN-019.1).
7. Dado qualquer upload, quando processado, então a validação de formato e
   tamanho (RF-019.2) deve ocorrer no servidor, independentemente de
   validação client-side.

## Dependências

- RF-005 (Acesso ao Office) — define quem é Owner do tenant.
- Decisão de armazenamento (`software-architect`/`aws-architect`) — ver
  "Decisões pendentes" acima.
- Dashboard mínimo (marcação de área da marca do cliente no header) — ver
  entrega desta mesma etapa, que usa um placeholder fake até este requisito
  ser implementado.

## Impactos

- Enquanto não implementado, o header do Office exibirá um placeholder fake
  fixo no lugar da marca do cliente (ver Dashboard mínimo). Nenhuma
  funcionalidade de upload real deve ser anunciada ao cliente antes deste
  requisito ser implementado.

## Fora do escopo

- Edição avançada de imagem (crop, filtros, redimensionamento manual).
- Múltiplas marcas por tenant (ex.: marca clara/escura separadas, variações
  por unidade/filial).
- Biblioteca de outros assets de marca do cliente (banners, favicons
  customizados) além do logo principal do header.
- Aprovação/moderação de conteúdo da marca enviada (assume-se boa-fé do
  Owner, mesma postura adotada para demais conteúdos gerados pelo cliente).
