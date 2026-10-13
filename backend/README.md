## Run

Requirements: .NET 10 SDK and PostgreSQL running locally.

From `backend/`, set the PostgreSQL connection string through configuration or environment variables, then run:

```bash
dotnet restore
dotnet run --project src/BankingBrain.Api
```

Development startup applies EF Core migrations and seeds the development administrator configured in `src/BankingBrain.Api/appsettings.Development.json`.

## Test

With PostgreSQL available locally:

```bash
dotnet test
```

The integration tests use the real API, JWT authentication, EF Core, and PostgreSQL persistence path.

## Current Authentication And Workspaces

Login is available at `POST /api/v1/auth/login`. Protected user and workspace endpoints require a bearer access token. Workspace creation, listing, and lookup are scoped to the authenticated owner; a workspace belonging to another user is returned as not found. Workspace names are unique per owner, and login email lookup is case-insensitive.

Application errors use a consistent JSON error body and status mapping: unauthorized is `401`, not found is `404`, duplicate workspace names are `409`, and other business or validation errors are `400`.

## Knowledge And Documents

Knowledge enters through workspace-owned knowledge sources. A source can represent a future file, web, email, database, API, or manual origin. Documents are logical artifacts attached to one source and workspace, while document versions preserve content hashes, optional object-storage locations, optional extracted text, processing state, and version number.

Current endpoints:

- `POST` and `GET /api/v1/workspaces/{workspaceId}/knowledge-sources`
- `GET /api/v1/workspaces/{workspaceId}/knowledge-sources/{knowledgeSourceId}`
- `POST` and `GET /api/v1/workspaces/{workspaceId}/documents`
- `GET /api/v1/workspaces/{workspaceId}/documents/{documentId}`

All source and document operations first verify that the authenticated user owns the workspace. Resources from another workspace are returned as `404`, and the database enforces the document-to-source workspace relationship.

## Ingestion Lifecycle

`POST /api/v1/workspaces/{workspaceId}/documents/{documentId}/ingest` accepts a streamed `multipart/form-data` file upload and returns `202 Accepted`. The raw artifact is written under a workspace-scoped local object-storage key while PostgreSQL stores immutable version metadata. SHA-256 is calculated over the exact stored bytes; repeated uploads are idempotent, while changed content creates the next version and updates the document's explicit current-version pointer without changing history.

Processing states are `Pending`, `Processing`, `Processed`, and `Failed`. A background worker normalizes supported text, CSV, JSON, XML, and HTML content into ordered sections. `POST /api/v1/workspaces/{workspaceId}/documents/{documentId}/versions/{versionId}/reprocess` retries a failed version without creating a new source version. Version history and processing attempts are available through the corresponding `GET .../versions` endpoints.

Local storage uses the configurable `Storage:Local:RootPath` directory and `Storage:MaxFileSizeBytes`/`Storage:AllowedMediaTypes` upload policy. It is replaceable through `IObjectStorage`; raw files are never stored in PostgreSQL or overwritten during processing. PostgreSQL-backed processing jobs use row locking and lease expiry for worker recovery; completed and failed jobs remain history while only one pending/processing job is allowed per version.
