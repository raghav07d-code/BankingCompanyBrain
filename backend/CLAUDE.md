# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Requirements: .NET 10 SDK and a local PostgreSQL instance.

```bash
dotnet restore
dotnet build
dotnet run --project src/BankingBrain.Api          # applies EF Core migrations and seeds the dev admin on startup
dotnet test                                        # runs the integration test suite against real Postgres/JWT/EF Core
dotnet test --filter FullyQualifiedName~ClassName  # run a single test class
dotnet test --filter FullyQualifiedName~ClassName.MethodName  # run a single test
```

There is one test project, `tests/BankingBrain.IntegrationTests`, and no separate unit test project — tests spin up the real API via `BankingBrainApiFactory` (a `WebApplicationFactory<Program>`) against a uniquely-named Postgres database and a temp directory for local object storage/connector roots per run. There is no mocking layer for persistence; expect tests to require Postgres running locally on the default port with the `raghavendra` user.

The dev admin credentials and JWT secret are configured in `src/BankingBrain.Api/appsettings.Development.json`.

## Architecture

Clean Architecture / CQRS layering, six projects referenced in dependency order:

- **BankingBrain.Domain** — entities, value objects, domain events, enums, specifications, and repository interfaces. No dependencies on other layers.
- **BankingBrain.Application** — CQRS handlers organized by feature under `Features/<FeatureName>/{Commands,Queries,Interfaces,Validators,Events,Mappings,DTOs}`. Uses MediatR for command/query dispatch and FluentValidation for input validation. Cross-cutting concerns are implemented as MediatR pipeline behaviors in `Behaviors/`: `ValidationBehavior`, `AuthorizationBehavior`, `TransactionBehavior`, `CachingBehavior`, `LoggingBehavior`, `PerformanceBehavior`, `ExceptionBehavior` (registration order in `ApplicationServiceCollection` matters — only Validation/Logging/Performance are currently wired up).
- **BankingBrain.Contracts** — DTOs shared across boundaries: `Requests/`, `Responses/`, `Events/`, `Models/`.
- **BankingBrain.Infrastructure** — EF Core (`Persistence/`, Npgsql provider, `Migrations/`), identity/JWT (`Identity/`), source connectors (`Connectors/`), document processing pipeline (`Processing/`), AI/LLM providers (`AI/`), caching, messaging, storage, and telemetry. `DependencyInjection/InfrastructureServiceCollection.cs` is the composition root for this layer.
- **BankingBrain.Api** — minimal API endpoint modules under `Endpoints/<Area>/`, registered explicitly in `Program.cs` (e.g. `app.MapCreateWorkspace()`), plus `Middleware/`, `Filters/`, and OpenAPI/Scalar setup.
- **BankingBrain.Shared** — cross-cutting helpers, constants, extensions, and a `Results` type used for the app's error/success pattern independent of exceptions.

Each project has its own `GlobalUsings.cs`.

### Domain model

Workspace-scoped resources form the core aggregate chain: `Workspace` → `KnowledgeSource` → `Document` → `DocumentVersion` → `DocumentSection`. `KnowledgeSource` represents a future file, web, email, database, API, or manual origin. `DocumentVersion` stores immutable content hashes, an optional object-storage key, optional extracted text, a processing state, and a version number; `Document` holds an explicit current-version pointer that only advances when content actually changes (uploads are idempotent by SHA-256 over the exact stored bytes). `ProcessingJob`/`ProcessingAttempt` track background processing with row-locking and lease expiry for worker recovery — only one pending/processing job is allowed per version, and completed/failed jobs remain as history.

All workspace-scoped queries/commands must verify the authenticated user owns the workspace before touching child resources; cross-workspace or cross-owner access returns `404`, not `403`, to avoid resource enumeration (see `AuthenticationWorkspaceTests.cs`). Application errors map to `401` (unauthorized), `404` (not found), `409` (duplicate names, e.g. workspace name uniqueness is per-owner), `400` (other business/validation errors), via the exception middleware and a consistent JSON error body.

### Source connectors

`Connectors/` implements `ISourceConnector` per source type (`FileSystemSourceConnector`, `GitSourceConnector`), resolved via `SourceConnectorRegistry`. Connectors are sandboxed to configured allowed roots (`Connectors:FileSystem:AllowedRoots`, `Connectors:Git:AllowedRoots` in `appsettings.json`, pointing at the top-level `sources/` and `repositories/` directories) and enforce path containment via `ConnectorPathPolicy`. Git connector settings also bound executable path, command timeout, max files, and max text file size.

### Document processing pipeline

Ingestion (`POST .../documents/{documentId}/ingest`) streams a multipart upload, writes the raw artifact to workspace-scoped local storage (`IObjectStorage`, configurable under `Storage:Local:RootPath`, size/media-type policy under `Storage:MaxFileSizeBytes`/`Storage:AllowedMediaTypes`), and enqueues a `ProcessingJob`. `DocumentProcessingWorker` dequeues jobs (`InMemoryDocumentProcessingQueue`) and dispatches to format-specific processors in `Processing/` (`TextDocumentProcessor`, `PdfDocumentProcessor` via PdfPig, `DocxDocumentProcessor`/`XlsxDocumentProcessor`/`PptxDocumentProcessor` via DocumentFormat.OpenXml, `OcrDocumentProcessor`) which normalize content into ordered `DocumentSection`s. Processing states flow `Pending` → `Processing` → `Processed`/`Failed`; failed versions can be retried in place via the reprocess endpoint without creating a new document version. Retry/lease behavior is governed by `Processing:MaxAttempts`, `Processing:RetryDelaySeconds`, `Processing:LeaseMinutes`, `Processing:PollMilliseconds`.

Semantic extraction (`SemanticExtractionWorker`, `DeterministicSemanticExtractor`, `SemanticEmbeddingService`/`DeterministicEmbeddingProvider`) and grounded answer generation (`ConfiguredGroundedAnswerGenerator`, `HttpLlmGenerationProvider`, `DeterministicGroundedIntelligence`) run downstream of processing. The `Llm:Provider` setting (`appsettings.json`) selects between a deterministic (test/offline) provider and an HTTP-backed LLM provider.

### Authentication

JWT bearer auth (`Jwt:Issuer`/`Jwt:Audience`/`Jwt:Secret`/`Jwt:AccessTokenExpirationMinutes`/`Jwt:RefreshTokenExpirationDays`), implemented in `Infrastructure/Identity/Jwt` and `Identity/Passwords`. Login is case-insensitive on email. `Identity/CurrentUser` exposes the authenticated user to the Application layer without a direct dependency on ASP.NET Core.
