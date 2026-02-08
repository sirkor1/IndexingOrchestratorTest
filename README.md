# Search Orchestrator

Сервис-оркестратор, который управляет процессом индексации файлов во внешнем сервисе поиска и предоставляет API для поиска по строке.

## Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                    API Layer                             │
│  IndexingController          SearchController           │
│  POST/GET /api/indexing/tasks   POST /api/search        │
└──────────────┬──────────────────────┬───────────────────┘
               │                      │
┌──────────────▼──────────────────────▼───────────────────┐
│                 Orchestration Layer                      │
│  IndexingOrchestrator (Singleton)                       │
│  - создание задач (с идемпотентностью)                  │
│  - получение статусов                                   │
│  - отмена задач                                         │
│  - делегирование поиска                                 │
└──────┬────────────────────────┬─────────────────────────┘
       │                        │
┌──────▼───────┐  ┌─────────────▼─────────────────────────┐
│ TaskQueue    │  │  IndexingBackgroundService             │
│ (Channel<T>) │──│  - параллельная обработка очереди      │
│              │  │    (TPL Dataflow ActionBlock)          │
└──────────────┘  │  - вызов внешнего сервиса с ретраями   │
                  │  - polling статуса                     │
                  │  - обновление задач                    │
                  └──────────────┬────────────────────────┘
                                 │
┌────────────────────────────────▼────────────────────────┐
│              ISearchEngineClient                        │
│  (интерфейс внешнего сервиса поиска)                    │
│  → HttpSearchEngineClient (HttpClient + Polly)          │
│  → Retry per endpoint + Circuit Breaker per endpoint    │
└─────────────────────────────────────────────────────────┘
```

### Слои и ответственности

| Слой | Компоненты | Ответственность |
|------|-----------|----------------|
| **API** | `IndexingController`, `SearchController` | HTTP-контракты, валидация, маппинг DTO |
| **Middleware** | `CorrelationIdMiddleware` | Корреляция запросов (X-Correlation-Id) |
| **Orchestration** | `IndexingOrchestrator` | Бизнес-логика: создание задач, идемпотентность, отмена |
| **Background** | `IndexingBackgroundService`, `IndexingTaskQueue` | Параллельная асинхронная обработка, polling |
| **Resilience** | `PolicyService` | Polly retry + circuit breaker per endpoint |
| **External** | `ISearchEngineClient` / `HttpSearchEngineClient` | Контракт внешнего сервиса (HTTP + Polly) |
| **Storage** | `IIndexingTaskRepository`, `IIndexingSourceRepository` | Хранение мета-информации (in-memory) |
| **Observability** | `SearchEngineMetrics`, `CorrelationIdMiddleware` | Prometheus-метрики, корреляция запросов |

### Модели данных

**IndexingSource** — источник данных для индексации:
- `Id`, `Uri` (уникальный ключ), `Name`, `CreatedAt`

**IndexingTask** — задание на индексацию (state machine):
- `Id`, `SourceId`, `Status`, `ErrorMessage`, `ExternalTaskId`
- `CorrelationId`, `CreatedAt`, `StartedAt`, `CompletedAt`
- Переходы защищены guard'ами: невалидные переходы бросают `InvalidOperationException`

**IndexingTaskStatus**: `Pending → InProgress → Completed | Failed | Cancelled`

**Result\<T\>** — обёртка для результата операции с ошибкой и HTTP-статусом.

## API-контракты

### Запуск индексации

```
POST /api/indexing/tasks
Content-Type: application/json

{
  "sourceUri": "s3://bucket/documents",
  "sourceName": "Customer Documents"
}
```

- `201 Created` — новая задача создана
- `200 OK` — идемпотентный повтор (для данного `sourceUri` уже есть активная задача)

### Статус задачи

```
GET /api/indexing/tasks/{taskId}
```

- `200 OK` — задача найдена
- `404 Not Found` — задача не найдена

### Отмена задачи

```
POST /api/indexing/tasks/{taskId}/cancel
```

- `200 OK` — задача отменена (или уже была отменена — идемпотентно)
- `404 Not Found` — задача не найдена
- `409 Conflict` — задача в терминальном статусе (Completed, Failed), отмена невозможна

При отмене InProgress-задачи внешний сервис уведомляется через `CancelIndexingAsync`.

### Поиск

```
POST /api/search
Content-Type: application/json

{
  "query": "search string",
  "maxResults": 20
}
```

- `200 OK` — результаты поиска
- `502 Bad Gateway` — внешний сервис недоступен

## Основные сценарии

### Постановка задачи на индексацию

1. Клиент отправляет `POST /api/indexing/tasks`
2. Оркестратор проверяет идемпотентность (есть ли активная задача для этого URI)
3. Создаёт/находит `IndexingSource`, создаёт `IndexingTask` со статусом `Pending`
4. Помещает ID задачи в `IndexingTaskQueue` (bounded Channel)
5. Если очередь переполнена — задача помечается как `Failed`
6. Возвращает клиенту задачу с `201` или `200` (если идемпотентный повтор)

### Фоновая обработка

1. `IndexingBackgroundService` читает ID из очереди
2. `ActionBlock` обрабатывает до `MaxConcurrency` задач параллельно (по умолчанию 5)
3. Вызывает `ISearchEngineClient.StartIndexingAsync()` (transient-ошибки ретраятся Polly)
4. При успехе — обновляет статус на `InProgress`, сохраняет `ExternalTaskId`
5. Периодически опрашивает `CheckIndexingStatusAsync()` до завершения
6. Обновляет финальный статус (`Completed` / `Failed`)
7. При timeout polling'а (настраивается через `MaxPollingDurationMinutes`) — `Failed`

### Обработка ошибок

- **Transient HTTP-ошибки** (5xx, 408, `HttpRequestException`): автоматические ретраи через Polly с настраиваемыми задержками per endpoint
- **Circuit breaker**: изолирован per endpoint — сбой Search не блокирует StartIndexing и наоборот. Поддерживает простой (count-based) и advanced (percentage-based) режимы
- **Исключения после исчерпания ретраев**: задача переводится в `Failed`
- **Ошибка в процессе индексации**: задача переводится в `Failed` с сообщением об ошибке
- **Отмена**: задача отменяется через API, внешний сервис уведомляется
- **Переполнение очереди**: задача создаётся, но сразу помечается как `Failed`

### Корреляция запросов

- Middleware автоматически генерирует или переиспользует `X-Correlation-Id`
- ID сохраняется в задаче и используется в логах через `ILogger.BeginScope`
- Возвращается в заголовке ответа

### Метрики

- `search_engine_http_requests_total` — счётчик HTTP-запросов к внешнему сервису (по endpoint и status_code)
- `search_engine_http_errors_total` — счётчик ошибок (по endpoint и status_code)
- Доступны на `/metrics` (Prometheus)

## Конфигурация

`appsettings.json`:

```json
{
  "SearchEngine": {
    "BaseUrl": "http://localhost:9200",
    "PollingIntervalMs": 2000,
    "QueueCapacity": 100,
    "EnqueueTimeoutMs": 5000,
    "MaxPollingDurationMinutes": 30,
    "MaxConcurrency": 5,
    "Policies": {
      "Default": {
        "Enabled": true,
        "RetryDelays": "1,2,3"
      },
      "Endpoints": {
        "StartIndexing": { "RetryDelays": "1,2,4" },
        "CheckStatus": { "RetryDelays": "0.5,1" },
        "CancelIndexing": { "RetryDelays": "0.5" },
        "Search": { "RetryDelays": "1,2,4" }
      },
      "CircuitBreaker": {
        "Enabled": true,
        "UseAdvanced": true,
        "FailureRateThreshold": 0.5,
        "MinimumThroughput": 10,
        "SamplingDurationSeconds": 30,
        "FailureThreshold": 5,
        "DurationOfBreakSeconds": 30
      }
    }
  }
}
```

| Параметр | Описание | По умолчанию |
|----------|----------|-------------|
| `BaseUrl` | URL внешнего сервиса поиска | `http://localhost:9200` |
| `PollingIntervalMs` | Интервал polling'а статуса (мс) | `2000` |
| `QueueCapacity` | Максимальный размер очереди задач | `100` |
| `EnqueueTimeoutMs` | Таймаут постановки в очередь (мс) | `5000` |
| `MaxPollingDurationMinutes` | Максимальное время polling'а (мин) | `30` |
| `MaxConcurrency` | Параллельная обработка задач | `5` |

## Запуск

```bash
dotnet run
```

- Swagger UI: http://localhost:5272/swagger
- Health check: http://localhost:5272/health
- Metrics: http://localhost:5272/metrics

## Тесты

```bash
dotnet test
```

18 тестов покрывают:
- Создание задач и идемпотентность
- Переиспользование источников
- Отмену задач (Pending, InProgress, Completed)
- Успешную индексацию через background service
- Обработку отказов (rejection, HTTP failure, mid-process failure)
- Polling прогресса
- Пропуск отменённых задач
- Делегирование поиска

## Структура проекта

```
SearchOrchestrator/
├── Configuration/
│   ├── CircuitBreakerSettings.cs      # Настройки circuit breaker
│   ├── Constants.cs                   # Имена HTTP-клиентов и эндпоинтов
│   ├── PolicySettings.cs             # Настройки Polly-политик
│   ├── PollyRetrySettings.cs         # Настройки задержек ретраев
│   └── SearchEngineOptions.cs        # Настройки внешнего сервиса
├── Contracts/
│   ├── CreateIndexingTaskRequest.cs   # DTO запроса создания задачи
│   ├── IndexingTaskResponse.cs       # DTO ответа о задаче
│   ├── SearchRequest.cs              # DTO запроса поиска
│   └── SearchResponse.cs             # DTO ответа поиска
├── Controllers/
│   ├── IndexingController.cs         # API индексации
│   └── SearchController.cs           # API поиска
├── Middleware/
│   └── CorrelationIdMiddleware.cs    # Корреляция запросов
├── Models/
│   ├── IndexingSource.cs             # Модель источника
│   ├── IndexingTask.cs               # Модель задачи (state machine)
│   ├── IndexingTaskStatus.cs         # Статусы задач
│   └── Result.cs                     # Обёртка результата операции
├── Services/
│   ├── ISearchEngineClient.cs        # Контракт внешнего сервиса
│   ├── HttpSearchEngineClient.cs     # HTTP-реализация (IHttpClientFactory + Polly)
│   ├── IPolicyService.cs             # Контракт Polly-политик
│   ├── PolicyService.cs              # Retry + Circuit Breaker per endpoint
│   ├── IIndexingSourceRepository.cs  # Контракт хранения источников
│   ├── IIndexingTaskRepository.cs    # Контракт хранения задач
│   ├── InMemoryIndexingSourceRepository.cs
│   ├── InMemoryIndexingTaskRepository.cs
│   ├── IndexingOrchestrator.cs       # Ядро оркестрации
│   ├── IndexingBackgroundService.cs  # Параллельный фоновый обработчик (ActionBlock)
│   ├── IndexingTaskQueue.cs          # Очередь задач (bounded Channel)
│   └── SearchEngineMetrics.cs        # Prometheus-метрики
├── Program.cs                        # Точка входа, DI
└── SearchOrchestrator.Tests/
    ├── IndexingOrchestratorTests.cs   # Тесты оркестратора
    └── IndexingBackgroundServiceTests.cs # Тесты background-обработки
```

## Расширяемость

- **IIndexingTaskRepository / IIndexingSourceRepository** — заменить in-memory на БД (EF Core, Dapper)
- **IndexingTaskQueue** — заменить Channel на RabbitMQ, Kafka и т.д.
- **ISearchEngineClient** — подключить реальный внешний сервис вместо заглушки
- **PolicyService** — добавить per-endpoint настройки circuit breaker, timeout policy
- **Масштабирование** — background service можно вынести в отдельный воркер-сервис
