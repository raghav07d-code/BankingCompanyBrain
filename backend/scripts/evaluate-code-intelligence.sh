#!/usr/bin/env bash
#
# Lightweight, deterministic evaluation workflow for Code Intelligence.
#
# Drives the REAL pipeline end to end (Git connector -> snapshot ->
# reconciliation -> auto Code Intelligence extraction -> embeddings/graph
# refresh -> semantic query layer -> grounded intelligence) against a REAL
# multi-project .NET repository fixture, and checks the result against known
# facts about that repository's structure. No LLM-based judging: every check
# is a deterministic assertion (a symbol/relationship/row does or doesn't
# exist, a query does or doesn't resolve something).
#
# The fixture is this very backend solution's own `src/` tree (six real
# projects: Domain, Shared, Contracts, Application, Infrastructure, Api),
# copied into the Git connector's configured root and git-initialized. It is
# genuinely real .NET source - interfaces implemented across projects,
# cross-project ProjectReferences, inheritance, method calls, containment -
# not a synthetic toy sample.
#
# Requirements:
#   - BankingBrain.Api must already be running (ASPNETCORE_ENVIRONMENT=Development)
#     with its working directory such that REPOS_ROOT below resolves under its
#     configured Connectors:Git:AllowedRoots ("repositories" by default).
#     The standard way to satisfy this: run
#       dotnet run --project src/BankingBrain.Api
#     from the backend/ directory (this script's default REPOS_ROOT assumes
#     that convention: src/BankingBrain.Api/repositories).
#   - Postgres reachable with the same connection this script uses (defaults
#     match appsettings.json's dev connection string).
#
# Usage:
#   ./scripts/evaluate-code-intelligence.sh
#
# Exit code is 0 only if every check passed.

# Not "set -u": macOS ships bash 3.2, whose handling of `"${empty_array[@]}"`
# under nounset is unreliable and would abort this script on the (common and
# valid) case of a check bucket having zero findings.
set -o pipefail

API_BASE="${API_BASE:-http://localhost:5206}"
REPOS_ROOT="${REPOS_ROOT:-src/BankingBrain.Api/repositories}"
DB_NAME="${DB_NAME:-BankingBrainDb}"
DB_USER="${DB_USER:-$(whoami)}"
FIXTURE_NAME="codeintel-eval-fixture"
FIXTURE_DIR="$REPOS_ROOT/$FIXTURE_NAME"
# externalIdentifier is resolved by the running API process relative to ITS OWN
# working directory against Connectors:Git:AllowedRoots ("repositories" by
# default) - it is the connector-root-relative form of FIXTURE_DIR, not the
# script's own (backend/-relative) filesystem path to it.
CONNECTOR_EXTERNAL_ID="repositories/$FIXTURE_NAME"
WAIT_TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-180}"

PASS_COUNT=0
FAIL_COUNT=0
declare -a CAPABILITIES_VERIFIED=()
declare -a MISSED_SYMBOLS=()
declare -a INCORRECT_RELATIONSHIPS=()
declare -a UNRESOLVED_REFERENCES=()
declare -a CURRENTNESS_ISSUES=()
declare -a PROVENANCE_ISSUES=()
declare -a RETRIEVAL_ISSUES=()

pass() { CAPABILITIES_VERIFIED+=("$1"); PASS_COUNT=$((PASS_COUNT + 1)); echo "  [PASS] $1"; }
fail() { local bucket_var="$1"; local msg="$2"; eval "$bucket_var+=(\"\$msg\")"; FAIL_COUNT=$((FAIL_COUNT + 1)); echo "  [FAIL] $msg"; }

psql_scalar() {
    psql -U "$DB_USER" -d "$DB_NAME" -tAc "$1" 2>/dev/null | head -1 | tr -d '[:space:]'
}

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

echo "=== 0. Preconditions ==="

if ! curl -s -o /dev/null -w '' --max-time 5 "$API_BASE/api/v1/auth/login"; then
    echo "BankingBrain.Api is not reachable at $API_BASE." >&2
    echo "Start it first: (cd backend && dotnet run --project src/BankingBrain.Api)" >&2
    exit 1
fi
echo "API reachable at $API_BASE."

if ! psql -U "$DB_USER" -d "$DB_NAME" -tAc "SELECT 1" >/dev/null 2>&1; then
    echo "Cannot reach Postgres database '$DB_NAME' as user '$DB_USER'." >&2
    exit 1
fi
echo "Database reachable."

ADMIN_EMAIL=$(jq -r '.Seed.AdminEmail' src/BankingBrain.Api/appsettings.Development.json)
ADMIN_PASSWORD=$(jq -r '.Seed.AdminPassword' src/BankingBrain.Api/appsettings.Development.json)

echo ""
echo "=== 1. Fixture setup (real multi-project .NET repository) ==="

mkdir -p "$FIXTURE_DIR"
for project in BankingBrain.Domain BankingBrain.Shared BankingBrain.Contracts BankingBrain.Application BankingBrain.Infrastructure BankingBrain.Api; do
    mkdir -p "$FIXTURE_DIR/$project"
    rsync -a --exclude 'bin' --exclude 'obj' --exclude 'repositories' --exclude 'sources' --exclude 'storage' \
        --exclude 'repository-snapshots' \
        "src/$project/" "$FIXTURE_DIR/$project/" 2>/dev/null \
        || cp -R "src/$project/." "$FIXTURE_DIR/$project/"
    rm -rf "$FIXTURE_DIR/$project/bin" "$FIXTURE_DIR/$project/obj" \
        "$FIXTURE_DIR/$project/repositories" "$FIXTURE_DIR/$project/sources" "$FIXTURE_DIR/$project/storage" \
        "$FIXTURE_DIR/$project/repository-snapshots"
done

# Git reconciliation intentionally skips zero-byte files (nothing to hash/version),
# so the expected Document count excludes them too (this repo genuinely has a
# handful: GlobalUsings.cs files and not-yet-wired-up empty behavior stubs).
FILE_COUNT_EXPECTED=$(find "$FIXTURE_DIR" -name '*.cs' -not -empty | wc -l | tr -d '[:space:]')
PROJECT_COUNT_EXPECTED=6

git -C "$FIXTURE_DIR" init -q 2>/dev/null
git -C "$FIXTURE_DIR" config user.email "eval@codeintel.local"
git -C "$FIXTURE_DIR" config user.name "Code Intelligence Eval"
git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "baseline" --allow-empty >/dev/null
echo "Fixture ready: $PROJECT_COUNT_EXPECTED projects, $FILE_COUNT_EXPECTED .cs files, committed at $(git -C "$FIXTURE_DIR" rev-parse --short HEAD)."

echo ""
echo "=== 2. Auth + workspace/knowledge-source setup ==="

TOKEN=$(curl -s -X POST "$API_BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r '.accessToken')

if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then
    echo "Login failed." >&2
    exit 1
fi
AUTH=(-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

WS_NAME="CodeIntel Eval $(date +%s)"
WORKSPACE_ID=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" \
    -d "{\"name\":\"$WS_NAME\",\"description\":\"code intelligence evaluation\"}" | jq -r '.workspaceId')

KS_ID=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"description\":\"eval fixture\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" \
    | jq -r '.knowledgeSourceId')

if [ -z "$WORKSPACE_ID" ] || [ "$WORKSPACE_ID" = "null" ] || [ -z "$KS_ID" ] || [ "$KS_ID" = "null" ]; then
    echo "Workspace/knowledge-source setup failed." >&2
    exit 1
fi
echo "Workspace $WORKSPACE_ID, knowledge source $KS_ID."

# Waits for the *specific* snapshot/run this script just triggered to reach a terminal
# state, rather than inferring "the pipeline settled" from a global extraction-run count -
# which says nothing about which snapshot it was for, and is trivially satisfied by the
# large baseline batch on every later, much smaller change. Polls
# GET .../knowledge-sources/{ks}/refresh-runs/latest?extractorIdentity=X and only accepts a
# response whose own revision matches the exact revision this script captured from the
# snapshot call it is waiting on.
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

# Retained for the embeddings/graph readiness signal only (Code Intelligence's own
# extraction completion is now checked precisely via wait_for_refresh_run above).
wait_for_pipeline() {
    local expected_min_runs="$1"
    local elapsed=0
    while [ "$elapsed" -lt "$WAIT_TIMEOUT_SECONDS" ]; do
        local runs
        runs=$(psql_scalar "SELECT count(*) FROM \"SemanticExtractionRuns\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"Status\"=3;")
        local nodes
        nodes=$(psql_scalar "SELECT count(*) FROM \"SemanticGraphNodes\" WHERE \"WorkspaceId\"='$WORKSPACE_ID';")
        if [ "${runs:-0}" -ge "$expected_min_runs" ] && [ "${nodes:-0}" -gt 0 ]; then
            return 0
        fi
        sleep 2
        elapsed=$((elapsed + 2))
    done
    return 1
}

echo ""
echo "=== 3. Baseline snapshot -> reconciliation -> extraction -> embeddings/graph ==="

SNAPSHOT_HTTP=$(curl -s -o /tmp/codeintel_eval_snapshot1.json -w '%{http_code}' \
    -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/knowledge-sources/$KS_ID/snapshot" "${AUTH[@]}" -d '{}')

if [ "$SNAPSHOT_HTTP" != "200" ]; then
    echo "Baseline snapshot failed (HTTP $SNAPSHOT_HTTP)." >&2
    cat /tmp/codeintel_eval_snapshot1.json >&2
    exit 1
fi

BASELINE_REVISION=$(jq -r '.revision // empty' /tmp/codeintel_eval_snapshot1.json | tr -d '[:space:]')

if [ -n "$BASELINE_REVISION" ] && wait_for_refresh_run "$WORKSPACE_ID" "$KS_ID" "roslyn-csharp" "$BASELINE_REVISION" && wait_for_pipeline 1; then
    echo "Pipeline settled."
else
    echo "  [WARN] Pipeline did not settle within ${WAIT_TIMEOUT_SECONDS}s; continuing with whatever state exists."
fi

echo ""
echo "=== 4. Deterministic checks against known repository structure ==="

# --- project discovery ---
DOC_PROJECT_COUNT=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Project';")
if [ "${DOC_PROJECT_COUNT:-0}" -eq "$PROJECT_COUNT_EXPECTED" ]; then
    pass "project discovery ($DOC_PROJECT_COUNT/$PROJECT_COUNT_EXPECTED projects found)"
else
    fail MISSED_SYMBOLS "project discovery: expected $PROJECT_COUNT_EXPECTED Project entities, found ${DOC_PROJECT_COUNT:-0}"
fi

# --- source-file discovery (Documents created by reconciliation for .cs files under the fixture) ---
DOC_FILE_COUNT=$(psql_scalar "SELECT count(*) FROM \"Documents\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"IsActive\"=true AND \"ExternalIdentifier\" LIKE '%.cs';")
if [ "${DOC_FILE_COUNT:-0}" -eq "$FILE_COUNT_EXPECTED" ]; then
    pass "source-file discovery ($DOC_FILE_COUNT/$FILE_COUNT_EXPECTED .cs files reconciled)"
else
    fail MISSED_SYMBOLS "source-file discovery: expected $FILE_COUNT_EXPECTED .cs Documents, found ${DOC_FILE_COUNT:-0}"
fi

# --- cross-project references ---
XPROJ=$(psql_scalar "
    SELECT count(*) FROM \"SemanticRelationships\" sr
    JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
    JOIN \"SemanticEntities\" obj ON obj.\"Id\"=sr.\"ObjectEntityId\"
    WHERE sr.\"WorkspaceId\"='$WORKSPACE_ID' AND sr.\"RelationType\"='PROJECT_REFERENCES_PROJECT'
      AND subj.\"CanonicalName\"='BankingBrain.Infrastructure' AND obj.\"CanonicalName\"='BankingBrain.Domain';")
if [ "${XPROJ:-0}" -ge 1 ]; then
    pass "cross-project references (Infrastructure -> Domain PROJECT_REFERENCES_PROJECT found)"
else
    fail UNRESOLVED_REFERENCES "cross-project reference Infrastructure->Domain not found"
fi

# --- class / interface / record / enum discovery (known symbols) ---
check_entity() {
    local entity_type="$1" canonical_name="$2" label="$3"
    local found
    found=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='$entity_type' AND \"CanonicalName\"='$canonical_name';")
    if [ "${found:-0}" -ge 1 ]; then
        pass "$label"
    else
        fail MISSED_SYMBOLS "$label: '$canonical_name' ($entity_type) not found"
    fi
}

check_entity "Class" "BankingBrain.Infrastructure.Connectors.Git.GitSourceConnector" "class discovery"
check_entity "Interface" "BankingBrain.Application.Common.Connectors.ISourceConnector" "interface discovery"
check_entity "Record" "BankingBrain.Application.Common.Connectors.SourceSnapshot" "record discovery"
check_entity "Enum" "BankingBrain.Domain.Enums.KnowledgeSourceType" "enum discovery"

# --- method / property / field discovery ---
# Roslyn's CSharpErrorMessageFormat renders some BCL parameter types (e.g.
# CancellationToken) unqualified even when custom types in the same signature
# are fully qualified, so match on prefix/shape rather than an exact literal
# (which would overspecify Roslyn's own display-format choices, not our code).
METHOD_FOUND=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Method' AND \"CanonicalName\" LIKE 'BankingBrain.Infrastructure.Connectors.Git.GitSourceConnector.SnapshotAsync(%SourceConnectorRequest%CancellationToken)';")
if [ "${METHOD_FOUND:-0}" -ge 1 ]; then
    pass "method discovery"
else
    fail MISSED_SYMBOLS "method discovery: GitSourceConnector.SnapshotAsync(...) not found"
fi
check_entity "Property" "BankingBrain.Domain.Entities.Document.Document.CurrentVersionId" "property discovery"

# --- inheritance / implementation ---
INHERITS=$(psql_scalar "
    SELECT count(*) FROM \"SemanticRelationships\" sr
    JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
    JOIN \"SemanticEntities\" obj ON obj.\"Id\"=sr.\"ObjectEntityId\"
    WHERE sr.\"WorkspaceId\"='$WORKSPACE_ID' AND sr.\"RelationType\"='INHERITS_OR_IMPLEMENTS'
      AND subj.\"CanonicalName\"='BankingBrain.Infrastructure.Connectors.Git.GitSourceConnector'
      AND obj.\"CanonicalName\"='BankingBrain.Application.Common.Connectors.ISourceConnector';")
if [ "${INHERITS:-0}" -ge 1 ]; then
    pass "interface implementation (GitSourceConnector implements ISourceConnector)"
else
    fail INCORRECT_RELATIONSHIPS "GitSourceConnector : ISourceConnector implementation relationship not found"
fi

# --- containment relationships ---
for predicate in NAMESPACE_CONTAINS_TYPE TYPE_CONTAINS_METHOD TYPE_CONTAINS_PROPERTY PROJECT_CONTAINS_NAMESPACE REPOSITORY_CONTAINS_PROJECT; do
    count=$(psql_scalar "SELECT count(*) FROM \"SemanticRelationships\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"RelationType\"='$predicate';")
    if [ "${count:-0}" -ge 1 ]; then
        pass "containment relationship $predicate present ($count)"
    else
        fail INCORRECT_RELATIONSHIPS "no $predicate relationships found"
    fi
done

# --- type dependencies / method dependencies / method calls (structural, count-based) ---
for predicate in TYPE_DEPENDS_ON_TYPE METHOD_DEPENDS_ON_TYPE CALLS; do
    count=$(psql_scalar "SELECT count(*) FROM \"SemanticRelationships\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"RelationType\"='$predicate';")
    if [ "${count:-0}" -ge 1 ]; then
        pass "$predicate relationships present ($count)"
    else
        fail INCORRECT_RELATIONSHIPS "no $predicate relationships found (expected a real-world repository to have some)"
    fi
done

# --- source file + line provenance ---
PROVENANCE=$(psql_scalar "
    SELECT se.\"SourceLocation\" FROM \"SemanticEvidence\" se
    JOIN \"SemanticRelationships\" sr ON sr.\"EvidenceId\"=se.\"Id\"
    JOIN \"SemanticEntities\" subj ON subj.\"Id\"=sr.\"SubjectEntityId\"
    WHERE se.\"WorkspaceId\"='$WORKSPACE_ID' AND subj.\"CanonicalName\"='BankingBrain.Infrastructure.Connectors.Git.GitSourceConnector'
    LIMIT 1;")
if [[ "$PROVENANCE" == *"GitSourceConnector.cs"* ]] && [[ "$PROVENANCE" == *":"* ]]; then
    pass "source file + line provenance (evidence location: $PROVENANCE)"
else
    fail PROVENANCE_ISSUES "expected evidence source location referencing GitSourceConnector.cs:<line-range>, got '$PROVENANCE'"
fi

echo ""
echo "=== 5. Grounded code-question retrieval ==="

GROUNDED=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/intelligence/answer" "${AUTH[@]}" \
    -d '{"question":"What does the GitSourceConnector class implement?"}')
GROUNDED_STATUS=$(echo "$GROUNDED" | jq -r '.evidenceStatus // empty')
GROUNDED_CITATION_LOCATION=$(echo "$GROUNDED" | jq -r '.citations[0].sourceLocation // empty')

if [ "$GROUNDED_STATUS" = "grounded" ] && [[ "$GROUNDED_CITATION_LOCATION" == *".cs"* ]]; then
    pass "grounded code-question retrieval (evidenceStatus=grounded, citation=$GROUNDED_CITATION_LOCATION)"
else
    fail RETRIEVAL_ISSUES "grounded retrieval did not return a grounded, cited answer (status='$GROUNDED_STATUS')"
fi

echo ""
echo "=== 6. Change set: add / modify / delete a file, then re-evaluate currentness ==="

NEW_FILE="$FIXTURE_DIR/BankingBrain.Domain/EvalMarkerAdded.cs"
cat > "$NEW_FILE" <<'EOF'
namespace BankingBrain.Domain;

public sealed class EvalMarkerAdded
{
    public string Name { get; set; } = string.Empty;
}
EOF

MODIFIED_FILE="$FIXTURE_DIR/BankingBrain.Domain/EvalMarkerAdded.cs"
# (immediately modify the just-added file in a follow-up commit to also exercise "changed")

# A dedicated, deliberately unreferenced marker class - rather than an arbitrary
# pre-existing file - guarantees a clean currentness signal: nothing else in the
# fixture depends on it, so once its file is deleted it must have NO remaining
# current relationship, and therefore no longer resolve as a current entity.
DELETE_TARGET="$FIXTURE_DIR/BankingBrain.Domain/EvalMarkerToDelete.cs"
cat > "$DELETE_TARGET" <<'EOF'
namespace BankingBrain.Domain;

public sealed class EvalMarkerToDelete
{
    public string Note { get; set; } = string.Empty;
}
EOF
DELETE_TARGET_CLASS="EvalMarkerToDelete"

git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "eval: add marker files" >/dev/null

echo 'public sealed class EvalMarkerAddedChanged { public int Value { get; set; } }' >> "$MODIFIED_FILE"
git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "eval: modify marker file" >/dev/null

# Snapshot the add+modify state BEFORE deleting anything, so the deleted file's
# entity actually gets a historical record to check for - otherwise "add then
# delete between two snapshots" is indistinguishable from "never existed." Waits for
# THIS EXACT revision's extraction to finish (not a global run count) before the file is
# deleted below - the fix for the race this evaluation originally caught: without this,
# the add's extraction could still be queued/running against the live working copy when
# the delete below removes the file out from under it.
ADD_SNAPSHOT_JSON=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/knowledge-sources/$KS_ID/snapshot" "${AUTH[@]}" -d '{}')
ADD_REVISION=$(echo "$ADD_SNAPSHOT_JSON" | jq -r '.revision // empty' | tr -d '[:space:]')
if [ -n "$ADD_REVISION" ]; then
    wait_for_refresh_run "$WORKSPACE_ID" "$KS_ID" "roslyn-csharp" "$ADD_REVISION" || true
fi

DELETE_TARGET_REL="${DELETE_TARGET#$FIXTURE_DIR/}"
git -C "$FIXTURE_DIR" rm -q "$DELETE_TARGET_REL" >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "eval: delete a file" >/dev/null

SNAPSHOT2_HTTP=$(curl -s -o /tmp/codeintel_eval_snapshot2.json -w '%{http_code}' \
    -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/knowledge-sources/$KS_ID/snapshot" "${AUTH[@]}" -d '{}')

if [ "$SNAPSHOT2_HTTP" != "200" ]; then
    fail CURRENTNESS_ISSUES "second snapshot (add/modify/delete) failed with HTTP $SNAPSHOT2_HTTP"
else
    DELETE_REVISION=$(jq -r '.revision // empty' /tmp/codeintel_eval_snapshot2.json | tr -d '[:space:]')
    if [ -n "$DELETE_REVISION" ]; then
        wait_for_refresh_run "$WORKSPACE_ID" "$KS_ID" "roslyn-csharp" "$DELETE_REVISION" || true
    else
        sleep 6
    fi
    wait_for_pipeline 1 || true

    ADDED=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Class' AND \"CanonicalName\"='BankingBrain.Domain.EvalMarkerAdded';")
    if [ "${ADDED:-0}" -ge 1 ]; then
        pass "new file's entity discovered after change (EvalMarkerAdded)"
    else
        fail MISSED_SYMBOLS "new file's class EvalMarkerAdded was not discovered after the second snapshot"
    fi

    CHANGED=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"EntityType\"='Class' AND \"CanonicalName\"='BankingBrain.Domain.EvalMarkerAddedChanged';")
    if [ "${CHANGED:-0}" -ge 1 ]; then
        pass "changed file reflects new relationships/entities (EvalMarkerAddedChanged)"
    else
        fail CURRENTNESS_ISSUES "changed file's added class EvalMarkerAddedChanged was not reflected"
    fi

    if [ -n "$DELETE_TARGET_CLASS" ]; then
        # Historical row must still exist (never deleted)...
        HISTORICAL=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID' AND \"CanonicalName\" LIKE '%$DELETE_TARGET_CLASS%';")
        # ...but it must no longer resolve as a CURRENT code entity.
        RESOLVE_HTTP=$(curl -s -o /tmp/codeintel_eval_resolve.json -w '%{http_code}' \
            -G "$API_BASE/api/v1/workspaces/$WORKSPACE_ID/semantic/query/code/entities" \
            --data-urlencode "canonicalName=BankingBrain.Domain.$DELETE_TARGET_CLASS" "${AUTH[@]}")
        RESOLVED_BODY=$(cat /tmp/codeintel_eval_resolve.json)

        if [ "${HISTORICAL:-0}" -ge 1 ] && [ "$RESOLVE_HTTP" = "404" ]; then
            pass "deleted file's entity preserved historically but excluded from current queries ($DELETE_TARGET_CLASS)"
        else
            fail CURRENTNESS_ISSUES "deleted file's entity '$DELETE_TARGET_CLASS' currentness check failed (historical rows=${HISTORICAL:-0}, resolve HTTP=$RESOLVE_HTTP, body=$RESOLVED_BODY)"
        fi
    else
        fail CURRENTNESS_ISSUES "could not determine a deletable enum/class name from the fixture to test deleted-file currentness"
    fi
fi

echo ""
echo "=== 7. Workspace isolation ==="

WORKSPACE_ID_2=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" \
    -d "{\"name\":\"$WS_NAME Isolation\",\"description\":\"isolation check\"}" | jq -r '.workspaceId')
KS_ID_2=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID_2/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"description\":\"isolation eval fixture\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" \
    | jq -r '.knowledgeSourceId')

curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_ID_2/knowledge-sources/$KS_ID_2/snapshot" "${AUTH[@]}" -d '{}'
sleep 6
wait_for_pipeline_ws2() {
    local elapsed=0
    while [ "$elapsed" -lt "$WAIT_TIMEOUT_SECONDS" ]; do
        local nodes
        nodes=$(psql_scalar "SELECT count(*) FROM \"SemanticGraphNodes\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_2';")
        if [ "${nodes:-0}" -gt 0 ]; then return 0; fi
        sleep 2
        elapsed=$((elapsed + 2))
    done
    return 1
}
wait_for_pipeline_ws2 || true

WS1_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID';")
WS2_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_ID_2';")
CROSS_LEAK=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" e1 JOIN \"SemanticEntities\" e2 ON e1.\"Id\"=e2.\"Id\" WHERE e1.\"WorkspaceId\"='$WORKSPACE_ID' AND e2.\"WorkspaceId\"='$WORKSPACE_ID_2';")

if [ "${WS2_ENTITIES:-0}" -gt 0 ] && [ "${CROSS_LEAK:-0}" -eq 0 ]; then
    pass "workspace isolation (ws1=$WS1_ENTITIES entities, ws2=$WS2_ENTITIES entities, 0 shared rows)"
else
    fail RETRIEVAL_ISSUES "workspace isolation check failed (ws2 entities=${WS2_ENTITIES:-0}, cross-workspace shared rows=${CROSS_LEAK:-0})"
fi

echo ""
echo "=== Cleanup (filesystem fixture only; DB rows left for inspection) ==="
rm -rf "$FIXTURE_DIR"
echo "Removed $FIXTURE_DIR. Workspaces left in DB: $WORKSPACE_ID, $WORKSPACE_ID_2 (named '$WS_NAME'*)."

print_bucket() {
    local title="$1"; shift
    local items=("$@")
    echo "$title: ${#items[@]}"
    for item in "${items[@]}"; do
        echo "  - $item"
    done
}

echo ""
echo "================= EVALUATION RESULT ================="
echo "Capabilities verified: $PASS_COUNT"
for c in "${CAPABILITIES_VERIFIED[@]}"; do echo "  - $c"; done
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
echo "======================================================="
echo "TOTAL: $PASS_COUNT passed, $FAIL_COUNT failed"

[ "$FAIL_COUNT" -eq 0 ]
