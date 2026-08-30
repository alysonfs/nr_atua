# S3 + CloudFront + Route53 - Landing & Estáticos Análise

**Data**: 2026-08-29, 16:00 UTC-3  
**Status**: Technical Analysis (Landing Page + Static Assets)  
**Context**: Hosted landing page, documentation, static assets  

---

## 1. Resumo Executivo

### S3 + CloudFront + Route53 para Landing

```
Arquitetura:
  S3 Bucket (origem)
    ↓
  CloudFront (cache global, CDN)
    ↓
  Route53 (DNS)
    ↓
  User Browser (atua.example.com)

Custo MVP:
  S3 Storage:       ~$1-2/mo (landing + assets)
  CloudFront:       ~$0-5/mo (low traffic MVP)
  Route53:          ~$0.50/mo (hosted zone)
  ──────────────────
  Total:            ~$2-8/mo

Vantagem:
  ✅ CDN global (fast everywhere)
  ✅ HTTPS automático (ACM free)
  ✅ Serverless (no infrastructure)
  ✅ Auto-scaling
  ✅ Cheap

Desvantagem:
  ❌ Static content only (no backend)
  ❌ Builds required for updates
  ❌ No dynamic rendering
```

---

## 2. Arquitetura Detalhada

### 2.1 S3 (Object Storage)

```
Bucket: atua-landing-prod
├─ index.html (landing page)
├─ about.html
├─ pricing.html
├─ css/
│  └─ styles.css
├─ js/
│  ├─ app.js
│  └─ analytics.js
├─ images/
│  ├─ logo.png
│  ├─ screenshot.png
│  └─ team/ (team photos)
└─ docs/
   ├─ api.html
   ├─ getting-started.html
   └─ faq.html

Configuração:
  ├─ Versioning: Disabled (save cost)
  ├─ Public Access: Blocked (CloudFront only)
  ├─ Encryption: Enabled (AWS managed keys)
  └─ Lifecycle: Delete old versions after 30 days

Storage Estimation:
  index.html: ~50 KB
  Static assets: ~500 KB
  Images: ~5-10 MB (low res, optimized)
  Docs: ~2 MB
  ──────────────
  Total: ~10-15 MB

Custo S3:
  Storage: ~10 MB × $0.023/GB = $0.00023/mo ✅ (basically free)
  Requests: ~1000 GET /mo × $0.0004/1000 = $0.0004/mo
  ────────────────────
  Total S3: <$0.01/mo
```

### 2.2 CloudFront (CDN)

```
Distribution Configuration:
  Origin: S3 bucket (atua-landing-prod.s3.amazonaws.com)
  Default Root Object: index.html
  TTL: 24 hours (for landing page changes)
  
  Behaviors:
    ├─ /* (all objects)
    │  ├─ TTL: 24h (HTML pages)
    │  ├─ Compress: Yes (gzip)
    │  └─ Cache Policy: Managed-CachingOptimized
    │
    ├─ /css/* 
    │  ├─ TTL: 30 days (CSS rarely changes)
    │  └─ Cache Policy: Managed-CachingOptimized
    │
    ├─ /js/*
    │  ├─ TTL: 30 days
    │  └─ Cache Policy: Managed-CachingOptimized
    │
    └─ /images/*
       ├─ TTL: 365 days
       └─ Cache Policy: Managed-CachingOptimized

SSL/TLS:
  ├─ Certificate: AWS Certificate Manager (free)
  ├─ Domain: atua.example.com
  ├─ HTTPS: Automatic redirect
  └─ min TLS 1.2

Pricing CloudFront (US/EU):
  Data Transfer OUT: $0.085/GB (first 10 TB)
  HTTP/HTTPS requests: $0.0075 per 10k requests
  
  Estimativa MVP (low traffic landing):
    ├─ Traffic: 100 users/day
    ├─ Avg session: 5 pages × 50KB = 250 KB/user
    ├─ Monthly: 100 × 30 × 250KB = 750 MB
    ├─ Data Transfer: 750 MB = $0.0638/mo
    ├─ Requests: 100 users × 30 days × 5 pages = 15k requests
    ├─ Request cost: 15k / 10k × $0.0075 = $0.01125/mo
    └─ Total CloudFront: ~$0.08/mo ✅ (basically free)

  If 1000 users/day:
    └─ Traffic: 7.5 GB × $0.085 = $0.64/mo
    └─ Requests: 150k × $0.0075/10k = $0.11/mo
    └─ Total: ~$0.75/mo (still very cheap)
```

### 2.3 Route53 (DNS)

```
Hosted Zone: example.com
  Records:
    ├─ example.com A → CloudFront distribution
    ├─ www.example.com CNAME → CloudFront
    ├─ api.example.com A → API Gateway or ALB
    ├─ mail MX → Email provider (Gmail, Zoho)
    └─ TXT (SPF, DKIM for email)

Pricing Route53:
  Hosted Zone: $0.50/month per zone
  Query: $0.40 per million queries
  
  Estimativa MVP:
    Hosted Zone: $0.50/mo
    Queries: ~100k/month (API + website)
    Query cost: 100k / 1,000,000 × $0.40 = $0.04/mo
    ────────────────────
    Total Route53: ~$0.54/mo
```

---

## 3. Custo Completo S3 + CloudFront + Route53

### Breakdown por Componente

```
┌───────────────────────────────────────────────────────┐
│    Landing Page + Static Assets Custo                 │
├────────────────────┬─────────────┬──────────────────┤
│ Serviço            │ MVP Cost    │ 1000 users/dia   │
├────────────────────┼─────────────┼──────────────────┤
│ S3 Storage         │ <$0.01      │ <$0.01           │
│ CloudFront (data)  │ $0.06       │ $0.64            │
│ CloudFront (req)   │ $0.01       │ $0.11            │
│ Route53            │ $0.54       │ $0.54            │
├────────────────────┼─────────────┼──────────────────┤
│ TOTAL/MÊS          │ $0.62       │ $1.30            │
└────────────────────┴─────────────┴──────────────────┘

Even at 1000 users/day, cost is <$2/mo!
Serverless + CDN = Powerful for landing pages
```

### Comparação: S3/CloudFront vs Alternatives

```
┌────────────────────┬──────────────┬──────────────────┬──────────┐
│ Option             │ Custo        │ Configuração     │ Escalabilidade │
├────────────────────┼──────────────┼──────────────────┼──────────┤
│ S3+CloudFront      │ ~$1/mo       │ Simples          │ ✅ Auto |
│ GitHub Pages       │ $0           │ Git-native       │ ✅ Auto |
│ Netlify/Vercel     │ $0-20/mo     │ Developer-friendly│ ✅ Auto |
│ EC2+Nginx          │ $8-16/mo     │ Manual ops       │ ❌ Manual|
│ ECS Fargate        │ $30-50/mo    │ Complex          │ ⚠️ Overkill  |
└────────────────────┴──────────────┴──────────────────┴──────────┘

Winner for MVP: GitHub Pages ($0) or S3+CloudFront (~$1)
```

---

## 4. Deployment & Update Workflow

### 4.1 Manual Deployment (via AWS Console)

```
1. Create landing page (HTML/CSS/JS)
2. Build locally: npm run build
3. Upload to S3:
   aws s3 sync build/ s3://atua-landing-prod/
4. CloudFront cache invalidation:
   aws cloudfront create-invalidation --distribution-id E123ABC \
   --paths "/*"
5. Done! Live in ~60 seconds
```

### 4.2 CI/CD Deployment (via GitHub Actions)

```yaml
name: Deploy Landing

on:
  push:
    branches: [main]
    paths: ['landing/**']

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Build
        run: |
          cd landing
          npm install
          npm run build
      
      - name: Deploy to S3
        env:
          AWS_ACCESS_KEY_ID: ${{ secrets.AWS_ACCESS_KEY_ID }}
          AWS_SECRET_ACCESS_KEY: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
        run: |
          aws s3 sync landing/dist/ \
            s3://atua-landing-prod/ \
            --delete
      
      - name: Invalidate CloudFront
        env:
          AWS_ACCESS_KEY_ID: ${{ secrets.AWS_ACCESS_KEY_ID }}
          AWS_SECRET_ACCESS_KEY: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
        run: |
          aws cloudfront create-invalidation \
            --distribution-id E123ABC \
            --paths "/*"
```

---

## 5. Landing Page Setup (Terraform IaC)

### 5.1 S3 Bucket

```hcl
# s3.tf
resource "aws_s3_bucket" "landing" {
  bucket = "atua-landing-prod"
}

resource "aws_s3_bucket_versioning" "landing" {
  bucket = aws_s3_bucket.landing.id
  versioning_configuration {
    status = "Disabled"  # Cost optimization
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "landing" {
  bucket = aws_s3_bucket.landing.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "landing" {
  bucket = aws_s3_bucket.landing.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true  # CloudFront only
}

resource "aws_s3_bucket_lifecycle_configuration" "landing" {
  bucket = aws_s3_bucket.landing.id

  rule {
    id     = "delete-old-versions"
    status = "Enabled"

    noncurrent_version_expiration {
      noncurrent_days = 30
    }
  }
}
```

### 5.2 CloudFront Distribution

```hcl
# cloudfront.tf
resource "aws_cloudfront_distribution" "landing" {
  origin {
    domain_name = aws_s3_bucket.landing.bucket_regional_domain_name
    origin_id   = "s3-landing"

    s3_origin_config {
      origin_access_identity = aws_cloudfront_origin_access_identity.oai.cloudfront_access_identity_path
    }
  }

  enabled = true
  default_root_object = "index.html"

  default_cache_behavior {
    allowed_methods  = ["GET", "HEAD"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "s3-landing"

    forwarded_values {
      query_string = false
      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 86400  # 24 hours
    max_ttl                = 31536000  # 1 year
    compress               = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

resource "aws_cloudfront_origin_access_identity" "oai" {
  comment = "OAI for landing S3"
}
```

### 5.3 Route53 DNS

```hcl
# route53.tf
resource "aws_route53_zone" "example" {
  name = "example.com"
}

resource "aws_route53_record" "landing" {
  zone_id = aws_route53_zone.example.zone_id
  name    = "example.com"
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.landing.domain_name
    zone_id                = aws_cloudfront_distribution.landing.hosted_zone_id
    evaluate_target_health = false
  }
}

resource "aws_route53_record" "www" {
  zone_id = aws_route53_zone.example.zone_id
  name    = "www.example.com"
  type    = "CNAME"
  ttl     = 300
  records = [aws_cloudfront_distribution.landing.domain_name]
}

# API subdomain (pointing to API Gateway or ALB)
resource "aws_route53_record" "api" {
  zone_id = aws_route53_zone.example.zone_id
  name    = "api.example.com"
  type    = "A"

  alias {
    name                   = aws_api_gateway_domain_name.api.cloudfront_domain_name
    zone_id                = aws_api_gateway_domain_name.api.cloudfront_zone_id
    evaluate_target_health = false
  }
}
```

---

## 6. Performance Optimization

### Recommended Practices

```
1. Image Optimization:
   ├─ Resize images to actual display size
   ├─ Use WebP format (smaller, faster)
   ├─ Compress PNG/JPEG (tinypng.com)
   ├─ Target: <5MB total landing images

2. Asset Bundling:
   ├─ Minify CSS/JavaScript
   ├─ Combine small files
   ├─ Use gzip (CloudFront automatic)
   ├─ Target: <100KB CSS, <200KB JS

3. HTTP Caching:
   ├─ Cache busting: index.html (no cache)
   ├─ CSS/JS: 30-day cache
   ├─ Images: 1-year cache
   ├─ CloudFront: 24h default

4. Metrics:
   ├─ Lighthouse score: >90
   ├─ Page load time: <2s
   ├─ Time to First Contentful Paint (FCP): <1s
```

---

## 7. Segurança

### 7.1 Configuração Segura

```
HTTPS:
  ✅ CloudFront automatic SSL (ACM certificate free)
  ✅ Minimum TLS 1.2
  ✅ Redirect HTTP → HTTPS

CORS:
  ✅ CloudFront handles CORS headers
  ✅ API Gateway handles cross-domain requests

CSP (Content Security Policy):
  ✅ Restrict script sources
  ✅ Prevent XSS attacks

Headers:
  ✅ X-Frame-Options: DENY (prevent clickjacking)
  ✅ X-Content-Type-Options: nosniff
```

### 7.2 S3 Security

```
Bucket Access:
  ✅ Public access blocked
  ✅ CloudFront only (via OAI)
  ✅ Encryption enabled

Versioning:
  ❌ Disabled (cost optimization)
  ⚠️  If needed: Enable + lifecycle delete
```

---

## 8. Alternativa: GitHub Pages

### GitHub Pages vs S3+CloudFront

```
┌──────────────────┬──────────────────┬──────────────────┐
│ Aspecto          │ GitHub Pages     │ S3+CloudFront    │
├──────────────────┼──────────────────┼──────────────────┤
│ Custo            │ $0               │ ~$0.60-1/mo      │
│ Setup time       │ ~15 min          │ ~30 min          │
│ Configuração     │ Simples (git)    │ Terraform/CLI    │
│ CDN              │ Sim (GitHub)     │ Sim (CloudFront) │
│ Custom domain    │ ✅ Sim           │ ✅ Sim           │
│ Serverless fnc   │ ❌ Não           │ ❌ Não           │
│ Build pipeline   │ ✅ GitHub Actions│ ⚠️  Manual/CA    │
│ Lock-in          │ GitHub only      │ AWS portable     │
└──────────────────┴──────────────────┴──────────────────┘

Recomendação: 
  ✅ GitHub Pages for simplicity ($0)
  ✅ S3+CloudFront if need AWS integration later
```

---

## 9. Recomendação Final

### 🎯 **RECOMENDADO: GitHub Pages (MVP) → S3+CloudFront (Escalação)**

#### **MVP (Mês 1-3): GitHub Pages**

```
Configuração:
  ├─ Landing repository: github.com/atua/landing
  ├─ GitHub Pages: Settings → Pages
  ├─ Custom domain: example.com via Route53
  └─ GitHub Actions: Auto-deploy on push

Cost: $0/month
Setup: ~15 minutes
Maintenance: None (GitHub managed)
```

#### **Escalação (Mês 3+): S3+CloudFront**

```
Trigger:
  ├─ Need more CDN performance
  ├─ Global audience growth
  ├─ Need AWS integration
  └─ Complex caching requirements

Migration:
  ├─ Export GitHub Pages to S3
  ├─ Set up CloudFront + Route53
  ├─ Update DNS
  └─ Duration: ~1 hour

New Cost: ~$0.60-1/mo (minimal)
Benefit: Better CDN, AWS ecosystem integration
```

---

## 10. Custo Total Estimado (MVP)

### Landing Page + Static Assets

```
S3 Storage:              $0-0.01/mo
CloudFront (data):       $0.06-0.75/mo
CloudFront (requests):   $0.01-0.11/mo
Route53:                 $0.54/mo
────────────────────────────
Total Landing:           $0.61-1.41/mo

vs GitHub Pages:         $0/mo
Difference:              +$0.61-1.41/mo

Recomendação MVP:
  Use GitHub Pages ($0)
  Migrate to S3+CloudFront when ready ($1/mo)
```

---

**Prepared by**: aws-architect  
**Date**: 2026-08-29, 16:00 UTC-3  
**Status**: ✅ Technical Analysis Complete  

**TL;DR**:
- **MVP**: GitHub Pages ($0) - simplest
- **Escalação**: S3 + CloudFront (~$1/mo) - global CDN
- **Cost**: Negligible either way
- **Setup**: 15-30 minutes
