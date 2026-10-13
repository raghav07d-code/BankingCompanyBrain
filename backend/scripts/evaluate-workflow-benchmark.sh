#!/usr/bin/env bash
#
# Banking Workflow Intelligence Benchmark.
#
# Deterministic, quantitative evaluation of the semantic intelligence engine
# against the synthetic Loan Approval fixture, scored against a hand-authored
# ground-truth manifest (benchmarks/loan-approval/ground-truth-manifest.json)
# that is NEVER derived from engine output. Drives the real pipeline end to
# end (Git connector -> snapshot -> reconciliation -> Code/API Intelligence ->
# SQL Lineage -> Data Access Intelligence -> embeddings/graph -> semantic
# query layer) exactly like the existing evaluation scripts, and additionally
# scores:
#   - relationship precision/recall/F1 and entity/relationship coverage
#   - evidence correctness (source file/line, document-version currentness)
#   - grounding (fabricated/unsupported relationship count)
#   - impact-analysis precision/recall
#   - temporal correctness (current vs. historical after add/modify/delete)
#   - unresolved-reference handling
#   - workspace isolation and repeated-run (determinism) reliability
#   - query latency, ONLY if API_LOG_FILE is supplied and parseable (never
#     fabricated otherwise - reported as unsupported in this run instead)
#   - a provider-independent A/B/C comparison: (A) chunk/vector retrieval
#     only, (B) graph/BFS traversal only, (C) the full claims+evidence+graph
#     grounded-intelligence pipeline - all three built from existing
#     endpoints, no new pipeline code.
#
# No LLM-based judging anywhere in this script: every check is a deterministic
# assertion against Postgres rows or JSON fields returned by existing APIs.
#
# Usage:
#   API_BASE=http://localhost:5207 DB_NAME=BankingBrainPerfDb DB_USER=raghavendra \
#     ./scripts/evaluate-workflow-benchmark.sh
#
# Optional:
#   API_LOG_FILE=/path/to/api.log   - enables real latency-percentile scoring
#   WAIT_TIMEOUT_SECONDS=180        - per-stage wait ceiling
#
# Exit code is 0 only if every deterministic check passed. The results JSON
# and human-readable summary are always written regardless of exit code.

set -o pipefail

API_BASE="${API_BASE:-http://localhost:5207}"
REPOS_ROOT="${REPOS_ROOT:-src/BankingBrain.Api/repositories}"
DB_NAME="${DB_NAME:-BankingBrainPerfDb}"
DB_USER="${DB_USER:-$(whoami)}"
FIXTURE_NAME="workflow-benchmark-fixture"
# SqlLineageExtractionService derives each SQL script's default database name
# from Slug(knowledgeSource.Name) (lowercase, non-alphanumeric -> '-') rather
# than a literal "default" - so every SqlTable/SqlView/SqlProcedure canonical
# name is "{slug}.{schema}.{name}". FIXTURE_NAME is already lowercase/hyphen-
# only, so Slug(FIXTURE_NAME) == FIXTURE_NAME; this is exact, not a guess.
SQL_DB_PREFIX="$FIXTURE_NAME"
FIXTURE_DIR="$REPOS_ROOT/$FIXTURE_NAME"
CONNECTOR_EXTERNAL_ID="repositories/$FIXTURE_NAME"
# 300s: Code Intelligence's first roslyn-csharp run after a fresh knowledge source has
# been observed, directly against a freshly started API process with zero backlog, to
# take anywhere from ~130s to over 600s with CPU usage staying near-idle the whole time -
# i.e. this is not bounded, compute-bound "cold JIT" work this script can simply out-wait
# with a bigger number. Raising the timeout further only delays when this script gives up
# and moves on; it does not make the run finish. So this budget is a deliberate, moderate
# cutoff (not a measured worst-case ceiling): when it is exceeded, the script WARNs,
# proceeds anyway, and the resulting gaps in Code-Intelligence-dependent metrics are
# reported as a reliability finding - see the benchmark report's "Code Intelligence
# extraction latency variability" limitation. Comparing >=2 runs is how this benchmark
# surfaces that variability rather than hiding it behind an ever-larger timeout.
WAIT_TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-300}"
API_LOG_FILE="${API_LOG_FILE:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$BACKEND_ROOT"

MANIFEST="benchmarks/loan-approval/ground-truth-manifest.json"
FIXTURE_SRC="benchmarks/loan-approval/fixture-src"
RESULTS_DIR="benchmarks/loan-approval/results"
mkdir -p "$RESULTS_DIR"
RUN_ID="$(date +%Y%m%dT%H%M%S)"
RESULTS_JSON="$RESULTS_DIR/run-$RUN_ID.json"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

PASS_COUNT=0
FAIL_COUNT=0
declare -a CAPABILITIES_VERIFIED=()
declare -a MISSED_SYMBOLS=()
declare -a INCORRECT_RELATIONSHIPS=()
declare -a UNRESOLVED_REFERENCES=()
declare -a CURRENTNESS_ISSUES=()
declare -a PROVENANCE_ISSUES=()
declare -a RETRIEVAL_ISSUES=()
declare -a UNSUPPORTED_METRICS=()

pass() { CAPABILITIES_VERIFIED+=("$1"); PASS_COUNT=$((PASS_COUNT + 1)); echo "  [PASS] $1"; }
fail() { local bucket_var="$1"; local msg="$2"; eval "$bucket_var+=(\"\$msg\")"; FAIL_COUNT=$((FAIL_COUNT + 1)); echo "  [FAIL] $msg"; }
skip() { UNSUPPORTED_METRICS+=("$1"); echo "  [SKIP] $1"; }

psql_scalar() {
    psql -U "$DB_USER" -d "$DB_NAME" -tAc "$1" 2>/dev/null | head -1 | tr -d '[:space:]'
}

sql_escape() { echo "${1//\'/\'\'}"; }

require() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "Missing required tool: $1" >&2
        exit 1
    fi
}

require curl
require jq
require psql
require git
require bc

echo "=== 0. Preconditions ==="

if ! curl -s -o /dev/null -w '' --max-time 5 "$API_BASE/api/v1/auth/login"; then
    echo "BankingBrain.Api is not reachable at $API_BASE." >&2
    exit 1
fi
echo "API reachable at $API_BASE."

if ! psql -U "$DB_USER" -d "$DB_NAME" -tAc "SELECT 1" >/dev/null 2>&1; then
    echo "Cannot reach Postgres database '$DB_NAME' as user '$DB_USER'." >&2
    exit 1
fi
echo "Database reachable."

if [ ! -f "$MANIFEST" ]; then
    echo "Ground-truth manifest not found at $MANIFEST." >&2
    exit 1
fi
jq empty "$MANIFEST" || { echo "Ground-truth manifest is not valid JSON." >&2; exit 1; }
echo "Ground-truth manifest loaded ($MANIFEST)."

ADMIN_EMAIL=$(jq -r '.Seed.AdminEmail' src/BankingBrain.Api/appsettings.Development.json)
ADMIN_PASSWORD=$(jq -r '.Seed.AdminPassword' src/BankingBrain.Api/appsettings.Development.json)

echo ""
echo "=== 1. Fixture setup (synthetic Loan Approval workflow, v1 baseline) ==="

rm -rf "$FIXTURE_DIR"
mkdir -p "$FIXTURE_DIR"
cp "$FIXTURE_SRC"/*.cs "$FIXTURE_DIR/"
cp "$FIXTURE_SRC"/*.sql "$FIXTURE_DIR/"
cp "$FIXTURE_SRC"/*.csproj "$FIXTURE_DIR/"

git -C "$FIXTURE_DIR" init -q 2>/dev/null
git -C "$FIXTURE_DIR" config user.email "eval@workflowbenchmark.local"
git -C "$FIXTURE_DIR" config user.name "Workflow Benchmark"
git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "v1 baseline: synthetic loan approval workflow" >/dev/null
echo "Fixture ready, committed at $(git -C "$FIXTURE_DIR" rev-parse --short HEAD)."

echo ""
echo "=== 2. Auth + workspace/knowledge-source setup ==="

TOKEN=$(curl -s -X POST "$API_BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r '.accessToken')
if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then
    echo "Login failed." >&2
    exit 1
fi
AUTH=(-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

WS_NAME="Workflow Benchmark $(date +%s)"
WORKSPACE_ID=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" \
    -d "{\"name\":\"$WS_NAME\",\"description\":\"workflow intelligence benchmark\"}" | jq -r '.workspaceId')
KS_ID=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"description\":\"benchmark fixture\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" \
    | jq -r '.knowledgeSourceId')

if [ -z "$WORKSPACE_ID" ] || [ "$WORKSPACE_ID" = "null" ] || [ -z "$KS_ID" ] || [ "$KS_ID" = "null" ]; then
    echo "Workspace/knowledge-source setup failed." >&2
    exit 1
fi
echo "Workspace $WORKSPACE_ID, knowledge source $KS_ID."

wait_for_refresh_run() {
    local ws_id="$1" ks_id="$2" extractor="$3" expected_revision="$4"
    local elapsed=0
    while [ "$elapsed" -lt "$WAIT_TIMEOUT_SECONDS" ]; do
        local response status revision
        response=$(curl -s -G "$API_BASE/api/v1/workspaces/$ws_id/knowledge-sources/$ks_id/refresh-runs/latest" \
            --data-urlencode "extractorIdentity=$extractor" "${AUTH[@]}")
        status=$(echo "$response" | jq -r '.status // empty')
        revision=$(echo "$response" | jq -r '.revision // empty' | tr -d '[:space:]')
        if [ "$revision" = "$expected_revision" ]; then
            case "$status" in
                Completed) return 0 ;;
                Failed|Superseded) return 1 ;;
            esac
        fi
        sleep 2
        elapsed=$((elapsed + 2))
    done
    return 1
}

# SQL Lineage is queued (async) only after the .sql DocumentVersion has been
# processed by DocumentProcessingWorker (DocumentProcessingState.Processed=3) -
# it reads SQL text from the already-persisted DocumentSection, not the live
# working copy. Waiting for that state, then calling the SQL Lineage and Data
# Access Intelligence extract endpoints explicitly (both are synchronous
# request handlers, not queued background work) guarantees Data Access
# Intelligence's by-bare-table-name match against already-persisted SqlTable
# entities (see DataAccessIntelligenceExtractionService.cs) has something to
# match against.
wait_for_sql_processed() {
    local ws_id="$1"
    local elapsed=0
    while [ "$elapsed" -lt "$WAIT_TIMEOUT_SECONDS" ]; do
        local processed
        processed=$(psql_scalar "
            SELECT count(*) FROM \"DocumentVersions\" dv
            JOIN \"Documents\" d ON d.\"Id\"=dv.\"DocumentId\"
            WHERE dv.\"WorkspaceId\"='$ws_id' AND d.\"MimeType\"='application/sql'
              AND dv.\"ProcessingState\"=3 AND dv.\"Id\"=d.\"CurrentVersionId\";")
        if [ "${processed:-0}" -ge 1 ]; then return 0; fi
        sleep 2
        elapsed=$((elapsed + 2))
    done
    return 1
}

ingest_revision() {
    local ws_id="$1" ks_id="$2" label="$3"
    local snapshot_json snapshot_http revision
    snapshot_json="$TMP_DIR/snapshot_${ws_id}_${RANDOM}.json"
    snapshot_http=$(curl -s -o "$snapshot_json" -w '%{http_code}' \
        -X POST "$API_BASE/api/v1/workspaces/$ws_id/knowledge-sources/$ks_id/snapshot" "${AUTH[@]}" -d '{}')
    if [ "$snapshot_http" != "200" ]; then
        echo "  snapshot ($label) failed with HTTP $snapshot_http" >&2
        cat "$snapshot_json" >&2
        return 1
    fi
    revision=$(jq -r '.revision // empty' "$snapshot_json" | tr -d '[:space:]')
    # Written to stderr, not stdout: this function's stdout is captured via $(...) by
    # callers to obtain the revision string, and a stray stdout line here would silently
    # corrupt that captured value.
    wait_for_refresh_run "$ws_id" "$ks_id" "roslyn-csharp" "$revision" || echo "  [WARN] roslyn-csharp refresh did not settle for $label" >&2
    wait_for_refresh_run "$ws_id" "$ks_id" "api-intelligence" "$revision" || echo "  [WARN] api-intelligence refresh did not settle for $label" >&2
    wait_for_sql_processed "$ws_id" || echo "  [WARN] .sql document did not reach Processed state for $label" >&2
    curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$ws_id/knowledge-sources/$ks_id/sql-lineage/extract" "${AUTH[@]}"
    curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$ws_id/knowledge-sources/$ks_id/data-access-intelligence/extract" "${AUTH[@]}"
    echo "$revision"
}

echo ""
echo "=== 3. Ingest v1 baseline through the real pipeline ==="
V1_REVISION=$(ingest_revision "$WORKSPACE_ID" "$KS_ID" "v1 baseline")
echo "v1 baseline ingested at revision $V1_REVISION."

echo ""
echo "=== 4. Entity and relationship coverage (ground-truth manifest) ==="

ENTITY_TOTAL=$(jq -r '.entities | length' "$MANIFEST")
ENTITY_FOUND=0
declare -a ENTITY_LIST_SQL_PARTS=()
while IFS= read -r ent; do
    etype=$(echo "$ent" | jq -r '.entityType')
    ename=$(echo "$ent" | jq -r '.canonicalName')
    case "$etype" in
        SqlTable|SqlView|SqlProcedure) ename="$SQL_DB_PREFIX.$ename" ;;
    esac
    esc=$(sql_escape "$ename")
    ENTITY_LIST_SQL_PARTS+=("'$esc'")
    cnt=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='$etype' AND \"CanonicalName\"='$esc';")
    if [ "${cnt:-0}" -ge 1 ]; then
        ENTITY_FOUND=$((ENTITY_FOUND + 1))
        pass "entity discovered: $etype $ename"
    else
        fail MISSED_SYMBOLS "entity not found: $etype '$ename'"
    fi
done < <(jq -c '.entities[]' "$MANIFEST")
ENTITY_LIST_SQL=$(IFS=,; echo "${ENTITY_LIST_SQL_PARTS[*]}")
ENTITY_COVERAGE=$(echo "scale=4; $ENTITY_FOUND / $ENTITY_TOTAL" | bc)
echo "Entity coverage: $ENTITY_FOUND/$ENTITY_TOTAL ($ENTITY_COVERAGE)"

echo ""
echo "=== 5. Relationship precision/recall/F1 (ground-truth manifest) ==="

REL_TOTAL=$(jq -r '.relationships | length' "$MANIFEST")
REL_TP=0
REL_TP_RESOLVED=0
REL_FN=0
declare -a REL_RESULTS=()

# Shared by sections 5 and 6, so evidence-correctness checking (section 6) always
# matches the SAME specific relationship tuple (subject+predicate+object) that the
# existence check (section 5) matched - not just "any relationship from this subject",
# which would let evidence from an unrelated relationship on the same entity produce a
# false PASS.
rel_filters() {
    local pclass="$1" pred="$2" obj_like="$3"
    PRED_FILTER=""
    case "$pclass" in
        CodeCallChain) PRED_FILTER="AND sr.\"RelationType\" IN ('CALLS','METHOD_DEPENDS_ON_TYPE')" ;;
        MethodAccessesTable) PRED_FILTER="AND sr.\"RelationType\"='METHOD_ACCESSES_TABLE'" ;;
        UnresolvedReference) PRED_FILTER="AND sr.\"RelationType\"='UNRESOLVED_REFERENCE'" ;;
        SqlDependency) PRED_FILTER="AND sr.\"RelationType\"='$pred'" ;;
    esac
    OBJ_FILTER=""
    if [ -n "$obj_like" ] && [ "$obj_like" != "null" ]; then
        local obj_esc
        obj_esc=$(sql_escape "$obj_like")
        OBJ_FILTER="AND obj.\"CanonicalName\" LIKE '$obj_esc'"
    fi
}

while IFS= read -r rel; do
    id=$(echo "$rel" | jq -r '.id')
    pclass=$(echo "$rel" | jq -r '.predicate_class')
    subj_like=$(echo "$rel" | jq -r '.subjectCanonicalNameLike')
    obj_like=$(echo "$rel" | jq -r '.objectCanonicalNameLike // empty')
    pred=$(echo "$rel" | jq -r '.predicate // empty')
    subj_esc=$(sql_escape "$subj_like")
    rel_filters "$pclass" "$pred" "$obj_like"
    cnt=$(psql_scalar "
        SELECT count(*) FROM \"SemanticRelationships\" sr
        JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
        LEFT JOIN \"SemanticEntities\" obj ON obj.\"Id\"=sr.\"ObjectEntityId\"
        WHERE sr.\"WorkspaceId\"='$WORKSPACE_ID' AND subj.\"CanonicalName\" LIKE '$subj_esc' $PRED_FILTER $OBJ_FILTER;")
    if [ "${cnt:-0}" -ge 1 ]; then
        REL_TP=$((REL_TP + 1))
        [ "$pclass" != "UnresolvedReference" ] && REL_TP_RESOLVED=$((REL_TP_RESOLVED + 1))
        pass "relationship found: $id"
    else
        REL_FN=$((REL_FN + 1))
        fail INCORRECT_RELATIONSHIPS "relationship missing: $id"
    fi
    REL_RESULTS+=("{\"id\":\"$id\",\"found\":$([ "${cnt:-0}" -ge 1 ] && echo true || echo false)}")
done < <(jq -c '.relationships[]' "$MANIFEST")

# Grounding / fabrication: count relationships among known fixture entities
# (both ends resolved to a manifest canonical name, excluding UNRESOLVED_REFERENCE)
# that are NOT accounted for by any matched manifest relationship. Definition:
# FP = max(0, total_resolved_relationships_among_known_entities - TP_resolved).
# This is an approximation (it compares set sizes, not exact tuple identity),
# documented as such in the report - but on this small, fully-enumerated
# fixture it is a meaningful fabrication signal: any nonzero FP here means the
# engine asserted a cross-layer dependency between two known fixture symbols
# that the manifest's independent reading of the source does not expect.
TOTAL_RESOLVED_AMONG_KNOWN=$(psql_scalar "
    SELECT count(*) FROM \"SemanticRelationships\" sr
    JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
    JOIN \"SemanticEntities\" obj ON obj.\"Id\"=sr.\"ObjectEntityId\"
    WHERE sr.\"WorkspaceId\"='$WORKSPACE_ID'
      AND subj.\"CanonicalName\" IN ($ENTITY_LIST_SQL)
      AND obj.\"CanonicalName\" IN ($ENTITY_LIST_SQL)
      AND sr.\"RelationType\" != 'UNRESOLVED_REFERENCE';")
REL_FP=$(( TOTAL_RESOLVED_AMONG_KNOWN - REL_TP_RESOLVED ))
if [ "$REL_FP" -lt 0 ]; then REL_FP=0; fi
if [ "$REL_FP" -eq 0 ]; then
    pass "grounding: 0 fabricated/unsupported relationships among known fixture entities"
else
    fail INCORRECT_RELATIONSHIPS "grounding: $REL_FP relationship(s) among known fixture entities not accounted for by the manifest"
fi

if [ "$((REL_TP + REL_FP))" -gt 0 ]; then
    REL_PRECISION=$(echo "scale=4; $REL_TP / ($REL_TP + $REL_FP)" | bc)
else
    REL_PRECISION="undefined"
fi
if [ "$((REL_TP + REL_FN))" -gt 0 ]; then
    REL_RECALL=$(echo "scale=4; $REL_TP / ($REL_TP + $REL_FN)" | bc)
else
    REL_RECALL="undefined"
fi
if [ "$REL_PRECISION" != "undefined" ] && [ "$REL_RECALL" != "undefined" ] && \
   [ "$(echo "$REL_PRECISION + $REL_RECALL > 0" | bc)" = "1" ]; then
    REL_F1=$(echo "scale=4; 2 * $REL_PRECISION * $REL_RECALL / ($REL_PRECISION + $REL_RECALL)" | bc)
else
    REL_F1="undefined"
fi
echo "Relationship precision=$REL_PRECISION recall=$REL_RECALL f1=$REL_F1 (TP=$REL_TP FP=$REL_FP FN=$REL_FN, total manifest=$REL_TOTAL)"

echo ""
echo "=== 6. Evidence correctness (source file, line range, document-version currentness) ==="

EVIDENCE_CHECKED=0
EVIDENCE_CORRECT=0
while IFS= read -r rel; do
    id=$(echo "$rel" | jq -r '.id')
    pclass=$(echo "$rel" | jq -r '.predicate_class')
    subj_like=$(echo "$rel" | jq -r '.subjectCanonicalNameLike')
    obj_like=$(echo "$rel" | jq -r '.objectCanonicalNameLike // empty')
    pred=$(echo "$rel" | jq -r '.predicate // empty')
    ev_file=$(echo "$rel" | jq -r '.evidenceFile')
    subj_esc=$(sql_escape "$subj_like")
    rel_filters "$pclass" "$pred" "$obj_like"
    EVIDENCE_CHECKED=$((EVIDENCE_CHECKED + 1))
    result=$(psql_scalar "
        SELECT se.\"SourceLocation\" || '|' || dv.\"Id\" || '|' || d.\"CurrentVersionId\"
        FROM \"SemanticEvidence\" se
        JOIN \"SemanticRelationships\" sr ON sr.\"EvidenceId\"=se.\"Id\"
        JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
        LEFT JOIN \"SemanticEntities\" obj ON obj.\"Id\"=sr.\"ObjectEntityId\"
        JOIN \"DocumentVersions\" dv ON dv.\"Id\"=se.\"DocumentVersionId\"
        JOIN \"Documents\" d ON d.\"Id\"=dv.\"DocumentId\"
        WHERE se.\"WorkspaceId\"='$WORKSPACE_ID' AND subj.\"CanonicalName\" LIKE '$subj_esc' $PRED_FILTER $OBJ_FILTER
        LIMIT 1;")
    source_location="${result%%|*}"
    rest="${result#*|}"
    doc_version_id="${rest%%|*}"
    current_version_id="${rest#*|}"
    if [[ "$source_location" == *"$ev_file"* ]] && [[ "$source_location" == *":"* ]] && [ "$doc_version_id" = "$current_version_id" ]; then
        EVIDENCE_CORRECT=$((EVIDENCE_CORRECT + 1))
        pass "evidence correct: $id (location=$source_location, current version)"
    else
        fail PROVENANCE_ISSUES "evidence incorrect for $id: location='$source_location' expected to reference '$ev_file' with a line range and a current DocumentVersionId (got version=$doc_version_id vs current=$current_version_id)"
    fi
done < <(jq -c '.relationships[]' "$MANIFEST")
EVIDENCE_ACCURACY=$(echo "scale=4; $EVIDENCE_CORRECT / $EVIDENCE_CHECKED" | bc)
echo "Evidence correctness: $EVIDENCE_CORRECT/$EVIDENCE_CHECKED ($EVIDENCE_ACCURACY)"

echo ""
echo "=== 7. Impact-analysis precision/recall ==="

find_entity_id() {
    local entity_type="$1" canonical_name="$2"
    curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/code/entities" \
        --data-urlencode "entityType=$entity_type" --data-urlencode "canonicalName=$canonical_name" "${AUTH[@]}" \
        | jq -r '.entityId // empty'
}

score_impact() {
    local label="$1" endpoint_entities_json="$2" expected_names_csv="$3"
    local found_count=0 expected_count total_returned=0
    expected_count=$(echo "$expected_names_csv" | tr ',' '\n' | grep -c . || true)
    while IFS= read -r expected; do
        [ -z "$expected" ] && continue
        if echo "$endpoint_entities_json" | jq -e --arg n "$expected" '.[] | select(.entity.canonicalName == $n or (.entity.canonicalName // "" | contains($n)))' >/dev/null 2>&1; then
            found_count=$((found_count + 1))
        fi
    done < <(echo "$expected_names_csv" | tr ',' '\n')
    total_returned=$(echo "$endpoint_entities_json" | jq -r 'length')
    echo "$found_count|$expected_count|$total_returned"
}

# --- 7a. Reachable code path from the approve endpoint ---
APPROVE_ENDPOINT_ID=$(find_entity_id "ApiEndpoint" "POST /api/loans/{id}/approve")
if [ -n "$APPROVE_ENDPOINT_ID" ] && [ "$APPROVE_ENDPOINT_ID" != "null" ]; then
    REACHABLE=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/api/entities/$APPROVE_ENDPOINT_ID/reachable" \
        --data-urlencode "maxDepth=5" "${AUTH[@]}")
    EXPECTED_REACHABLE=$(jq -r '.impactAnalysis.approvalCodePath.expectedReachableCanonicalNamesLike | join(",")' "$MANIFEST")
    reach_found=0
    reach_expected=$(echo "$EXPECTED_REACHABLE" | tr ',' '\n' | grep -c . || true)
    while IFS= read -r pat; do
        [ -z "$pat" ] && continue
        clean_pat="${pat//%/}"
        if echo "$REACHABLE" | jq -e --arg n "$clean_pat" '.[] | select((.entity.canonicalName // "") | contains($n))' >/dev/null 2>&1; then
            reach_found=$((reach_found + 1))
        fi
    done < <(echo "$EXPECTED_REACHABLE" | tr ',' '\n')
    if [ "$reach_found" -eq "$reach_expected" ]; then
        pass "impact: approval code path reachable ($reach_found/$reach_expected expected nodes found)"
    else
        fail RETRIEVAL_ISSUES "impact: approval code path reachable only $reach_found/$reach_expected expected nodes"
    fi
    IMPACT_CODEPATH_RECALL=$(echo "scale=4; $reach_found / $reach_expected" | bc)
else
    fail RETRIEVAL_ISSUES "impact: could not resolve approve endpoint entity id"
    IMPACT_CODEPATH_RECALL="undefined"
fi

# --- 7b. Endpoints affected by approval-limit method ---
POLICY_METHOD_ID=$(find_entity_id "Method" "LoanApprovalBenchmark.LoanApprovalPolicy.IsWithinApprovalLimit(decimal)")
if [ -z "$POLICY_METHOD_ID" ] || [ "$POLICY_METHOD_ID" = "null" ]; then
    POLICY_METHOD_ID=$(psql_scalar "SELECT \"Id\" FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Method' AND \"CanonicalName\" LIKE '%LoanApprovalPolicy.IsWithinApprovalLimit%' LIMIT 1;")
fi
if [ -n "$POLICY_METHOD_ID" ] && [ "$POLICY_METHOD_ID" != "null" ]; then
    AFFECTED=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/api/entities/$POLICY_METHOD_ID/affected-endpoints" \
        --data-urlencode "maxDepth=5" "${AUTH[@]}")
    AFFECTED_ROUTES=$(echo "$AFFECTED" | jq -r '[.[].entity.canonicalName] | join(",")')
    EXPECTED_AFFECTED="POST /api/loans/{id}/approve"
    EXCLUDED_AFFECTED=$(jq -r '.impactAnalysis.approvalLimitChange.excludedEndpointRoutes | join(",")' "$MANIFEST")
    correctly_included=0
    [[ "$AFFECTED_ROUTES" == *"$EXPECTED_AFFECTED"* ]] && correctly_included=1
    incorrectly_included=0
    while IFS= read -r excl; do
        [ -z "$excl" ] && continue
        [[ "$AFFECTED_ROUTES" == *"$excl"* ]] && incorrectly_included=$((incorrectly_included + 1))
    done < <(echo "$EXCLUDED_AFFECTED" | tr ',' '\n')
    if [ "$correctly_included" -eq 1 ] && [ "$incorrectly_included" -eq 0 ]; then
        pass "impact: approval-limit change affects exactly the approve endpoint (returned: $AFFECTED_ROUTES)"
    else
        fail RETRIEVAL_ISSUES "impact: approval-limit affected-endpoints mismatch (returned: $AFFECTED_ROUTES, expected only '$EXPECTED_AFFECTED')"
    fi
    IMPACT_AFFECTED_PRECISION=$(echo "scale=4; $correctly_included / ($correctly_included + $incorrectly_included)" | bc 2>/dev/null || echo undefined)
else
    fail RETRIEVAL_ISSUES "impact: could not resolve LoanApprovalPolicy.IsWithinApprovalLimit method entity id"
    IMPACT_AFFECTED_PRECISION="undefined"
fi

# --- 7c. SQL impact of the LoanApplications table ---
LOANAPPS_TABLE_ID=$(find_entity_id "SqlTable" "$SQL_DB_PREFIX.dbo.LoanApplications")
if [ -n "$LOANAPPS_TABLE_ID" ] && [ "$LOANAPPS_TABLE_ID" != "null" ]; then
    SQL_IMPACT=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/sql/entities/$LOANAPPS_TABLE_ID/impact" \
        --data-urlencode "maxDepth=5" "${AUTH[@]}")
    EXPECTED_SQL_IMPACT=$(jq -r --arg p "$SQL_DB_PREFIX." '.impactAnalysis.loanApplicationsTableChange.expectedImpactedCanonicalNames | map($p + .) | join(",")' "$MANIFEST")
    sql_found=0
    sql_expected=$(echo "$EXPECTED_SQL_IMPACT" | tr ',' '\n' | grep -c . || true)
    while IFS= read -r name; do
        [ -z "$name" ] && continue
        if echo "$SQL_IMPACT" | jq -e --arg n "$name" '.[] | select(.entity.canonicalName == $n)' >/dev/null 2>&1; then
            sql_found=$((sql_found + 1))
        fi
    done < <(echo "$EXPECTED_SQL_IMPACT" | tr ',' '\n')
    sql_total_returned=$(echo "$SQL_IMPACT" | jq -r 'length')
    if [ "$sql_found" -eq "$sql_expected" ]; then
        pass "impact: LoanApplications table change impact ($sql_found/$sql_expected expected entities found, $sql_total_returned total returned)"
    else
        fail RETRIEVAL_ISSUES "impact: LoanApplications table change impact found only $sql_found/$sql_expected expected entities ($sql_total_returned total returned)"
    fi
    IMPACT_SQL_RECALL=$(echo "scale=4; $sql_found / $sql_expected" | bc)
    if [ "$sql_total_returned" -gt 0 ]; then
        IMPACT_SQL_PRECISION=$(echo "scale=4; $sql_found / $sql_total_returned" | bc)
    else
        IMPACT_SQL_PRECISION="undefined"
    fi
else
    fail RETRIEVAL_ISSUES "impact: could not resolve dbo.LoanApplications SqlTable entity id"
    IMPACT_SQL_RECALL="undefined"
    IMPACT_SQL_PRECISION="undefined"
fi

echo ""
echo "=== 8. Unresolved-reference handling ==="

UNRESOLVED_TOTAL=$(jq -r '[.relationships[] | select(.predicate_class == "UnresolvedReference")] | length' "$MANIFEST")
UNRESOLVED_FOUND=0
while IFS= read -r rel; do
    id=$(echo "$rel" | jq -r '.id')
    subj_like=$(echo "$rel" | jq -r '.subjectCanonicalNameLike')
    subj_esc=$(sql_escape "$subj_like")
    cnt=$(psql_scalar "
        SELECT count(*) FROM \"SemanticRelationships\" sr
        JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
        WHERE sr.\"WorkspaceId\"='$WORKSPACE_ID' AND subj.\"CanonicalName\" LIKE '$subj_esc' AND sr.\"RelationType\"='UNRESOLVED_REFERENCE';")
    if [ "${cnt:-0}" -ge 1 ]; then
        UNRESOLVED_FOUND=$((UNRESOLVED_FOUND + 1))
        pass "unresolved reference correctly surfaced: $id"
    else
        fail UNRESOLVED_REFERENCES "expected unresolved reference not surfaced: $id"
    fi
done < <(jq -c '[.relationships[] | select(.predicate_class == "UnresolvedReference")][]' "$MANIFEST")
echo "Unresolved references correctly surfaced: $UNRESOLVED_FOUND/$UNRESOLVED_TOTAL"

echo ""
echo "=== 9. Temporal correctness (add / modify / delete) ==="

cp "$FIXTURE_SRC/LoanApprovalPolicy.cs" "$TMP_DIR/LoanApprovalPolicy.cs.orig"
cat > "$FIXTURE_DIR/LoanBenchmarkMarkerAdded.cs" <<'EOF'
namespace LoanApprovalBenchmark;

public sealed class LoanBenchmarkMarkerAdded
{
    public string Name { get; set; } = string.Empty;
}
EOF
cat > "$FIXTURE_DIR/LoanBenchmarkMarkerToDelete.cs" <<'EOF'
namespace LoanApprovalBenchmark;

public sealed class LoanBenchmarkMarkerToDelete
{
    public string Note { get; set; } = string.Empty;
}
EOF
echo '' >> "$FIXTURE_DIR/LoanApprovalPolicy.cs"
echo 'public sealed class LoanBenchmarkMarkerAddedChanged { public int Value { get; set; } }' >> "$FIXTURE_DIR/LoanApprovalPolicy.cs"

git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "v2: add + modify marker files" >/dev/null
V2_REVISION=$(ingest_revision "$WORKSPACE_ID" "$KS_ID" "v2 add+modify")
echo "v2 ingested at revision $V2_REVISION."

git -C "$FIXTURE_DIR" rm -q "LoanBenchmarkMarkerToDelete.cs" >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "v3: delete marker file" >/dev/null
V3_REVISION=$(ingest_revision "$WORKSPACE_ID" "$KS_ID" "v3 delete")
echo "v3 ingested at revision $V3_REVISION."

ADDED=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Class' AND \"CanonicalName\"='LoanApprovalBenchmark.LoanBenchmarkMarkerAdded';")
if [ "${ADDED:-0}" -ge 1 ]; then
    pass "temporal: added file's entity discovered (LoanBenchmarkMarkerAdded)"
else
    fail CURRENTNESS_ISSUES "temporal: added file's entity LoanBenchmarkMarkerAdded not discovered"
fi

CHANGED=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Class' AND \"CanonicalName\"='LoanApprovalBenchmark.LoanBenchmarkMarkerAddedChanged';")
if [ "${CHANGED:-0}" -ge 1 ]; then
    pass "temporal: modified file's new entity discovered (LoanBenchmarkMarkerAddedChanged)"
else
    fail CURRENTNESS_ISSUES "temporal: modified file's new entity LoanBenchmarkMarkerAddedChanged not discovered"
fi

HISTORICAL=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"CanonicalName\" LIKE '%LoanBenchmarkMarkerToDelete%';")
RESOLVE_HTTP=$(curl -s -o "$TMP_DIR/resolve.json" -w '%{http_code}' \
    -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/code/entities" \
    --data-urlencode "canonicalName=LoanApprovalBenchmark.LoanBenchmarkMarkerToDelete" "${AUTH[@]}")
if [ "${HISTORICAL:-0}" -ge 1 ] && [ "$RESOLVE_HTTP" = "404" ]; then
    pass "temporal: deleted file's entity preserved historically but excluded from current queries (LoanBenchmarkMarkerToDelete)"
else
    fail CURRENTNESS_ISSUES "temporal: deleted entity currentness check failed (historical rows=${HISTORICAL:-0}, resolve HTTP=$RESOLVE_HTTP)"
fi

echo ""
echo "=== 10. Workspace isolation ==="

WORKSPACE_ID_2=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" \
    -d "{\"name\":\"$WS_NAME Isolation\",\"description\":\"isolation check\"}" | jq -r '.workspaceId')
KS_ID_2=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID_2/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"description\":\"isolation fixture\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" \
    | jq -r '.knowledgeSourceId')
WS2_REVISION=$(ingest_revision "$WORKSPACE_ID_2" "$KS_ID_2" "isolation check")

WS1_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID';")
WS2_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_2';")
CROSS_LEAK=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" e1 JOIN \"SemanticEntities\" e2 ON e1.\"Id\"=e2.\"Id\" WHERE e1.\"WorkspaceId\"='$WORKSPACE_ID' AND e2.\"WorkspaceId\"='$WORKSPACE_ID_2';")
if [ "${WS2_ENTITIES:-0}" -gt 0 ] && [ "${CROSS_LEAK:-0}" -eq 0 ]; then
    pass "workspace isolation (ws1=$WS1_ENTITIES entities, ws2=$WS2_ENTITIES entities, 0 shared rows)"
else
    fail RETRIEVAL_ISSUES "workspace isolation check failed (ws2 entities=${WS2_ENTITIES:-0}, cross-workspace shared rows=${CROSS_LEAK:-0})"
fi

echo ""
echo "=== 11. Repeated-run reliability (determinism) ==="

# 11a. Same query against the same data, 3x, must return identical results.
REPEAT_RESULTS=()
for i in 1 2 3; do
    r=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/api/entities/$APPROVE_ENDPOINT_ID/reachable" \
        --data-urlencode "maxDepth=5" "${AUTH[@]}" | jq -S '[.[] | {predicate, canonicalName: .entity.canonicalName}] | sort_by(.canonicalName, .predicate)')
    REPEAT_RESULTS+=("$r")
done
if [ "${REPEAT_RESULTS[0]}" = "${REPEAT_RESULTS[1]}" ] && [ "${REPEAT_RESULTS[1]}" = "${REPEAT_RESULTS[2]}" ]; then
    pass "determinism: repeated reachable-code-path query returned identical results across 3 calls"
else
    fail RETRIEVAL_ISSUES "determinism: repeated reachable-code-path query returned different results across 3 calls"
fi

# 11b. Full-pipeline determinism: independently ingesting the same fixture into
# a third, fresh workspace must produce the same entity/relationship counts as
# ws1 (same source, independent run of the entire ingestion pipeline).
WORKSPACE_ID_3=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" \
    -d "{\"name\":\"$WS_NAME Repeat\",\"description\":\"repeated-run determinism check\"}" | jq -r '.workspaceId')
KS_ID_3=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID_3/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"description\":\"repeated-run fixture\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" \
    | jq -r '.knowledgeSourceId')
ingest_revision "$WORKSPACE_ID_3" "$KS_ID_3" "determinism rerun" >/dev/null

WS3_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_3';")
WS3_RELATIONSHIPS=$(psql_scalar "SELECT count(*) FROM \"SemanticRelationships\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_3';")
WS2_ENTITIES_V1_ONLY="$WS2_ENTITIES"
WS2_RELATIONSHIPS=$(psql_scalar "SELECT count(*) FROM \"SemanticRelationships\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_2';")
if [ "${WS3_ENTITIES:-0}" -eq "${WS2_ENTITIES_V1_ONLY:-0}" ] && [ "${WS3_RELATIONSHIPS:-0}" -eq "${WS2_RELATIONSHIPS:-0}" ]; then
    pass "determinism: independent re-ingestion of the same fixture (v1-only) produced identical entity/relationship counts (entities=$WS3_ENTITIES, relationships=$WS3_RELATIONSHIPS)"
else
    fail RETRIEVAL_ISSUES "determinism: independent re-ingestion produced different counts (ws2[v1-only]: entities=$WS2_ENTITIES_V1_ONLY relationships=$WS2_RELATIONSHIPS; ws3[v1-only]: entities=$WS3_ENTITIES relationships=$WS3_RELATIONSHIPS)"
fi

echo ""
echo "=== 12. Query latency (only if API_LOG_FILE is supplied and readable) ==="

if [ -n "$API_LOG_FILE" ] && [ -r "$API_LOG_FILE" ]; then
    LATENCIES=$(grep "GroundedStageTiming Stage=TotalRequest" "$API_LOG_FILE" | grep -o 'ElapsedMs=[0-9]*' | cut -d= -f2 | sort -n)
    LATENCY_COUNT=$(echo "$LATENCIES" | grep -c . || true)
    if [ "$LATENCY_COUNT" -gt 0 ]; then
        p50_idx=$(( (LATENCY_COUNT * 50 + 99) / 100 )); [ "$p50_idx" -lt 1 ] && p50_idx=1
        p95_idx=$(( (LATENCY_COUNT * 95 + 99) / 100 )); [ "$p95_idx" -gt "$LATENCY_COUNT" ] && p95_idx=$LATENCY_COUNT
        p99_idx=$(( (LATENCY_COUNT * 99 + 99) / 100 )); [ "$p99_idx" -gt "$LATENCY_COUNT" ] && p99_idx=$LATENCY_COUNT
        P50=$(echo "$LATENCIES" | sed -n "${p50_idx}p")
        P95=$(echo "$LATENCIES" | sed -n "${p95_idx}p")
        P99=$(echo "$LATENCIES" | sed -n "${p99_idx}p")
        pass "latency: TotalRequest p50=${P50}ms p95=${P95}ms p99=${P99}ms (n=$LATENCY_COUNT, from API_LOG_FILE)"
    else
        skip "latency: API_LOG_FILE supplied but contained no GroundedStageTiming Stage=TotalRequest lines for this run"
        P50="unsupported"; P95="unsupported"; P99="unsupported"
    fi
else
    skip "latency: API_LOG_FILE not supplied or not readable - query latency percentiles are not measured in this run (never fabricated)"
    P50="unsupported"; P95="unsupported"; P99="unsupported"
fi
skip "cost/provider metrics: unavailable in deterministic-provider mode (Llm:Provider=deterministic carries no token/cost telemetry; only ProviderIdentity/Model are populated by the HTTP-backed provider, which these evaluations deliberately do not use)"

echo ""
echo "=== 13. Comparative evaluation: A (chunk retrieval) vs B (graph/BFS) vs C (claims+evidence+graph) ==="

# Index DOCUMENT_SECTION embeddings for every current document version in this
# workspace, so Approach A has real chunk content to search (not simulated).
DOC_VERSION_IDS=$(psql_scalar "SELECT string_agg(\"CurrentVersionId\"::text, ',') FROM \"Documents\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"IsActive\"=true;")
IFS=',' read -ra VERSION_ID_ARR <<< "$DOC_VERSION_IDS"
for vid in "${VERSION_ID_ARR[@]}"; do
    [ -z "$vid" ] && continue
    curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/embeddings/index" "${AUTH[@]}" \
        -G --data-urlencode "versionId=$vid" -d '{}' 2>/dev/null || true
done

approach_a_hit() {
    local query="$1" expected_file_substring="$2"
    local result
    result=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/embeddings/search" \
        --data-urlencode "query=$query" --data-urlencode "limit=5" --data-urlencode "sourceType=DOCUMENT_SECTION" "${AUTH[@]}")
    echo "$result" | jq -e --arg f "$expected_file_substring" '[.[].metadata["source.location"] // ""] | any(contains($f))' >/dev/null 2>&1 && echo "1|$(echo "$result" | jq -r 'length')" || echo "0|$(echo "$result" | jq -r 'length' 2>/dev/null || echo 0)"
}

approach_b_hit_reachable() {
    local result expected
    result=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/api/entities/$APPROVE_ENDPOINT_ID/reachable" --data-urlencode "maxDepth=5" "${AUTH[@]}")
    echo "$result" | jq -e '.[] | select((.entity.canonicalName // "") | contains("ApproveLoanHandler.Handle"))' >/dev/null 2>&1 && echo "1" || echo "0"
}

approach_c_hit() {
    local question="$1" expected_substring="$2"
    local result status
    result=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/intelligence/answer" "${AUTH[@]}" -d "{\"question\":\"$question\"}")
    status=$(echo "$result" | jq -r '.evidenceStatus // empty')
    has_citation=$(echo "$result" | jq -r '[.citations[].sourceLocation // ""] | any(contains($f))' --arg f "$expected_substring" 2>/dev/null)
    if [ "$status" = "grounded" ] && [ "$has_citation" = "true" ]; then echo "1"; else echo "0"; fi
}

# Q1: Which code path handles loan approval?
a1=$(approach_a_hit "loan approval handler approve" "LoanCommands.cs")
A1_HIT="${a1%%|*}"; A1_RETURNED="${a1#*|}"
B1_HIT=$(approach_b_hit_reachable)
C1_HIT=$(approach_c_hit "Which code path handles loan approval?" "LoanCommands.cs")
echo "Q1 (code path for approval): A=$A1_HIT B=$B1_HIT C=$C1_HIT"

# Q2: Which tables and procedures participate in approval?
SP_APPROVE_ID=$(find_entity_id "SqlProcedure" "$SQL_DB_PREFIX.dbo.sp_ApproveLoanApplication")
B2_HIT=0
if [ -n "$SP_APPROVE_ID" ] && [ "$SP_APPROVE_ID" != "null" ]; then
    PROV=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/code/entities/$SP_APPROVE_ID/provenance" "${AUTH[@]}")
    echo "$PROV" | jq -e '[.[] | (.relationship.objectEntityId // .relationship.subjectEntityId)] | length > 0' >/dev/null 2>&1 && B2_HIT=1
fi
a2=$(approach_a_hit "approval decisions audit history tables procedures" "schema.sql")
A2_HIT="${a2%%|*}"
C2_HIT=$(approach_c_hit "Which tables and procedures participate in loan approval?" "schema.sql")
echo "Q2 (tables/procedures in approval): A=$A2_HIT B=$B2_HIT C=$C2_HIT"

# Q3: Which API endpoints are affected if the loan approval limit changes?
B3_HIT=0
[ "${AFFECTED_ROUTES:-}" ] && [[ "$AFFECTED_ROUTES" == *"POST /api/loans/{id}/approve"* ]] && B3_HIT=1
a3=$(approach_a_hit "approval limit fifty thousand" "LoanApprovalPolicy.cs")
A3_HIT="${a3%%|*}"
C3_HIT=$(approach_c_hit "Which API endpoints are affected if the loan approval limit changes?" "LoanApprovalPolicy.cs")
echo "Q3 (endpoints affected by limit change): A=$A3_HIT B=$B3_HIT C=$C3_HIT"

# Q4: What could be affected if the loan application table changes?
B4_HIT=0
[ "${sql_found:-0}" -gt 0 ] && B4_HIT=1
a4=$(approach_a_hit "loan application table status amount" "schema.sql")
A4_HIT="${a4%%|*}"
C4_HIT=$(approach_c_hit "What could be affected if the loan application table changes?" "schema.sql")
echo "Q4 (affected by LoanApplications change): A=$A4_HIT B=$B4_HIT C=$C4_HIT"

A_SCORE=$((A1_HIT + A2_HIT + A3_HIT + A4_HIT))
B_SCORE=$((B1_HIT + B2_HIT + B3_HIT + B4_HIT))
C_SCORE=$((C1_HIT + C2_HIT + C3_HIT + C4_HIT))
echo "Comparative totals (4 questions): A(chunk retrieval)=$A_SCORE/4  B(graph/BFS)=$B_SCORE/4  C(claims+evidence+graph)=$C_SCORE/4"
pass "comparative evaluation completed for approaches A/B/C across 4 of the 5 required questions"
skip "comparative question 5 ('what source evidence supports each relationship') is scored as the evidence-attachment property of A/B/C's own responses (section 6 and the per-approach citation checks above), not as a 5th independent retrieval target - documented limitation, not a fabricated 5th score"

echo ""
echo "=== Cleanup (filesystem fixture only; DB rows left for inspection) ==="
rm -rf "$FIXTURE_DIR"
echo "Removed $FIXTURE_DIR. Workspaces left in DB: $WORKSPACE_ID, $WORKSPACE_ID_2, $WORKSPACE_ID_3 (named '$WS_NAME'*)."

print_bucket() {
    local title="$1"; shift
    local items=("$@")
    echo "$title: ${#items[@]}"
    for item in "${items[@]}"; do
        echo "  - $item"
    done
}

echo ""
echo "================= BENCHMARK RESULT ================="
echo "Capabilities verified: $PASS_COUNT"
echo ""
print_bucket "Failures / missed symbols" "${MISSED_SYMBOLS[@]}"
echo ""
print_bucket "Incorrect relationships" "${INCORRECT_RELATIONSHIPS[@]}"
echo ""
print_bucket "Unresolved references" "${UNRESOLVED_REFERENCES[@]}"
echo ""
print_bucket "Stale/currentness issues" "${CURRENTNESS_ISSUES[@]}"
echo ""
print_bucket "Provenance issues" "${PROVENANCE_ISSUES[@]}"
echo ""
print_bucket "Retrieval issues" "${RETRIEVAL_ISSUES[@]}"
echo ""
print_bucket "Unsupported metrics (not scored, not fabricated)" "${UNSUPPORTED_METRICS[@]}"
echo "======================================================="
echo "TOTAL: $PASS_COUNT passed, $FAIL_COUNT failed"

jq -n \
    --arg runId "$RUN_ID" \
    --arg apiBase "$API_BASE" \
    --arg dbName "$DB_NAME" \
    --arg workspaceId "$WORKSPACE_ID" \
    --arg pass "$PASS_COUNT" --arg fail "$FAIL_COUNT" \
    --arg entityFound "$ENTITY_FOUND" --arg entityTotal "$ENTITY_TOTAL" --arg entityCoverage "$ENTITY_COVERAGE" \
    --arg relTP "$REL_TP" --arg relFP "$REL_FP" --arg relFN "$REL_FN" --arg relTotal "$REL_TOTAL" \
    --arg relPrecision "$REL_PRECISION" --arg relRecall "$REL_RECALL" --arg relF1 "$REL_F1" \
    --arg evidenceCorrect "$EVIDENCE_CORRECT" --arg evidenceChecked "$EVIDENCE_CHECKED" --arg evidenceAccuracy "$EVIDENCE_ACCURACY" \
    --arg impactCodepathRecall "${IMPACT_CODEPATH_RECALL:-undefined}" \
    --arg impactAffectedPrecision "${IMPACT_AFFECTED_PRECISION:-undefined}" \
    --arg impactSqlRecall "${IMPACT_SQL_RECALL:-undefined}" --arg impactSqlPrecision "${IMPACT_SQL_PRECISION:-undefined}" \
    --arg unresolvedFound "$UNRESOLVED_FOUND" --arg unresolvedTotal "$UNRESOLVED_TOTAL" \
    --arg latencyP50 "${P50:-unsupported}" --arg latencyP95 "${P95:-unsupported}" --arg latencyP99 "${P99:-unsupported}" \
    --arg aScore "$A_SCORE" --arg bScore "$B_SCORE" --arg cScore "$C_SCORE" \
    '{
        runId: $runId, apiBase: $apiBase, dbName: $dbName, workspaceId: $workspaceId,
        summary: { passed: ($pass|tonumber), failed: ($fail|tonumber) },
        entityCoverage: { found: ($entityFound|tonumber), total: ($entityTotal|tonumber), coverage: $entityCoverage },
        relationshipScoring: { truePositives: ($relTP|tonumber), falsePositives: ($relFP|tonumber), falseNegatives: ($relFN|tonumber), manifestTotal: ($relTotal|tonumber), precision: $relPrecision, recall: $relRecall, f1: $relF1 },
        evidenceCorrectness: { correct: ($evidenceCorrect|tonumber), checked: ($evidenceChecked|tonumber), accuracy: $evidenceAccuracy },
        impactAnalysis: { approvalCodePathRecall: $impactCodepathRecall, approvalLimitAffectedEndpointsPrecision: $impactAffectedPrecision, loanApplicationsTableImpactRecall: $impactSqlRecall, loanApplicationsTableImpactPrecision: $impactSqlPrecision },
        unresolvedReferenceHandling: { found: ($unresolvedFound|tonumber), total: ($unresolvedTotal|tonumber) },
        latencyMs: { p50: $latencyP50, p95: $latencyP95, p99: $latencyP99 },
        comparativeEvaluation: { approachA_chunkRetrieval: ($aScore|tonumber), approachB_graphTraversal: ($bScore|tonumber), approachC_claimsEvidenceGraph: ($cScore|tonumber), outOf: 4 }
    }' > "$RESULTS_JSON"

echo ""
echo "Machine-readable results written to $RESULTS_JSON"

[ "$FAIL_COUNT" -eq 0 ]
