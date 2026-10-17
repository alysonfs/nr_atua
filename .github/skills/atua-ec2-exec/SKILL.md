---
name: atua-ec2-exec
description: Executa comandos na EC2 do ATUA via SSH usando o IP resolvido pelos outputs do CloudFormation. Use quando precisar executar comando na EC2 do ATUA, checar free -m, swap, systemctl ou inspecionar a instância com segurança.
---

# Execução remota na EC2 do ATUA

Use esta skill para rodar um comando pontual na instância EC2 do ATUA sem depender de IP anotado manualmente. O script resolve o IP público a partir dos outputs do `AtuaComputeStack`, usa a chave `~/.ssh/atua-mvp-key.pem`, o usuário `ec2-user` e respeita `AWS_PROFILE`/`AWS_REGION` do ambiente.

## Como usar

1. Receba o comando exato do usuário.
2. Para comandos somente leitura, execute diretamente:

```bash
bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh "<comando-do-usuario>"
```

3. Devolva a saída do comando ao usuário, preservando erros e exit code.

## Comandos que exigem confirmação explícita

Recuse executar imediatamente e peça confirmação explícita se o comando for destrutivo, mutável ou puder afetar disponibilidade/segurança, por exemplo:

- `rm`
- `reboot`
- `shutdown`
- `systemctl stop`
- `systemctl restart`
- `kill`
- `dd`
- `chmod`/`chown` recursivo
- escrita em `/etc`
- `curl | sh`

Na ausência de confirmação explícita, não execute esses comandos.

## Comandos somente leitura que podem rodar direto

- `echo alyson`
- `free -m`
- `swapon --show`
- `systemctl status ... --no-pager`
- `journalctl ... --no-pager`
- `uptime`
- `df -h`

## Exemplos

```bash
bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh "echo alyson"
```

```bash
bash .github/skills/atua-ec2-exec/scripts/ec2-exec.sh "echo '=== free -m ===' && free -m && echo && echo '=== swap ===' && swapon --show && echo && echo '=== systemd services ===' && systemctl status atua-api atua-collector --no-pager | grep -E 'Memory|Tasks|Active'"
```

## Diagnóstico operacional

Se a conexão SSH der timeout, isso normalmente indica que o IP da instância mudou ou que seu IP atual não está liberado na porta 22 do Security Group. A skill não altera regras de rede automaticamente.
