# Banking Workflow Intelligence Benchmark — Report

Deterministic, reproducible benchmark of the semantic intelligence engine against the
synthetic Loan Approval workflow fixture. No LLM-based judging anywhere: every metric is
a rule-based assertion against Postgres rows or JSON fields returned by existing APIs.

## Files

- `benchmarks/loan-approval/fixture-src/` — canonical Loan Approval fixture source (API →
  handler → repository → EF Core mapping → SQL schema/view/procedures), copied into a Git
  knowledge source by the benchmark script at run time.
- `benchmarks/loan-approval/ground-truth-manifest.json` — hand-authored expected entities,
  relationships (with evidence file + a grep-matchable anchor line), impact-analysis sets,
  unresolved-reference cases, and the add/modify/delete revision plan. **Written solely by
  reading the fixture source. Never generated from, or adjusted to match, engine output.**
- `scripts/evaluate-workflow-benchmark.sh` — the benchmark itself.
- `benchmarks/loan-approval/results/run-<timestamp>.json` — machine-readable results, one
  file per run.

## How to rerun

```bash
# From backend/, with BankingBrain.Api already running against BankingBrainPerfDb on 5207
API_BASE=http://localhost:5207 DB_NAME=BankingBrainPerfDb DB_USER=raghavendra \
  ./scripts/evaluate-workflow-benchmark.sh
```

Optional: `API_LOG_FILE=/path/to/api.log` enables real latency-percentile scoring (parses
`GroundedStageTiming Stage=TotalRequest` log lines); omitted, that metric is reported as
unsupported, never fabricated. `WAIT_TIMEOUT_SECONDS` (default 300) bounds how long the
script waits for each Code Intelligence/API Intelligence refresh run before proceeding
anyway and recording a WARN (see "Known limitation" below — raising this further does not
make a stuck run finish).

## Metric definitions, denominators, and thresholds

| Metric | Denominator | Empty-result treatment | Threshold / rationale |
|---|---|---|---|
| Entity coverage | 17 manifest entities | n/a (manifest fixed) | Informational; no single engine query combines all layers, so this is a coverage signal, not pass/fail. |
| Relationship precision/recall/F1 | TP+FP / TP+FN / harmonic mean | `"undefined"` if a denominator is 0 (never fabricated as 0 or 1) | No fixed pass bar is asserted globally — each of the 15 manifest relationships is itself pass/fail; precision/recall/F1 are the roll-up. |
| Grounding (fabrication) | `TOTAL_RESOLVED_AMONG_KNOWN - TP_resolved`, floored at 0 | n/a | **0 is the only acceptable value.** This is an approximation (set-size comparison on a fully-enumerated small fixture, not exact tuple matching) and is documented as such — any nonzero result means the engine asserted a dependency between two known fixture symbols the manifest's independent reading does not expect. |
| Evidence correctness | 15 (every manifest relationship) | Relationship missing ⇒ evidence check fails too (by design: evidence correctness is conditional on the relationship existing) | No global bar; each relationship's evidence is pass/fail individually. |
| Impact-analysis recall/precision | manifest expected-set size / engine's returned-set size | `"undefined"` if a denominator is 0 | Same as above — no global bar. |
| Unresolved-reference handling | 2 (manifest's deliberate unresolved cases) | n/a | Both cases are expected to surface `UNRESOLVED_REFERENCE`; this is the explicit "don't invent connections" check. |
| Workspace isolation | — | n/a | **Must be exactly 0 shared entity rows between two independently ingested workspaces.** Non-negotiable (existing scripts use the identical bar). |
| Repeated-run reliability | — | n/a | (a) the same query run 3× against unchanged data must return byte-identical JSON; (b) independently re-ingesting the same fixture into a fresh workspace should produce the same entity/relationship counts, modulo the latency variability documented below. |
| Latency percentiles | count of `GroundedStageTiming Stage=TotalRequest` lines | 0 lines ⇒ reported as unsupported, not as "0ms" | Only scored when `API_LOG_FILE` is supplied and readable, exactly mirroring `evaluate-semantic-intelligence.sh`'s existing convention. |
| Cost/provider metrics | — | — | **Always reported unsupported.** The deterministic LLM provider used by every evaluation in this repository carries no token/cost telemetry; only the HTTP-backed provider populates `ProviderIdentity`/`Model`, and these evaluations deliberately never use it. |

## Observed results (two independent runs, same fixture, same script)

Both runs: build 0 errors/5 pre-existing warnings; Semantic Intelligence 21/21; Code
Intelligence 24/24; isolated against port 5207 / `BankingBrainPerfDb` only.

| | Run A | Run B |
|---|---|---|
| Capabilities verified | 28 | 28 |
| Failures | 32 | 32 |
| Relationship precision/recall/F1 | 1.00 / 0.4666 / 0.6363 | 1.00 / 0.4666 / 0.6363 |
| Evidence correctness | 9/15 → 7/15* | 7/15 |
| Grounding (fabricated relationships) | 0 | 0 |
| Unresolved references surfaced | 1/2 | 1/2 |
| Workspace isolation | PASS (0 shared rows) | PASS (0 shared rows) |
| Determinism (3× same query) | PASS | PASS |
| Determinism (independent re-ingest) | FAIL (43 vs 144 entities) | FAIL (43 vs 37 entities) |
| Comparative B (graph/BFS) score | 3/4 | 1/4 |
| Comparative A (chunk) / C (claims) score | 0/4 / 0/4 | 0/4 / 0/4 |

*An earlier pre-fix run of Run A's content showed 9/15 before a real bug in the evidence
check (described below) was found and fixed; the corrected figure is 7/15, shown above.

**The headline finding: the deterministic correctness score (28 passed / 32 failed, same
15 relationship pass/fail outcomes, same 0 fabricated relationships) was identical across
both independent runs.** The only metrics that varied between runs were ones downstream of
Code Intelligence's variable completion time (the comparative B score, and the determinism
re-ingest entity counts) — this is the latency-variability finding below, not a
correctness regression.

## The 5 required questions

1. **Which code path handles loan approval?** `POST /api/loans/{id}/approve` →
   `ApproveLoanHandler.Handle` → `LoanApprovalPolicy.IsWithinApprovalLimit` →
   `LoanApplicationRepository.UpdateStatus` → `ApprovalDecisionRepository.Add`. Reachable
   via `GetReachableCodePathAsync`, **but only when Code Intelligence has finished
   extracting this knowledge source** (see limitation below) — in both benchmark runs this
   path resolved only partially (1/4 expected nodes) because the Code Intelligence
   extraction for the approve handler's own class had not completed within the wait
   budget. The METHOD_ACCESSES_TABLE hops (independent of Code Intelligence, produced by
   Data Access Intelligence instead) resolved correctly in every run.
2. **Which tables and procedures participate in approval?** `LoanApplications`,
   `ApprovalDecisions` resolve via `METHOD_ACCESSES_TABLE`/`PROCEDURE_WRITES_TABLE`.
   `sp_RecordAuditEntry` and `AuditHistory`'s involvement did **not** resolve in any run —
   see the SQL Lineage defect below.
3. **Which API endpoints are affected if the loan approval limit changes?** Could not be
   fully verified in either run: resolving `LoanApprovalPolicy.IsWithinApprovalLimit` as a
   `Method` entity depends on the same Code Intelligence extraction that did not complete
   in time.
4. **What could be affected if the loan application table changes?** `GetSqlImpactAsync`
   on `dbo.LoanApplications` correctly returned `vw_LoanApplicationSummary` and
   `sp_ApproveLoanApplication` (2/3 expected) in both runs — `sp_RejectLoanApplication` is
   missing because it was never extracted (same SQL Lineage defect).
5. **What source evidence supports each relationship?** Every relationship that *was*
   found carries a `file:line` citation and a `DocumentVersionId` equal to the owning
   document's current version (see Evidence correctness above) — verified, not assumed,
   for every one of the 15 manifest relationships in both runs.

Per the milestone's instruction to "return explicit unresolved references where the
engine cannot establish a relationship, do not invent connections": the two deliberately
unresolvable cases in the fixture (`Notification`'s unmapped `DbSet`, and
`sp_RejectLoanApplication`'s dynamic `EXEC(@var)`) were checked explicitly — the
`Notification` case correctly surfaced `UNRESOLVED_REFERENCE` in every run; the dynamic-SQL
case could not be checked because the procedure containing it was never extracted at all
(see below) — a bigger gap than "unresolved," and reported as such rather than papered
over.

## Comparative evaluation: A (chunk retrieval) vs B (graph/BFS) vs C (claims+evidence+graph)

- **Approach A** (`/semantic/embeddings/search?sourceType=DOCUMENT_SECTION`, pure
  chunk/vector retrieval, no graph or claims) scored 0/4 in both runs. Its retrieval did
  not surface the fixture's own source files for any of the 4 questions tested.
- **Approach B** (direct calls to `GetReachableCodePathAsync` /
  `GetEndpointsAffectedByMethodAsync` / `GetSqlImpactAsync`, i.e. graph/BFS traversal only)
  scored 3/4 in Run A and 1/4 in Run B — the variance tracks Code Intelligence's
  variable completion exactly (see below), not a logic difference between runs.
- **Approach C** (the merged `/semantic/intelligence/answer` grounded-pipeline endpoint,
  representing "claims + evidence + graph") scored 0/4 in both runs on the specific
  evidenceStatus=grounded + citation-match bar used here. Its own internals call the same
  retrieval primitives B and C would use — **this is a documented limitation of the
  comparison, not a finding that C is "worse than B":** B tests the graph primitive
  directly with a loose canonical-name match, while C's score here requires the merged
  pipeline to produce a *fully grounded* answer with a matching citation, a strictly
  harder bar. Both approaches draw on entities that, in these runs, were incompletely
  extracted (see below). This limitation is reported rather than claiming an
  apples-to-apples ranking that the available components don't support.

## Discovered defects / limitations (found during this benchmark, not fixed, per constraints)

### 1. SQL Lineage drops the 3rd `CREATE PROCEDURE` statement in a script, silently

Confirmed via isolated diagnosis (a minimal fixture outside the main benchmark, created
and removed solely for this diagnosis): a script with exactly 2 `CREATE PROCEDURE`
statements parses correctly, including a procedure forward-referencing another via
`EXEC`. Adding a 3rd `CREATE PROCEDURE` — tested both with and without dynamic SQL in its
body — causes that 3rd procedure and everything in its body to never appear as entities or
relationships. No error is recorded: the `SemanticExtractionRun` row reports
`Status=Succeeded` with the **same** `RelationshipCount` as the 2-procedure version, even
after a 30-second wait confirming the file had fully reached `ProcessingState=Processed`
(ruling out a timing race). This is a genuine, reproducible defect in the deterministic
SQL analyzer, discovered by this benchmark's ground-truth comparison — exactly the kind of
gap this benchmark exists to surface. **Not fixed**, per this task's explicit constraint
not to modify the parser; the ground-truth manifest was deliberately left unchanged
(scoring this as a real recall gap) rather than adjusted to match the engine's output.

### 2. Code Intelligence (`roslyn-csharp`) extraction has highly variable first-run latency

Measured directly via `KnowledgeSourceRefreshRuns.{QueuedOnUtc, CompletedOnUtc}` across
many independent measurements, including against a freshly started API process with zero
backlog: the first `roslyn-csharp` extraction for a new knowledge source ranged from ~130s
to over 600s (one direct measurement exceeded 10 minutes with the process still visibly
idle at ~2% CPU). This is not bounded, compute-bound "cold JIT" work a bigger timeout can
simply out-wait — CPU stayed near-idle through the long tail, suggesting an I/O- or
wait-bound cause that this benchmark did not have the tooling (no `dotnet-dump`/managed
profiler available in this environment) to fully isolate. The benchmark script does not
try to conceal this by raising its timeout indefinitely (per explicit review feedback): it
waits a moderate, fixed budget (300s), WARNs, and proceeds — which is exactly why the two
runs above show identical *deterministic correctness* scores despite this variability:
the metrics genuinely dependent on Code Intelligence completing (Class entity discovery,
CALLS/METHOD_DEPENDS_ON_TYPE relationships, parts of the reachable-code-path and
affected-endpoints impact analysis) are the ones that fluctuate; metrics independent of it
(SQL Lineage results, Data Access Intelligence's METHOD_ACCESSES_TABLE, temporal
correctness, workspace isolation, grounding/fabrication) were identical in both runs.

### 3. Script bugs found and fixed during this review (not engine bugs)

- Section 6 (evidence correctness) originally matched evidence from *any* relationship on
  the same subject entity, not the specific subject+predicate+object tuple being checked —
  producing false PASSes for relationships that didn't actually exist. Fixed by sharing a
  single `rel_filters` helper between sections 5 and 6.
- SQL entity canonical names use `{Slug(knowledgeSourceName)}.{schema}.{name}`, not
  `default.{schema}.{name}` as initially assumed — fixed by computing the exact slug the
  benchmark's own knowledge source name produces, rather than guessing.
- An unused `declare -A` (bash associative array) is unsupported on macOS's default bash
  3.2 and was removed (it was dead code, not used anywhere).
- A stray `[WARN]` message was being written to the same stdout stream a caller captured
  via command substitution to obtain a revision string, silently corrupting that value;
  redirected to stderr.

## Regressions

None in production code — this milestone touched only `scripts/evaluate-workflow-benchmark.sh`
and the `benchmarks/loan-approval/` deliverables. Both existing baselines (Semantic
Intelligence 21/21, Code Intelligence 24/24) remained green across every rebuild and every
fresh API instance used in this session.
