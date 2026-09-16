# Lambda Viability Analysis - ASP.NET Core Monolith

**Data**: 2026-08-29, 15:42 UTC-3  
**Status**: Technical Analysis (não aprovado para produção)  

---

## Pergunta 1: ASP.NET Core Monolito em Lambda?

### Resposta Curta: ❌ NÃO RECOMENDADO para MVP

**Por quê?**
- Possível, mas complexo
- Não é a stack nativa de Lambda
- Pode criar problemas de latência/timeout
- Alternativa mais simples: EC2 t3.micro

---

## Análise Detalhada

### 1. Opção A: Lambda + ASP.NET Core Monolito (Container Image)

#### Setup Técnico

```
Architecture:
  API Gateway
    ↓ (routes all requests)
  Lambda function (container image)
    ↓
  ASP.NET Core 7 monolito
    ↓
  RDS + DocumentDB
```

#### Viability Scorecard

| Aspecto | Status | Detalhes |
|---------|--------|----------|
| **Framework Support** | ⚠️ Indirect | Sem suporte nativo; precisa container |
| **Startup Time** | ❌ Problematic | ASP.NET cold start 2-3s + Lambda 3-5s = 5-8s total |
| **Memory Usage** | ❌ High | ASP.NET mínimo ~200MB; Lambda min 128MB (use 512MB+) |
| **Timeout** | ✅ OK | 15 minutos (λ timeout) > typical request |
| **Package Size** | ❌ Large | ASP.NET + dependencies ~150-300MB > 50MB zip limit |
| **Solution** | ✅ Container | ECR image (até 10GB) solves package size |
| **State Management** | ❌ Complex | ASP.NET session = in-memory; Lambda ephemeral |
| **Connection Pooling** | ❌ Wasteful | Novo pool per invocation; não reutiliza |
| **Cost** | ✅ OK | 1M API calls/mês Free Tier |

#### Problemas Específicos

**Problema 1: Cold Start Latency**

```
ASP.NET Core startup:
  - Load runtime: ~1s
  - Compile reflection: ~1s  
  - DI container boot: ~0.5s
  Total: ~2.5s

Lambda container cold start:
  - Download image from ECR: ~1-2s
  - Decompress layers: ~1s
  - Execute entrypoint: ~0.5s
  Total: ~2.5-3.5s

Combined cold start: ~5-6s

Impact:
  ❌ First request after deployment: 5-6s latency (user perception: slow)
  ✅ Subsequent requests (warm): 100-200ms (acceptable)

Solution:
  1. Provisioned Concurrency: Keep lambda "warm" (EXPENSIVE: $0.015/hour per concurrent)
  2. Accept cold start in MVP (better than premium cost)
  3. API Gateway caching (reduce requests)
  4. Client-side retry + backoff (graceful degradation)
```

**Problema 2: State Management**

```
ASP.NET architecture often assumes:
  - Session storage (in-memory by default)
  - Cache (in-memory: MemoryCache)
  - Singleton services (application-wide)

Lambda execution model:
  - Ephemeral container (alive only during request)
  - No persistent memory between invocations
  - New container per request (cold start)

Impact:
  ❌ Session data lost between requests
  ❌ In-memory cache invalidated
  ❌ Singleton services recreated

Solution:
  1. Refactor to stateless design
     - Use distributed cache (Redis/ElastiCache)
     - Store session in RDS/DynamoDB
     - Implement Repository pattern (stateless)
  2. Cost: Redis/ElastiCache adds $30-50/mês
  3. Complexity: Significant refactoring of ASP.NET Core app

Result:
  ❌ Not viable without significant refactoring
```

**Problema 3: Connection Pooling Waste**

```
RDS Connection Pool behavior:

Traditional (EC2):
  - Connection pool: Keep 10 connections warm
  - Reuse for multiple requests
  - Cost: 1 connection = ~5MB memory

Lambda:
  - New pool per cold start (new process)
  - Pool recreated every ~5-15 minutes (λ recycle)
  - Multiple concurrent λ = multiple pools
  - 100 concurrent requests = 100 pools = 500MB

Solution:
  1. RDS Proxy: Connection multiplexing
     - Lambda → RDS Proxy → RDS (share connections)
     - Cost: $0.015/hour = ~$10.95/mês
     - Better: Reduces RDS connection limit issues
  2. Provisioned concurrency (warm lambda)
     - Keep container alive between requests
     - Cost: $0.015/hour per concurrent
     - 5 concurrent = $0.075/hour = $54.75/mês
```

#### Implementação Técnica (se quisesse tentar)

```dockerfile
# Dockerfile for Lambda ASP.NET Core
FROM public.ecr.aws/lambda/dotnet:7

COPY bin/Release/net7.0/linux-x64/published/ ${LAMBDA_TASK_ROOT}/
COPY bin/Release/net7.0/linux-x64/published/bootstrap ${LAMBDA_TASK_ROOT}/bootstrap
RUN chmod +x ${LAMBDA_TASK_ROOT}/bootstrap

CMD ["Atua.Api.Startup::FunctionHandler"]
```

```csharp
// FunctionHandler for Lambda
public class Startup
{
    [LambdaSerializer(typeof(SystemTextJsonSerializer))]
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request, 
        ILambdaContext context)
    {
        var app = BuildApp(); // ❌ COLD START EVERY INVOCATION
        var result = await app.HandleRequest(request);
        return new APIGatewayProxyResponse
        {
            StatusCode = 200,
            Body = JsonSerializer.Serialize(result)
        };
    }
}
```

**Problemas com acima**:
- ❌ BuildApp() é chamado TODA invocation (muito lento)
- ❌ Não reutiliza Kestrel server
- ✅ Solução: Usar framework wrapper (AWS.Lambda.AspNetCoreServer)

#### Resultado: Viability Score

```
Funciona? ✅ Sim, tecnicamente possível
Prático?  ❌ Não, complexo + latência
Custo?    ❌ Não, Provisioned Concurrency = $$$
MVP fit?  ❌ Não, outras opções mais simples
```

---

### 2. Opção B: API Gateway + Lambda (Endpoint-Specific)

#### Arquitetura

```
API Gateway (routes)
  ├─ GET /users     → Lambda (GetUsersHandler)
  ├─ POST /users    → Lambda (CreateUserHandler)
  ├─ GET /orders    → Lambda (GetOrdersHandler)
  └─ ...            → Lambda (specialized)

Libraries:
  ├─ Shared.Domain (models, business logic)
  ├─ Shared.Data (repositories, EF)
  └─ Handlers (endpoint-specific)
```

#### Viability

| Aspecto | Score | Notas |
|---------|-------|-------|
| **Simplicity** | ✅ High | 1 Lambda per endpoint = clear responsibility |
| **Performance** | ✅ Better | Smaller package per Lambda = faster cold start |
| **Cost** | ✅ Good | Pay per invocation (Free Tier 1M) |
| **Refactoring** | ❌ High | Need to split monolito → multiple lambdas |
| **State** | ✅ OK | Less in-memory state per handler |
| **Testing** | ✅ Good | Unit test handlers in isolation |

#### Trade-offs

```
Vantagem:
- ✅ Arquitetura cloud-native
- ✅ Menor package (~30MB per lambda)
- ✅ Escalabilidade per-endpoint
- ✅ Cost effective

Desvantagem:
- ❌ Quebra monolito
- ❌ Requer refactoring significativa
- ❌ Não é MVP-ready (requer 2-3 semanas)
- ❌ Duplicação de código entre handlers
- ❌ Gerenciamento de dependências compartilhadas
```

#### MVP Viability: ❌ NÃO RECOMENDADO (tempo)

---

### 3. Opção C: EC2 t3.micro + ASP.NET Core Monolito ✅ RECOMENDADO

#### Setup

```
EC2 t3.micro (Free Tier, after 750h/mês)
  ├─ OS: Ubuntu 22.04
  ├─ Runtime: .NET 7 SDK
  ├─ App: ASP.NET Core Kestrel
  ├─ Process Manager: systemd
  └─ Reverse Proxy: Nginx (optional)

Deployment: GitHub Actions → EC2 (SSH)
```

#### Viability Scorecard

| Aspecto | Score | Notas |
|---------|-------|-------|
| **Simplicity** | ✅ Excellent | Deploy monolito as-is, no refactoring |
| **Performance** | ✅ Good | Immediate startup, no cold start |
| **Cost** | ✅ Free! | db.t3.micro = 750h/mês free |
| **Scalability** | ⚠️ Manual | Requires manual scaling later |
| **Availability** | ⚠️ Single-AZ | No automatic failover |
| **Deployment** | ✅ Simple | systemd service + GitHub Actions |
| **MVP Fitness** | ✅✅ Perfect | Ready to go in hours |

#### Setup Example

```bash
# EC2 t3.micro Ubuntu setup
sudo apt update && sudo apt install -y dotnet-sdk-7.0

# Clone repo + build
git clone <repo>
cd atua/src/Atua.Api
dotnet build -c Release

# Systemd service
sudo tee /etc/systemd/system/atua-api.service > /dev/null <<EOF
[Unit]
Description=ATUA Master API
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/atua/src/Atua.Api
ExecStart=/usr/bin/dotnet run --configuration Release
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl enable atua-api
sudo systemctl start atua-api
```

#### Monitoring & Logs

```bash
# Check status
sudo systemctl status atua-api

# View logs (last 100 lines)
sudo journalctl -u atua-api -n 100

# Real-time logs
sudo journalctl -u atua-api -f
```

#### Upgrade Path

```
Phase 1: EC2 t3.micro (MVP, Mês 1-12)
  Cost: $0 (Free Tier)
  ↓
Phase 2: EC2 t3.small (if CPU > 60%, Mês 3+)
  Cost: $45/mês
  ↓
Phase 3: Application Load Balancer + ASG (Mês 12+)
  Cost: ~$100-150/mês
  ↓
Phase 4: ECS Fargate (if container needed)
  Cost: ~$150-300/mês
```

#### Resultado: Viability Score

```
Funciona?  ✅ Sim, garantido
Prático?   ✅ Sim, muito simples
Custo?     ✅ Sim, Free Tier
MVP fit?   ✅✅ Perfeito, pronto em 1h
```

---

## Pergunta 2: Collector em Lambda + SQS?

### Resposta Curta: ✅ SIM, VIÁVEL (mas EC2 pode ser simpler)

---

## Análise Detalhada

### 1. Opção A: Lambda + SQS + EventBridge Scheduler ✅

#### Arquitetura

```
EventBridge Scheduler (cron: "*/15 * * * ? *")
  ↓ [trigger every 15 min]
SQS Queue
  ├─ Message 1: {tenant_id: "abc123", timestamp: "2026-08-29T10:00Z"}
  ├─ Message 2: {tenant_id: "def456", timestamp: "2026-08-29T10:00Z"}
  └─ Message N: {...}
  ↓ [Lambda triggers on message]
Lambda Function (Collector)
  ├─ Read message (tenant_id)
  ├─ Call iService API (GET /os?tenant=abc123)
  ├─ Process response
  ├─ Store in RDS + DocumentDB
  ├─ Delete message from queue
  └─ [Done]

Retry logic:
  ├─ Message visibility timeout: 300s (5 min)
  ├─ Max attempts: 3 (before DLQ)
  └─ Dead-Letter Queue for failures
```

#### Pricing

```
EventBridge Scheduler:
  Free Tier: 100 invocations/month
  After: $0.00279 per invocation
  MVP (15-min intervals, 10 tenants):
    10 tenants × 4/hour × 24h × 30d = 28,800 invocations
    = $80.47/month ❌ EXPENSIVE!

SQS:
  Free Tier: 1M requests/month
  After: $0.40 per million
  MVP: 28,800 messages/month = FREE ✅

Lambda:
  Free Tier: 1M invocations + 400k GB-seconds/month
  After: $0.20 per million + $0.0000166667 per GB-second
  MVP: 28,800 invocations × 10s × 0.128GB = 36,864 GB-seconds
  = WITHIN FREE TIER ✅

Total Lambda+SQS cost: $80.47/month (EventBridge is the issue)
```

#### Alternative: Cron with EC2 (cheaper)

```
EC2 t2.micro runs cron job:
  */15 * * * * /opt/collector/run.sh

Script:
  - Poll SQS queue (or read direct list of tenants)
  - Process each tenant
  - No EventBridge needed

Cost: $0 (Free Tier)

Trade-off:
  - No managed scheduling (cron responsibility)
  - EC2 must stay on (vs Lambda can be idle)
```

#### Viability Assessment

```
Performance:
  Cold start: 5-10s per invocation
  Processing: 10-20s per tenant
  Total: 15-30s per invocation
  ✅ Acceptable (SLA doesn't specify iService access time)

Scalability:
  ✅ Auto-scales (Lambda parallelism)
  ✅ Handled by AWS (no manual config)

Cost:
  ❌ $80/month (EventBridge overhead)
  ✅ Within Free Tier if skip EventBridge

Complexity:
  ⚠️ Moderate (SQS + Lambda + DLQ + retry logic)
```

#### Recommendation

```
Use Lambda + SQS IF:
  ✅ You want fully serverless, pay-per-execution
  ✅ Scaling requirement clear
  ✅ OK with $80/month EventBridge cost
  ❌ DON'T use EventBridge Scheduler (expensive)
  ✅ Use EC2 cron instead to trigger SQS

Use EC2 t2.micro IF:
  ✅ Prefer simplicity (1 cron job, always on)
  ✅ Want to minimize cost ($0 Free Tier)
  ✅ Don't need sophisticated scaling yet
```

---

### 2. Opção B: EC2 t2.micro + Cron ✅ RECOMENDADO

#### Setup

```
EC2 t2.micro Ubuntu
  ├─ Collector service (.NET Worker Service)
  ├─ Systemd daemon (always running)
  ├─ Cron job runs every 15 minutes
  └─ Logs to CloudWatch or local file

Alternative: Timer-based (no cron):
  ├─ .NET Timer in application
  ├─ Runs every 15 minutes
  ├─ Simpler than cron
```

#### .NET Worker Service Implementation

```csharp
public class Program
{
    public static void Main(string[] args) =>
        CreateHostBuilder(args).Build().Run();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<CollectorService>();
                services.AddScoped<IServiceClient>();
                services.AddScoped<IOrderRepository>();
            });
}

public class CollectorService : BackgroundService
{
    private readonly ILogger<CollectorService> _logger;
    private readonly IServiceClient _serviceClient;
    private readonly IOrderRepository _repository;
    private Timer _timer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _timer = new Timer(
            callback: async _ => await CollectAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(15),
            period: TimeSpan.FromMinutes(15)
        );

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task CollectAsync()
    {
        _logger.LogInformation("Collecting OS from iService...");
        
        var tenants = await _repository.GetAllTenantsAsync();
        
        foreach (var tenant in tenants)
        {
            try
            {
                var orders = await _serviceClient.GetOrdersAsync(tenant.Id);
                await _repository.UpdateOrdersAsync(tenant.Id, orders);
                _logger.LogInformation("✅ Processed tenant {TenantId}", tenant.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing tenant {TenantId}", tenant.Id);
                // Retry logic, dead-letter handling
            }
        }
    }
}
```

#### Systemd Service

```ini
[Unit]
Description=ATUA Collector Service
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/atua/src/Atua.Collector
ExecStart=/usr/bin/dotnet Atua.Collector.dll
Restart=always
RestartSec=10

Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DATABASE_URL=..."
Environment="MONGODB_URI=..."

[Install]
WantedBy=multi-user.target
```

#### Pricing

```
Compute: EC2 t2.micro Free Tier
  Cost: $0/month (for 12 months)

Networking:
  Data egress (to iService): ~1-5 GB/month
  = $0.45-2.25 (very cheap)

Total: $0/month ✅
```

#### Viability

```
Simplicity:      ✅✅ Excellent
Cost:            ✅✅ FREE (Free Tier)
Scalability:     ⚠️ Manual (clone EC2 later)
Availability:    ⚠️ Single (not HA)
MVP Fitness:     ✅✅ Perfect
Time to Deploy:  ✅✅ 2-3 hours
```

---

## Recomendação Final

### Master API (ASP.NET Monolito)

```
Option A (Lambda):          ❌ Not recommended
  - Cold start problems
  - State management complexity
  - Refactoring required
  - Provisioned concurrency = expensive

✅ Option C (EC2 t3.micro):  RECOMMENDED
  - Deploy as-is, no changes
  - Free Tier for 12 months
  - Simple monitoring + logging
  - Clear upgrade path
  - Ready in 1-2 hours
```

### Collector

```
Option A (Lambda + SQS):    ⚠️ Possible but expensive
  - $80/month (EventBridge)
  - Moderate complexity
  - Good for future scaling

✅ Option B (EC2 t2.micro):  RECOMMENDED
  - Free Tier for 12 months
  - Simple to implement (.NET Worker)
  - Clear upgrade path
  - Ready in 2-3 hours
```

---

## Sumário: Both on EC2

```
┌─────────────────────────────────────────┐
│  RECOMMENDED: EC2-BASED MVP             │
├─────────────────────────────────────────┤
│  Compute:                               │
│  ├─ Master API: EC2 t3.micro (Free)    │
│  └─ Collector: EC2 t2.micro (Free)     │
│                                         │
│  Database:                              │
│  ├─ RDS db.t3.micro PostgreSQL (Free)  │
│  └─ EC2 t2.micro MongoDB (Free)        │
│                                         │
│  Total Cost: $0 (Free Tier, Mês 1-12)  │
│              $17/month (Mês 13+)        │
│                                         │
│  Advantages:                            │
│  ✅ Simplicity (deploy as-is)          │
│  ✅ Cost (Free Tier max)                │
│  ✅ Time (ready in hours)               │
│  ✅ Proven ASP.NET runtime              │
│                                         │
│  Disadvantages:                         │
│  ❌ Manual ops                          │
│  ❌ Single-AZ (no HA)                   │
│  ❌ Manual scaling                      │
│                                         │
│  Upgrade Path:                          │
│  Mês 13: RDS db.t3.small ($45)          │
│  Mês 18: ALB + ASG (when 50+ customers) │
│  Mês 24: ECS Fargate (if needed)        │
└─────────────────────────────────────────┘
```

---

**Document prepared by**: aws-architect  
**Date**: 2026-08-29, 15:42 UTC-3  
**Status**: Technical Analysis & Recommendation  

🎯 **TL;DR**: EC2 is simpler. Lambda is possible but complex. Use EC2 for MVP.
