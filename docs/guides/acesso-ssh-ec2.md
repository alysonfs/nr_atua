# Acesso SSH à EC2 do ATUA

## Objetivo

Este guia mostra como descobrir o IP atual da EC2 do ATUA, liberar acesso SSH com segurança, conectar manualmente, executar comandos únicos e usar o script/skill `atua-ec2-exec`.

## Pré-requisitos

- AWS CLI v2 instalada e autenticada com um profile válido (`AWS_PROFILE`, por exemplo `moldato`).
- Permissão para consultar CloudFormation/EC2/SSM.
- Chave privada local recuperada com:

```bash
cd infra
make retrieve-keypair
```

- Permissão correta na chave:

```bash
chmod 400 ~/.ssh/atua-mvp-key.pem
```

- Saber que a instância é de produção: evite comandos mutáveis sem necessidade.

## Descobrir o IP atual da instância

O IP público muda quando a instância é recriada. Não dependa de IP salvo manualmente.

### Opção 1: CloudFormation Outputs

```bash
AWS_PROFILE=moldato aws cloudformation describe-stacks \
  --stack-name AtuaComputeStack \
  --region sa-east-1 \
  --query "Stacks[0].Outputs[?OutputKey=='ApiPublicIp'].OutputValue | [0]" \
  --output text
```

### Opção 2: `make status`

```bash
cd infra
AWS_PROFILE=moldato make status
```

Use o output do `AtuaComputeStack` para confirmar que o compute stack está ativo antes de tentar o SSH.

## Autorizar seu IP na porta 22

Antes de conectar, libere temporariamente seu IP público atual no Security Group da instância para a porta 22.

Passos sugeridos:

1. Descubra seu IP público atual.
2. No Security Group da EC2 do ATUA, adicione uma regra de entrada SSH (`TCP 22`) restrita ao seu IP (`x.x.x.x/32`).
3. Após terminar, remova a regra para reduzir superfície de ataque.

> Importante: o script/skill **não** altera Security Group automaticamente.

## Conectar manualmente por SSH

```bash
ssh -i ~/.ssh/atua-mvp-key.pem ec2-user@IP
```

Se quiser evitar prompt interativo de host key na primeira conexão:

```bash
ssh -i ~/.ssh/atua-mvp-key.pem -o StrictHostKeyChecking=accept-new ec2-user@IP
```

## Executar um comando único

Sem abrir shell interativo:

```bash
ssh -i ~/.ssh/atua-mvp-key.pem ec2-user@IP "echo alyson"
```

## Diagnóstico de memória e serviços

Comando recomendado:

```bash
echo '=== free -m ===' && free -m && echo && echo '=== swap ===' && swapon --show && echo && echo '=== systemd services ===' && systemctl status atua-api atua-collector --no-pager | grep -E 'Memory|Tasks|Active'
```

### O que cada parte significa

- `free -m`: mostra memória RAM em MB.
  - Observe principalmente `available`.
  - Sinal de alerta: `available` persistentemente baixo e o sistema sob pressão.
- `swapon --show`: mostra swap configurada e em uso.
  - Sinal de alerta: uso crescente e recorrente de swap, especialmente junto com lentidão.
- `systemctl status ... | grep -E 'Memory|Tasks|Active'`: resume estado dos serviços.
  - `Active`: confirma se o serviço está ativo.
  - `Tasks`: ajuda a perceber crescimento anormal de processos/threads.
  - `Memory`: mostra consumo atual de memória pelo serviço.

### Sinais de alerta

- swap em uso crescente ao longo do tempo;
- `available` muito baixo por períodos prolongados;
- reinícios inesperados dos serviços;
- mensagens de `OOM`/`Out of Memory` no sistema ou nos logs.

## Usar o script da skill

Script local:

```bash
bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh "echo alyson"
```

Diagnóstico completo:

```bash
bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh "echo '=== free -m ===' && free -m && echo && echo '=== swap ===' && swapon --show && echo && echo '=== systemd services ===' && systemctl status atua-api atua-collector --no-pager | grep -E 'Memory|Tasks|Active'"
```

## Usar pelo Makefile

```bash
cd infra
AWS_PROFILE=moldato make ec2-exec CMD="echo alyson"
```

## Usar pela skill

A skill `atua-ec2-exec` existe para agentes executarem comandos pontuais de inspeção remota sem depender de IP manual. Ela deve:

- resolver o IP via CloudFormation Outputs do `AtuaComputeStack`;
- usar `~/.ssh/atua-mvp-key.pem` e usuário `ec2-user`;
- executar diretamente comandos somente leitura;
- exigir confirmação explícita antes de comandos destrutivos ou mutáveis.

## Troubleshooting

### Timeout de SSH

Se houver timeout:

- confirme que o `AtuaComputeStack` está no ar;
- confirme que o IP público atual mudou e você está usando o output mais recente;
- confira se seu IP público atual está autorizado no Security Group na porta 22.

### `Permission denied`

Verifique:

- chave correta em `~/.ssh/atua-mvp-key.pem`;
- permissão `chmod 400 ~/.ssh/atua-mvp-key.pem`;
- usuário `ec2-user`;
- regra de SSH liberada para seu IP.

### `WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!`

Isso pode acontecer após recriar a instância. Remova a chave antiga do `known_hosts` e conecte novamente:

```bash
ssh-keygen -R IP
```

Depois repita o SSH com o IP novo.

## Avisos de segurança

- A instância é de produção; prefira comandos somente leitura.
- Não cole segredos, tokens ou credenciais em comandos ou históricos.
- Evite comandos mutáveis sem janela operacional e confirmação explícita.
- Revogue a regra temporária da porta 22 ao finalizar.
