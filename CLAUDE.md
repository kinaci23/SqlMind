# SqlMind — CLAUDE.md
# AI Destekli SQL Script Analiz ve Aksiyon Sistemi

## Proje Özeti
SqlMind, SQL scriptlerini analiz eden, riskleri tespit eden ve yüksek riskli durumlarda otomatik aksiyon alan hibrit bir AI sistemidir.
**Stack:** ASP.NET Core (.NET 8) / C# · PostgreSQL + pgvector · Gemini API · Redis · Hangfire · Docker

---

## HARD RULES — ASLA İHLAL ETME

Bu kurallar tartışılmaz. Kod yazarken her birini kontrol et:

1. **LLM tek başına karar veremez** — Rule-based analiz her zaman önce çalışır, LLM ikincil katmandır
2. **RAG her istekte tetiklenmez** — Gating logic zorunludur (bkz. RAG Gating Logic)
3. **Tool execution yalnızca Policy Engine onayıyla** gerçekleşir
4. **Free text response yasaktır** — LLM output her zaman structured JSON olmalıdır
5. **Tüm kararlar audit_logs tablosuna yazılır** — correlation_id zorunludur
6. **Direct implementation yasaktır** — Her external dependency bir interface arkasında olmalıdır
7. **Hard-coded IF policy yasaktır** — Policy konfigürasyondan/DB'den okunur

---

## Mimari Formül (Değiştirilemez)

```
SQL ANALİZİ = Deterministic Engine + LLM Analysis + RAG (Conditional) + Policy Engine
```

Hiçbir katman atlanamaz. Sıra:
1. SQL Parser → 2. Rule-Based Risk → 3. RAG (gated) → 4. LLM → 5. Final Risk → 6. Policy Engine → 7. Tool Executor → 8. Audit Log

---

## Zorunlu Interface'ler

Bu 10 interface'in tamamı implement edilmelidir. Direkt implementasyon yasaktır.

| Interface | Sorumluluk |
|---|---|
| `ILLMClient` | LLM provider abstraction |
| `IEmbeddingService` | Metin → vektör dönüşümü |
| `IRagService` | Embedding + similarity search + context assembly |
| `ISqlAnalyzer` | SQL parse, AST analizi, operasyon/tablo/risk çıkarımı |
| `IRiskEvaluator` | Rule-based + LLM risk skoru birleştirme |
| `IPolicyEngine` | Risk seviyesine göre configurable aksiyon kararları |
| `ITool` | Her tool için input/output schema ve execution |
| `IToolExecutor` | Tool çağrısı orchestration |
| `ICacheService` | LLM response ve embedding cache abstraction |
| `IBackgroundJobService` | Async job scheduling (Hangfire default) |

---

## Klasör Yapısı

```
SqlMind/
├── src/
│   ├── SqlMind.API/                  # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   ├── Middleware/               # Auth, rate limiting
│   │   └── Program.cs
│   ├── SqlMind.Core/                 # Domain + Interfaces
│   │   ├── Interfaces/               # 10 zorunlu interface
│   │   ├── Models/                   # Domain modeller
│   │   └── Enums/                    # RiskLevel, OperationType vb.
│   ├── SqlMind.Infrastructure/       # Implementasyonlar
│   │   ├── LLM/                      # GeminiLLMClient : ILLMClient
│   │   ├── Embedding/                # GeminiEmbeddingService : IEmbeddingService
│   │   ├── RAG/                      # RagService : IRagService
│   │   ├── SqlParsing/               # CustomSqlAnalyzer : ISqlAnalyzer
│   │   ├── Risk/                     # RiskEvaluator : IRiskEvaluator
│   │   ├── Policy/                   # PolicyEngine : IPolicyEngine
│   │   ├── Tools/                    # CreateTicketTool, SendNotificationTool vb.
│   │   ├── Cache/                    # RedisCacheService : ICacheService
│   │   ├── Jobs/                     # HangfireJobService : IBackgroundJobService
│   │   └── Persistence/              # EF Core DbContext, Repositories
│   └── SqlMind.Agent/                # Agent orchestration logic
├── tests/
│   ├── SqlMind.UnitTests/
│   └── SqlMind.IntegrationTests/
├── docker-compose.yml
├── docker-compose.dev.yml
└── CLAUDE.md
```

---

## Teknoloji Kararları

| Katman | Teknoloji | Not |
|---|---|---|
| Backend | ASP.NET Core (.NET 8) / C# | Birincil framework |
| Veritabanı | PostgreSQL + pgvector | Vector store dahil |
| ORM | EF Core (primary), Dapper (ham SQL) | İkisi birlikte kullanılabilir |
| Cache | Redis | LLM response cache, deduplication |
| Background Jobs | Hangfire | IBackgroundJobService üzerinden |
| LLM | Gemini API (default) | ILLMClient abstraction zorunlu |
| Embedding | Gemini Embedding API | IEmbeddingService üzerinden |
| SQL Parsing | Özel Parser/Tokenizer | Regex yeterli değil, AST benzeri yapı |
| Agent | Native Function Calling | LangChain/Semantic Kernel yok |
| Auth | JWT Bearer | Her endpoint zorunlu |
| Container | Docker + Docker Compose | |

**Alternatif LLM provider'lar** (ILLMClient üzerinden geçilebilir):
- GPT-4o (OpenAI)
- Claude Sonnet 4 (Anthropic)
- Ollama / qwen2.5-coder:7b (local, hassas veri)

---

## Risk Seviyeleri ve Pattern'lar

```
CRITICAL : DELETE without WHERE, DROP TABLE, TRUNCATE
HIGH     : UPDATE without WHERE, büyük toplu silme, ALTER TABLE
MEDIUM   : Büyük JOIN, performans riski
LOW      : Standart SELECT
```

**Kural Önceliği:** Rule-based sonuç CRITICAL ise LLM onu düşüremez. Rule-based PRIMARY, LLM SECONDARY katmandır.

---

## RAG Gating Logic

```
IF tables_detected == True
   AND (risk_level >= MEDIUM OR context_needed == True)
THEN: RAG çalıştır → context'i LLM prompt'una ekle
ELSE: SKIP → rag_used = False
```

Gereksiz RAG çağrısı yasaktır.

---

## LLM Output Formatı (Zorunlu)

```json
{
  "business_summary": "...",
  "technical_summary": "...",
  "risk_insights": [],
  "uncertainties": [],
  "recommended_actions": []
}
```

- Temperature: 0.0–0.2 (deterministik çıktı)
- Output schema her prompt'a eklenmeli
- Sistem prompt ve kullanıcı prompt izole edilmeli (injection önleme)

---

## API Endpoint'leri

| Method | Path | Açıklama |
|---|---|---|
| POST | `/api/v1/analyze` | SQL script analizi başlat (async, job_id döner) |
| GET | `/api/v1/analyze/{job_id}` | Analiz sonucu getir |
| POST | `/api/v1/knowledge` | Knowledge base'e doküman ekle |
| GET | `/api/v1/knowledge/search` | Knowledge base'de arama |
| GET | `/api/v1/reports` | Geçmiş analizler |
| POST | `/api/v1/tools/execute` | Manuel tool çalıştırma |

---

## Veritabanı Tabloları (Tümü Zorunlu)

`analysis_jobs` · `analysis_results` · `risk_findings` · `knowledge_documents` · `knowledge_chunks` · `embeddings` · `tool_executions` · `audit_logs`

---

## Policy Engine Kuralları (Configurable)

```
CRITICAL → create_ticket + notify (Slack) + require_approval
HIGH     → suggest_ticket
MEDIUM   → warning_log
LOW      → log_only
```

Hard-coded IF yasak. Konfigürasyon dosyasından veya DB'den okunur.

---

## Tool'lar

| Tool | Trigger |
|---|---|
| `CreateTicketTool` | CRITICAL / HIGH |
| `SendNotificationTool` | CRITICAL |
| `RequestApprovalTool` | CRITICAL |

---

## Performans Hedefleri

- Tek script analizi: < 10 saniye (P95)
- Throughput: 50 script/dakika
- Cache: Aynı SQL hash'i için LLM tekrar çağrılmaz (Redis, hash-based key)
- Async: Hangfire job queue — istemciye anında job_id döner

---

## Geliştirme Planı (8 Gün)

| Gün | Odak |
|---|---|
| Gün 1 | Solution yapısı, Docker Compose, PostgreSQL + pgvector, interface tanımları |
| Gün 2 | Custom SQL parser, rule-based risk engine |
| Gün 3 | Gemini API / ILLMClient, prompt tasarımı, structured JSON output |
| Gün 4 | pgvector + Gemini Embedding, IRagService |
| Gün 5 | Agent döngüsü, IPolicyEngine, IToolExecutor, CreateTicketTool |
| Gün 6 | API endpoint'leri, Hangfire async job, JSON rapor |
| Gün 7 | Uçtan uca testler, edge case'ler, Redis cache, logging |
| Gün 8 | README, Swagger, demo |

---

## Audit Log Zorunlu Alanları

`input_hash` · `sql_parse_result` · `rule_triggers` · `llm_output` · `rag_used` · `tool_execution` · `timestamp (ISO 8601)` · `correlation_id`

---

## Güvenlik

- JWT kimlik doğrulama — her API isteğinde zorunlu
- Prompt injection önleme — input sanitization + sistem prompt izolasyonu
- Rate limiting — API gateway seviyesinde
- LLM output JSON schema validasyonu
- Temperature 0.0–0.2 — deterministik ve güvenli çıktı

---

## Geliştirme Notları

- **VS Code + Claude Code** ile geliştirme yapılıyor
- Tüm büyük kararlar önce bu sohbette (Claude.ai) tartışılır, sonra Claude Code ile implemente edilir
- Her yeni bileşen önce interface tanımıyla başlar, sonra implementasyon gelir
- Docker olmadan local test için `docker-compose.dev.yml` ayrı tutulur