# Utilidades de desenvolvimento local

## Liberar porta ocupada por app local

Quando um app Vite falhar com erro semelhante a:

```text
Error: Port 5175 is already in use
```

verifique qual processo esta escutando na porta:

```bash
lsof -nP -iTCP:5175 -sTCP:LISTEN
```

Encerre o processo pelo PID exibido:

```bash
kill -TERM <PID>
```

Se o processo nao encerrar:

```bash
kill -KILL <PID>
```

Atalho para liberar diretamente a porta `5175`:

```bash
lsof -tiTCP:5175 -sTCP:LISTEN | xargs kill -TERM
```

Se ainda ficar preso:

```bash
lsof -tiTCP:5175 -sTCP:LISTEN | xargs kill -KILL
```

Depois suba o app novamente:

```bash
pnpm run dev
```

## Liberar portas comuns dos frontends

Portas usadas com frequencia pelos apps Vite do monorepo:

| Porta | Uso comum |
|---|---|
| 5173 | Vite default |
| 5174 | App frontend local |
| 5175 | App frontend local |
| 5176 | App frontend local |
| 5177 | App frontend local |

No macOS, para encerrar todos os listeners nessas portas:

```bash
for port in 5173 5174 5175 5176 5177; do
  pids=$(lsof -tiTCP:$port -sTCP:LISTEN)
  [ -n "$pids" ] && kill -TERM $pids
done
```

Se algum processo persistir, rode a mesma varredura com `kill -KILL`:

```bash
for port in 5173 5174 5175 5176 5177; do
  pids=$(lsof -tiTCP:$port -sTCP:LISTEN)
  [ -n "$pids" ] && kill -KILL $pids
done
```

Use `kill -KILL` apenas quando `kill -TERM` nao resolver, pois ele encerra o
processo imediatamente sem permitir finalizacao limpa.
