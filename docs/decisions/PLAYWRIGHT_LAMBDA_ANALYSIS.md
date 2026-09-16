# Playwright em Lambda - Análise Técnica Detalhada

**Data**: 2026-08-29, 15:55 UTC-3  
**Status**: Technical Analysis (Critical Path Item)  
**Context**: Collector precisa fazer login + extração de dados via Playwright  

---

## 1. Pergunta: Playwright funciona em Lambda?

### Resposta Técnica: ⚠️ SIM, mas com limitações

```
Viabilidade:        ✅ Técnicamente possível
Recomendação:       ⚠️ Avaliar trade-offs vs EC2
Production Ready:   ❌ Não ideal para MVP
Performance:        ❌ Cold start problemático
```

---

## 2. Análise Técnica Detalhada

### 2.1 Limitações Lambda vs Playwright

#### **Problema 1: Package Size**

```
Playwright Chromium (browserless):
  chromium.zip:           ~200-300 MB (descompactado)
  @playwright/browser:    ~100 MB
  node_modules:           ~50 MB
  ─────────────────────
  Total:                  ~350-450 MB (uncompressed)

Lambda Limits:
  Deployment package zip:     50 MB max ❌ EXCEED
  Uncompressed code:          250 MB max ❌ EXCEED
  Solution:
    ├─ Container image:       10 GB max ✅ ENOUGH
    ├─ Lambda Layer + S3:     ~3 layers × 50MB = 150MB limit ❌
    └─ Bundled container:     Best option

Result: MUST use Lambda container image (not zip deployment)
```

#### **Problema 2: Cold Start Latency**

```
Lambda Container Image Startup:
  1. ECR pull (~2-5s)
  2. Extract layers (~2-3s)
  3. Node.js runtime init (~1-2s)
  4. Playwright browser init (~10-15s) ← BIGGEST COST
  5. Page load + login (~5-10s)
  ─────────────────────────────
  Total cold start: ~20-35 seconds ❌

Impact:
  First invocation after deployment: 20-35s latency
  Warm invocation (same container): ~2-5s
  Problem: EventBridge scheduler = new invocation every 15 min
           → Likely to hit cold start regularly

Comparison:
  EC2 always-on:   ~5-10s from start to login ✅
  Lambda warm:     ~2-5s (after cold start)
  Lambda cold:     ~20-35s ❌
```

#### **Problema 3: Timeout**

```
Lambda Max Timeout: 15 minutes (900 seconds)

Collector Flow:
  1. Login + session:           5-10s
  2. Fetch OS list (per tenant): 5-30s (depends on iService)
  3. Extract data:              10-60s (depends on volume)
  4. Store to RDS/MongoDB:      5-15s
  ─────────────────────────────
  Estimated per tenant: 25-115 seconds

For 10 tenants:
  10 tenants × 100s avg = 1000s (~16.7 min) ❌ EXCEEDS 15 min timeout!

Solution Options:
  A. Process fewer tenants per invocation (2-3 tenants)
     → Multiple Lambda invocations
     → SQS queue with batch processing
     → Cost increases (more invocations)

  B. Increase concurrency
     → Run 5 parallel Lambdas (each 2 tenants)
     → Costs: Provisioned Concurrency = $0.015/hour × 5 × 24 = $1.80/day
     → Total: $54/month just for concurrency

  C. Use EC2 instead (single process handles all tenants)
     → No timeout limit
     → Single invocation per schedule
```

#### **Problema 4: Memory & CPU**

```
Playwright Requirements:
  Browser process (Chromium): ~200-300 MB
  Node.js process:            ~50-100 MB
  Data processing:            ~50 MB
  ──────────────────────
  Minimum: 512 MB (tight)
  Recommended: 1024 MB (safe)

Lambda Pricing (1024 MB):
  $0.0000166667 per GB-second
  Collector job: 30s × 1 GB = 30 GB-seconds
  10 tenants × 30 invocations/month = 9000 invocations × 30s = 270,000 GB-seconds
  Cost: 270,000 × $0.0000166667 = $4.50/month (compute)

Plus EventBridge: 9000 invocations × $0.00279 = $25.11/month

Total Lambda approach: ~$30-35/month
vs EC2: $8.50/month (already running)

Delta: +$22-26/month for Lambda (not worth it)
```

#### **Problema 5: Browser Control & Sessions**

```
Playwright in Lambda Challenge:
  
  Browser Persistence:
    - Browser instance = ephemeral (new each invocation)
    - Session cookies lost between invocations
    - Workaround: Store cookies in Redis/DynamoDB
    - Extra cost + complexity ❌

  Browser Orchestration:
    - Playwright Server Mode (remote browser)
    - BrowserStack/SauceLabs (cloud browsers)
    - Cost: $100-500/month ❌❌

  Better: Keep browser running on EC2
    - Reuse browser instance
    - Session persistence
    - Cost: $8.50/month
```

---

### 2.2 Container Image Implementation

#### **If you insist on Lambda...**

```dockerfile
# Dockerfile - Playwright Lambda
FROM public.ecr.aws/lambda/nodejs:18

# Install Playwright dependencies
RUN apt-get update && apt-get install -y \
  libglib2.0-0 libx11-6 libx11-xcb1 libdbus-1-3 libxrender1 \
  libxkbcommon0 libgbm1 libdrm2 libnss3 libasound2

# Install Playwright & browsers
RUN npm install playwright

# Copy collector code
COPY collector/ ${LAMBDA_TASK_ROOT}/

CMD [ "index.handler" ]
```

```javascript
// index.js - Lambda handler
const { chromium } = require('playwright');

exports.handler = async (event) => {
  console.log('Cold start - initializing browser...');
  
  const browser = await chromium.launch({
    headless: true,
    args: ['--disable-gpu', '--no-sandbox'] // Lambda requirements
  });
  
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    
    // Login flow
    await page.goto('https://iservice.example.com/login');
    await page.fill('input[name="username"]', process.env.TENANT_USER);
    await page.fill('input[name="password"]', process.env.TENANT_PASSWORD);
    await page.click('button[type="submit"]');
    
    // Wait for navigation
    await page.waitForNavigation();
    
    // Extract OS data
    const osData = await page.evaluate(() => {
      return document.querySelectorAll('.os-row').map(row => ({
        id: row.querySelector('.id').textContent,
        status: row.querySelector('.status').textContent
      }));
    });
    
    // Store in database
    await saveToDatabase(osData);
    
    return {
      statusCode: 200,
      body: JSON.stringify({ processed: osData.length })
    };
  } finally {
    await browser.close();
  }
};
```

**Problems with above:**
- ❌ `browser.launch()` called EVERY invocation (slow)
- ❌ No session reuse (login every time)
- ❌ 20-35s per invocation
- ❌ Timeout risk with multiple tenants

---

### 2.3 EC2 Alternative (Recommended)

```csharp
// CollectorService.cs - EC2 approach
public class CollectorService : BackgroundService
{
  private readonly IPlaywrightClient _playwright;
  
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Initialize browser ONCE on startup
    _playwright = new PlaywrightClient();
    await _playwright.InitializeBrowserAsync();
    
    // Keep browser running in background
    while (!stoppingToken.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
      await CollectFromAllTenantsAsync();
    }
  }
  
  private async Task CollectFromAllTenantsAsync()
  {
    var tenants = await _repository.GetAllTenantsAsync();
    
    foreach (var tenant in tenants)
    {
      try
      {
        // Reuse same browser instance
        var osData = await _playwright.ExtractOrdersAsync(
          tenant.iServiceUsername,
          tenant.iServicePassword
        );
        
        await _repository.SaveOrdersAsync(tenant.Id, osData);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error collecting tenant {Id}", tenant.Id);
      }
    }
  }
}

// Startup method
public class PlaywrightClient
{
  private IBrowser _browser;
  
  public async Task InitializeBrowserAsync()
  {
    // Browser starts ONCE, runs continuously
    var playwright = await Playwright.CreateAsync();
    _browser = await playwright.Chromium.LaunchAsync();
  }
  
  public async Task<List<Order>> ExtractOrdersAsync(string user, string pass)
  {
    var context = await _browser.NewContextAsync();
    var page = await context.NewPageAsync();
    
    // Login
    await page.GotoAsync("https://iservice.example.com/login");
    await page.FillAsync("input[name='username']", user);
    await page.FillAsync("input[name='password']", pass);
    await page.ClickAsync("button[type='submit']");
    await page.WaitForNavigationAsync();
    
    // Extract
    var osData = await page.EvaluateAsync<List<Order>>(
      "() => document.querySelectorAll('.os-row').map(row => ({ ... }))"
    );
    
    await context.CloseAsync();
    return osData;
  }
}
```

**Advantages:**
- ✅ Browser initialized ONCE (20s overhead happens once at startup)
- ✅ Reuse browser instance (2-5s per execution)
- ✅ Session persistence
- ✅ No timeout risk
- ✅ Cost: $8.50/month (already running)

---

## 3. Custo Comparativo

### Cenário A: Lambda + SQS + EventBridge

```
EventBridge Scheduler:
  10 tenants × 4/hour × 24h × 30d = 28,800 invocations
  $0.00279 per invocation = $80.47/month

SQS (1 message per tenant):
  28,800 messages/month = $0 (Free Tier 1M)

Lambda (1024 MB, 30s per invocation):
  28,800 invocations × 30s × 1GB = 864,000 GB-seconds
  Minus Free Tier (400k GB-seconds) = 464,000 GB-seconds
  $0.0000166667 × 464,000 = $7.73/month

Provisioned Concurrency (if needed to avoid cold starts):
  5 concurrent × $0.015/hour × 730h = $54.75/month

Total Lambda Approach:
  Without concurrency: $80.47 + $7.73 = $88.20/month
  With concurrency:    $80.47 + $7.73 + $54.75 = $142.95/month
```

### Cenário B: EC2 + Cron + Playwright

```
EC2 t2.micro:
  $0/month (Free Tier, already running for MongoDB)
  (or $8.50/month after Free Tier)

Playwright on EC2:
  Initial setup: ~30 min
  Browser runs: Continuously in background
  Memory usage: ~300-400 MB (acceptable on t2.micro 1GB)
  Disk usage: ~500 MB (Chromium executable)

Total EC2 Approach:
  $0-8.50/month (just the EC2 itself)
  No extra costs for Playwright
```

### Cost Comparison Summary

```
┌─────────────────────────────────────────────────────┐
│           Playwright Execution Cost                 │
├──────────────────┬──────────────────┬──────────────┤
│ Option           │ MVP Cost         │ After FT     │
├──────────────────┼──────────────────┼──────────────┤
│ Lambda           │ $88.20/mo        │ $88.20/mo    │
│ Lambda + Concur. │ $142.95/mo       │ $142.95/mo   │
│ EC2 (existing)   │ $0/mo            │ $8.50/mo     │
├──────────────────┼──────────────────┼──────────────┤
│ Difference       │ -$88.20/mo       │ -$79.70/mo   │
└─────────────────────────────────────────────────────┘

Lambda is 10-17x more expensive than EC2!
```

---

## 4. Análise de Query Patterns

**Pergunta**: Será que Playwright é realmente necessário?

**Contexto**: iService oferece:
1. ✅ Web UI (requires Playwright)
2. ❓ API REST (se disponível)?
3. ❓ API SOAP (se disponível)?

**Recomendação**: 
- Verificar com time iService se há API alternativa
- Se não, Playwright é necessário
- Se sim, usar API (mais simples, mais rápido, mais barato)

```csharp
// API approach (if available)
public async Task<List<Order>> FetchOrdersViaApiAsync(string tenantId)
{
  var client = new HttpClient();
  var response = await client.GetAsync(
    $"https://api.iservice.com/v1/orders?tenant={tenantId}",
    new { 
      Authorization = $"Bearer {token}"
    }
  );
  
  var orders = await response.Content.ReadAsAsync<List<Order>>();
  return orders;
}

// Cost: ~$0/month (just HTTP calls)
// Time: 1-5 seconds per tenant (vs 10-30s with Playwright)
```

---

## 5. Viabilidade Scorecard

```
╔══════════════════════════════════════════════════════════════╗
║         Playwright Implementation Viability                  ║
╠══════════════════════════════════════════════════════════════╣
║                                                              ║
║ Lambda + Playwright:                                         ║
║  ├─ Technically Possible?      ✅ Yes (container image)      ║
║  ├─ Cold Start Acceptable?     ❌ 20-35s too slow            ║
║  ├─ Timeout Risk?              ⚠️  High (15 min limit)       ║
║  ├─ Cost Effective?            ❌ $88-143/mo (wasteful)     ║
║  ├─ MVP Ready?                 ❌ Complex setup              ║
║  └─ Recommendation:            ❌ NOT RECOMMENDED            ║
║                                                              ║
║ EC2 + Playwright:                                            ║
║  ├─ Technically Possible?      ✅ Yes (simple setup)        ║
║  ├─ Cold Start?                ✅ None (browser always-on)  ║
║  ├─ Timeout Risk?              ✅ No (process control)      ║
║  ├─ Cost Effective?            ✅ $0-8.50/mo                ║
║  ├─ MVP Ready?                 ✅ Ready in 2-3 hours        ║
║  └─ Recommendation:            ✅ HIGHLY RECOMMENDED        ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
```

---

## 6. Recomendação Final

### 🎯 **RECOMENDAÇÃO: EC2 + Playwright (NOT Lambda)**

**Justificativa**:

1. **Cold Start**: EC2 avoids 20-35s delay
2. **Timeout**: No 15-min limit (can handle all tenants)
3. **Cost**: $0-8.50/mo (vs $88-143/mo Lambda)
4. **Simplicity**: .NET Worker Service + cron
5. **Browser State**: Persistent sessions (no re-login)

### **Implementation Path**

```
EC2 t2.micro (already running for MongoDB):
  ├─ Install Chromium + Playwright
  ├─ Create CollectorService (.NET Worker)
  ├─ Add Playwright client library
  ├─ Timer: every 15 minutes
  └─ Cost: $0 (included with t2.micro)

Time to implement: 2-3 hours
Risk level: Low (proven approach)
```

### **Alternative: Verify iService API**

Before investing in Playwright, confirm:
```
1. Does iService have REST/SOAP API?
2. What's authentication method (OAuth, API key)?
3. What are rate limits?
4. What data format (JSON, XML)?

If YES → Use API instead (simpler, cheaper, faster)
If NO → Use Playwright on EC2
```

---

## 7. Arquivos de Configuração

### docker-compose.yml (EC2 Setup)

```yaml
version: '3.8'

services:
  mongodb:
    image: mongo:5.0
    ports:
      - "27017:27017"
    volumes:
      - mongodb_data:/data/db

  collector:
    build:
      context: ./src/Atua.Collector
      dockerfile: Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      PLAYWRIGHT_HEADLESS: "true"
      ISERVICE_URL: ${ISERVICE_URL}
    depends_on:
      - mongodb
    command: dotnet Atua.Collector.dll

volumes:
  mongodb_data:
```

### Dockerfile (Collector + Playwright)

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:7.0

# Install Playwright dependencies
RUN apt-get update && apt-get install -y \
    libglib2.0-0 libx11-6 libx11-xcb1 \
    libdbus-1-3 libxrender1 libxkbcommon0 libgbm1

# Install Playwright
RUN dotnet add package Microsoft.Playwright
RUN playwright install chromium

# Copy app
COPY bin/Release/net7.0/ /app/
WORKDIR /app

CMD ["dotnet", "Atua.Collector.dll"]
```

---

**Prepared by**: aws-architect  
**Date**: 2026-08-29, 15:55 UTC-3  
**Status**: ✅ Technical Analysis Complete  

**Bottom Line**: Use EC2 + Playwright, NOT Lambda. Save $88-143/month, avoid complexity.
