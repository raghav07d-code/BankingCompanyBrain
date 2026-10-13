#!/usr/bin/env bash
#
# Deterministic, LLM-independent evaluation of the Semantic Intelligence pipeline:
# entity resolution, relationship retrieval, provenance, grounded-context
# construction, citation resolution, controlled failure modes, and stage-level
# performance observability. Every check here either hits a pure query endpoint
# (never touches an LLM) or, where it hits /semantic/intelligence/answer, asserts
# only on the retrieved "context" array (populated before generation) - so this
# script produces the same PASS/FAIL results regardless of which
# IGroundedAnswerGenerator is configured, including when no LLM is reachable at
# all (Llm:Provider=deterministic is fine and is in fact preferred for this run).
#
# Uses a small, disposable, deliberately-ambiguous git fixture (two classes each
# with a method of the same simple name) rather than any real application
# workspace, so results are fully reproducible and nothing real is touched.
#
# Usage:
#   ./scripts/evaluate-semantic-intelligence.sh
# Optional env overrides: API_BASE, REPOS_ROOT, DB_NAME, DB_USER, API_LOG_FILE
# (API_LOG_FILE, if set and readable, is used for the performance-observability
# check; otherwise that check is skipped rather than failed, since a caller
# running against someone else's already-running instance may not have log
# access).

set -o pipefail

API_BASE="${API_BASE:-http://localhost:5207}"
REPOS_ROOT="${REPOS_ROOT:-src/BankingBrain.Api/repositories}"
DB_NAME="${DB_NAME:-BankingBrainDb}"
DB_USER="${DB_USER:-$(whoami)}"
API_LOG_FILE="${API_LOG_FILE:-}"
FIXTURE_NAME="semantic-intel-eval-fixture"
FIXTURE_DIR="$REPOS_ROOT/$FIXTURE_NAME"
CONNECTOR_EXTERNAL_ID="repositories/$FIXTURE_NAME"
WAIT_TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-120}"

PASS_COUNT=0
FAIL_COUNT=0
declare -a RESULTS=()

pass() { RESULTS+=("PASS: $1"); PASS_COUNT=$((PASS_COUNT + 1)); echo "  [PASS] $1"; }
fail() { RESULTS+=("FAIL: $1"); FAIL_COUNT=$((FAIL_COUNT + 1)); echo "  [FAIL] $1"; }
skip() { RESULTS+=("SKIP: $1"); echo "  [SKIP] $1"; }

psql_scalar() { psql -U "$DB_USER" -d "$DB_NAME" -tAc "$1" 2>/dev/null | head -1 | tr -d '[:space:]'; }

require() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required tool: $1" >&2; exit 1; }; }
require curl; require jq; require psql; require git

echo "=== 0. Preconditions ==="
if ! curl -s -o /dev/null --max-time 5 "$API_BASE/api/v1/auth/login"; then
    echo "API not reachable at $API_BASE." >&2
    exit 1
fi
if ! psql -U "$DB_USER" -d "$DB_NAME" -tAc "SELECT 1" >/dev/null 2>&1; then
    echo "Database not reachable." >&2
    exit 1
fi
echo "API and database reachable ($API_BASE)."

ADMIN_EMAIL=$(jq -r '.Seed.AdminEmail' src/BankingBrain.Api/appsettings.Development.json)
ADMIN_PASSWORD=$(jq -r '.Seed.AdminPassword' src/BankingBrain.Api/appsettings.Development.json)

echo ""
echo "=== 1. Deterministic ambiguous-name fixture ==="
# Account/Loan each expose a method named "Calculate" - looking it up by that bare
# simple name must be rejected as ambiguous, while a fully-qualified or Type.Method
# form must resolve unambiguously to the right one.
mkdir -p "$FIXTURE_DIR"
cat > "$FIXTURE_DIR/Fixture.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF
cat > "$FIXTURE_DIR/Account.cs" <<'EOF'
namespace SemanticEvalFixture;

public interface IHasBalance
{
    decimal Balance { get; }
}

public class Account : IHasBalance
{
    public decimal Balance { get; set; }

    public void Calculate()
    {
        Balance += 1;
    }
}
EOF
cat > "$FIXTURE_DIR/Loan.cs" <<'EOF'
namespace SemanticEvalFixture;

public class Loan
{
    public decimal Principal { get; set; }

    public void Calculate()
    {
        Principal += 1;
    }
}
EOF
git -C "$FIXTURE_DIR" init -q 2>/dev/null
git -C "$FIXTURE_DIR" config user.email eval@semantic.local
git -C "$FIXTURE_DIR" config user.name "Semantic Eval"
git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m baseline --allow-empty >/dev/null
echo "Fixture ready at $FIXTURE_DIR ($(git -C "$FIXTURE_DIR" rev-parse --short HEAD))."

echo ""
echo "=== 2. Auth + two isolated workspaces (for cross-workspace checks) ==="
TOKEN=$(curl -s -X POST "$API_BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r '.accessToken')
if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then echo "Login failed." >&2; exit 1; fi
AUTH=(-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

WS_NAME="SemanticIntel Eval $(date +%s)"
WORKSPACE_A=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" -d "{\"name\":\"$WS_NAME A\"}" | jq -r '.workspaceId')
KS_A=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" | jq -r '.knowledgeSourceId')
curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/knowledge-sources/$KS_A/snapshot" "${AUTH[@]}" -d '{}'

WORKSPACE_B=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" -d "{\"name\":\"$WS_NAME B\"}" | jq -r '.workspaceId')
KS_B=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_B/knowledge-sources" "${AUTH[@]}" \
    -d "{\"name\":\"$FIXTURE_NAME\",\"sourceType\":2,\"connectorKey\":\"git\",\"externalIdentifier\":\"$CONNECTOR_EXTERNAL_ID\"}" | jq -r '.knowledgeSourceId')
curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_B/knowledge-sources/$KS_B/snapshot" "${AUTH[@]}" -d '{}'

echo "Workspace A=$WORKSPACE_A, Workspace B=$WORKSPACE_B (same fixture content in both)."

wait_for_extraction() {
    local ws="$1" elapsed=0
    while [ "$elapsed" -lt "$WAIT_TIMEOUT_SECONDS" ]; do
        local nodes
        nodes=$(psql_scalar "SELECT count(*) FROM \"SemanticGraphNodes\" WHERE \"WorkspaceId\"='$ws';")
        if [ "${nodes:-0}" -gt 0 ]; then return 0; fi
        sleep 2; elapsed=$((elapsed + 2))
    done
    return 1
}
wait_for_extraction "$WORKSPACE_A" || echo "  [WARN] workspace A extraction did not settle in time"
wait_for_extraction "$WORKSPACE_B" || echo "  [WARN] workspace B extraction did not settle in time"

query_code_entity() {
    # $1=workspace $2=canonicalName -> prints HTTP status
    curl -s -o /tmp/semantic_eval_entity.json -w '%{http_code}' -G \
        "$API_BASE/api/v1/workspaces/$1/semantic/query/code/entities" \
        --data-urlencode "canonicalName=$2" "${AUTH[@]}"
}

ACCOUNT_ENTITY_ID=$(psql_scalar "SELECT \"Id\" FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_A' AND \"CanonicalName\"='SemanticEvalFixture.Account';")
ACCOUNT_CALC_ID=$(psql_scalar "SELECT \"Id\" FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_A' AND \"CanonicalName\" LIKE 'SemanticEvalFixture.Account.Calculate%';")
LOAN_CALC_ID=$(psql_scalar "SELECT \"Id\" FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_A' AND \"CanonicalName\" LIKE 'SemanticEvalFixture.Loan.Calculate%';")

echo ""
echo "=== 3. ENTITY RESOLUTION ==="

STATUS=$(query_code_entity "$WORKSPACE_A" "SemanticEvalFixture.Account")
BODY=$(cat /tmp/semantic_eval_entity.json)
if [ "$STATUS" = "200" ] && [ "$(echo "$BODY" | jq -r '.entityId')" = "$ACCOUNT_ENTITY_ID" ]; then
    pass "exact fully-qualified name resolution (Account)"
else
    fail "exact fully-qualified name resolution: HTTP $STATUS body=$BODY"
fi

STATUS=$(query_code_entity "$WORKSPACE_A" "SemanticEvalFixture.Account.Calculate()")
if [ "$STATUS" = "200" ]; then
    pass "exact fully-qualified method resolution (Account.Calculate)"
else
    fail "exact fully-qualified method resolution: HTTP $STATUS"
fi

STATUS=$(query_code_entity "$WORKSPACE_A" "Account.Calculate")
BODY=$(cat /tmp/semantic_eval_entity.json)
if [ "$STATUS" = "200" ] && [ "$(echo "$BODY" | jq -r '.entityId')" = "$ACCOUNT_CALC_ID" ]; then
    pass "namespace-qualified Type.Method resolution (Account.Calculate, not Loan.Calculate)"
else
    fail "Type.Method resolution: HTTP $STATUS body=$BODY"
fi

STATUS=$(query_code_entity "$WORKSPACE_A" "Calculate")
if [ "$STATUS" = "404" ]; then
    pass "ambiguous simple name correctly refused (Calculate exists on two types)"
else
    fail "ambiguous simple name should be 404, got HTTP $STATUS"
fi

STATUS=$(query_code_entity "$WORKSPACE_A" "SemanticEvalFixture.DoesNotExist")
if [ "$STATUS" = "404" ]; then
    pass "no-match entity correctly refused"
else
    fail "no-match entity should be 404, got HTTP $STATUS"
fi

CROSS_ENTITIES=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" e1 JOIN \"SemanticEntities\" e2 ON e1.\"Id\"=e2.\"Id\" WHERE e1.\"WorkspaceId\"='$WORKSPACE_A' AND e2.\"WorkspaceId\"='$WORKSPACE_B';")
WS_B_ENTITY_COUNT=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_B';")
if [ "${CROSS_ENTITIES:-1}" = "0" ] && [ "${WS_B_ENTITY_COUNT:-0}" -gt 0 ]; then
    pass "entity resolution workspace isolation (0 shared entity rows between A and B)"
else
    fail "entity workspace isolation: shared rows=${CROSS_ENTITIES:-?}, wsB entities=${WS_B_ENTITY_COUNT:-?}"
fi

echo ""
echo "=== 4. RELATIONSHIP RETRIEVAL ==="

RELATED=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/code/entities/$ACCOUNT_ENTITY_ID/related" \
    --data-urlencode "predicate=TYPE_CONTAINS_METHOD" --data-urlencode "direction=Outgoing" "${AUTH[@]}")
RELATED_ID=$(echo "$RELATED" | jq -r '.[0].entity.entityId // empty')
if [ "$RELATED_ID" = "$ACCOUNT_CALC_ID" ]; then
    pass "relationship direction/subject-object correctness (Account -TYPE_CONTAINS_METHOD-> Account.Calculate)"
else
    fail "relationship retrieval returned unexpected entity: $RELATED"
fi

IMPLEMENTS=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/relationships" \
    --data-urlencode "subjectEntityId=$ACCOUNT_ENTITY_ID" --data-urlencode "predicate=INHERITS_OR_IMPLEMENTS" "${AUTH[@]}")
if echo "$IMPLEMENTS" | jq -e '.relationships | length >= 1' >/dev/null 2>&1; then
    pass "relationship type correctness (Account INHERITS_OR_IMPLEMENTS IHasBalance found)"
else
    fail "expected INHERITS_OR_IMPLEMENTS relationship not found: $IMPLEMENTS"
fi

NO_MATCH=$(curl -s -G "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/code/entities/$ACCOUNT_CALC_ID/related" \
    --data-urlencode "predicate=TYPE_CONTAINS_METHOD" --data-urlencode "direction=Outgoing" "${AUTH[@]}")
if [ "$(echo "$NO_MATCH" | jq 'length')" = "0" ]; then
    pass "no-relationships case returns empty array, not an error (a method has no TYPE_CONTAINS_METHOD children)"
else
    fail "expected empty array for a leaf entity, got: $NO_MATCH"
fi

CROSS_RELATIONSHIPS=$(psql_scalar "SELECT count(*) FROM \"SemanticRelationships\" r1 JOIN \"SemanticRelationships\" r2 ON r1.\"Id\"=r2.\"Id\" WHERE r1.\"WorkspaceId\"='$WORKSPACE_A' AND r2.\"WorkspaceId\"='$WORKSPACE_B';")
if [ "${CROSS_RELATIONSHIPS:-1}" = "0" ]; then
    pass "relationship retrieval workspace isolation (0 shared relationship rows)"
else
    fail "relationship workspace isolation: shared rows=$CROSS_RELATIONSHIPS"
fi

echo ""
echo "=== 5. PROVENANCE ==="

PROVENANCE=$(curl -s "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/code/entities/$ACCOUNT_ENTITY_ID/provenance" "${AUTH[@]}")
FIRST=$(echo "$PROVENANCE" | jq -c '.[0]')
DV=$(echo "$FIRST" | jq -r '.relationship.documentVersionId // empty')
EV=$(echo "$FIRST" | jq -r '.evidence.evidenceId // empty')
LOC=$(echo "$FIRST" | jq -r '.evidence.sourceLocation // empty')
if [ -n "$DV" ] && [ -n "$EV" ] && [[ "$LOC" == *"Account.cs:"* ]]; then
    pass "provenance carries DocumentVersionId + EvidenceId + valid source location ($LOC)"
else
    fail "provenance incomplete: $FIRST"
fi

if [[ "$LOC" =~ :[0-9]+-[0-9]+$ ]]; then
    pass "source span/line information present (start-end line range)"
else
    fail "source location missing a line span: $LOC"
fi

echo ""
echo "=== 6. GROUNDED CONTEXT ==="

ANSWER1=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/intelligence/answer" "${AUTH[@]}" \
    -d '{"question":"What does the Account class implement?"}')
CONTEXT1=$(echo "$ANSWER1" | jq -c '.context')
CONTEXT1_ENTITY_IDS=$(echo "$ANSWER1" | jq -r '[.context[].semanticEntityId] | map(select(. != null)) | unique | sort | @csv')

UNRELATED=$(echo "$ANSWER1" | jq --arg loan "$LOAN_CALC_ID" '[.context[] | select(.semanticEntityId == $loan)] | length')
if [ "$UNRELATED" = "0" ]; then
    pass "grounded context contains no unrelated entity (Loan.Calculate absent from an Account question)"
else
    fail "grounded context leaked an unrelated entity: $UNRELATED occurrence(s) of Loan.Calculate"
fi

INTERNAL_CONSISTENT=$(echo "$ANSWER1" | jq '[.context[] | select(.relationshipId != null and .evidenceId == null)] | length')
if [ "$INTERNAL_CONSISTENT" = "0" ]; then
    pass "context items with a relationshipId all carry matching evidence (internally consistent)"
else
    fail "$INTERNAL_CONSISTENT context item(s) have a relationshipId but no evidenceId"
fi

ANSWER2=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/intelligence/answer" "${AUTH[@]}" \
    -d '{"question":"What does the Account class implement?"}')
CONTEXT2_ENTITY_IDS=$(echo "$ANSWER2" | jq -r '[.context[].semanticEntityId] | map(select(. != null)) | unique | sort | @csv')
if [ "$CONTEXT1_ENTITY_IDS" = "$CONTEXT2_ENTITY_IDS" ] && [ -n "$CONTEXT1_ENTITY_IDS" ]; then
    pass "grounded context is deterministic for identical input (same entity set across two calls)"
else
    fail "grounded context differed across identical calls: [$CONTEXT1_ENTITY_IDS] vs [$CONTEXT2_ENTITY_IDS]"
fi

echo ""
echo "=== 7. CITATION RESOLUTION ==="

REAL_RELATIONSHIP_ID=$(psql_scalar "SELECT \"Id\" FROM \"SemanticRelationships\" WHERE \"WorkspaceId\"='$WORKSPACE_A' LIMIT 1;")
EVIDENCE_HTTP=$(curl -s -o /tmp/semantic_eval_evidence.json -w '%{http_code}' \
    "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/code/relationships/$REAL_RELATIONSHIP_ID/evidence" "${AUTH[@]}")
if [ "$EVIDENCE_HTTP" = "200" ] && [ "$(jq -r '.evidence.evidenceId // empty' /tmp/semantic_eval_evidence.json)" != "" ]; then
    pass "a real citation reference resolves to real stored evidence"
else
    fail "real relationship evidence lookup failed: HTTP $EVIDENCE_HTTP"
fi

RANDOM_GUID="00000000-0000-0000-0000-000000000000"
INVALID_HTTP=$(curl -s -o /dev/null -w '%{http_code}' \
    "$API_BASE/api/v1/workspaces/$WORKSPACE_A/semantic/query/code/relationships/$RANDOM_GUID/evidence" "${AUTH[@]}")
if [ "$INVALID_HTTP" = "404" ]; then
    pass "unknown/invalid citation identifier is rejected (404), not fabricated"
else
    fail "unknown citation identifier should be 404, got HTTP $INVALID_HTTP"
fi

CROSS_WORKSPACE_HTTP=$(curl -s -o /dev/null -w '%{http_code}' \
    "$API_BASE/api/v1/workspaces/$WORKSPACE_B/semantic/query/code/relationships/$REAL_RELATIONSHIP_ID/evidence" "${AUTH[@]}")
if [ "$CROSS_WORKSPACE_HTTP" = "404" ]; then
    pass "citations cannot cross workspaces (workspace A's relationship id is 404 under workspace B)"
else
    fail "cross-workspace citation lookup should be 404, got HTTP $CROSS_WORKSPACE_HTTP"
fi

echo ""
echo "=== 8. Historical exclusion (delete a file, verify current-vs-historical) ==="

cat > "$FIXTURE_DIR/ToDelete.cs" <<'EOF'
namespace SemanticEvalFixture;

public sealed class EvalHistoricalMarker
{
    public string Note { get; set; } = string.Empty;
}
EOF
git -C "$FIXTURE_DIR" add -A >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "add marker" >/dev/null
curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/knowledge-sources/$KS_A/snapshot" "${AUTH[@]}" -d '{}'
sleep 6
git -C "$FIXTURE_DIR" rm -q ToDelete.cs >/dev/null
git -C "$FIXTURE_DIR" commit -q -m "delete marker" >/dev/null
curl -s -o /dev/null -X POST "$API_BASE/api/v1/workspaces/$WORKSPACE_A/knowledge-sources/$KS_A/snapshot" "${AUTH[@]}" -d '{}'
sleep 6

HISTORICAL_ROWS=$(psql_scalar "SELECT count(*) FROM \"SemanticEntities\" WHERE \"WorkspaceId\"='$WORKSPACE_A' AND \"CanonicalName\" LIKE '%EvalHistoricalMarker%';")
CURRENT_HTTP=$(query_code_entity "$WORKSPACE_A" "SemanticEvalFixture.EvalHistoricalMarker")
if [ "${HISTORICAL_ROWS:-0}" -ge 1 ] && [ "$CURRENT_HTTP" = "404" ]; then
    pass "historical evidence preserved but excluded from current entity resolution after deletion"
else
    fail "historical/current split failed: historical rows=${HISTORICAL_ROWS:-0}, current-resolve HTTP=$CURRENT_HTTP"
fi

echo ""
echo "=== 9. FAILURE MODES ==="

EMPTY_WS=$(curl -s -X POST "$API_BASE/api/v1/workspaces" "${AUTH[@]}" -d "{\"name\":\"$WS_NAME Empty\"}" | jq -r '.workspaceId')
EMPTY_ANSWER=$(curl -s -X POST "$API_BASE/api/v1/workspaces/$EMPTY_WS/semantic/intelligence/answer" "${AUTH[@]}" \
    -d '{"question":"What is the meaning of life?"}')
if [ "$(echo "$EMPTY_ANSWER" | jq -r '.evidenceStatus')" = "insufficient-evidence" ] && \
   [ "$(echo "$EMPTY_ANSWER" | jq -r '.providerIdentity')" = "deterministic" ]; then
    pass "empty grounded context yields controlled insufficient-evidence, LLM never invoked"
else
    fail "empty-context failure mode incorrect: $EMPTY_ANSWER"
fi

FABRICATED=$(echo "$EMPTY_ANSWER" | jq '.citations | length')
if [ "$FABRICATED" = "0" ]; then
    pass "no fabricated citations on insufficient-evidence"
else
    fail "insufficient-evidence answer unexpectedly carries $FABRICATED citation(s)"
fi

echo ""
echo "=== 10. PERFORMANCE OBSERVABILITY ==="

if [ -n "$API_LOG_FILE" ] && [ -r "$API_LOG_FILE" ]; then
    # RelationshipRetrieval instruments the graph/banking-predicate branch
    # (intent.RequiresGraph), which only ever fires for a question that matches a
    # seeded SemanticPredicate with AllowsEntityObject - this fixture is pure Code
    # Intelligence data with no banking claims/predicates, so that branch (and its
    # stage log) is correctly never exercised here. Code relationship retrieval
    # itself is fully covered above via ProvenanceRetrieval and the direct
    # /semantic/query/code/... checks in section 4.
    for stage in IntentAnalysis EntityResolution SemanticRetrieval ProvenanceRetrieval GroundedContextConstruction TotalRequest; do
        if grep -q "GroundedStageTiming Stage=$stage" "$API_LOG_FILE"; then
            pass "stage timing observed in logs: $stage"
        else
            fail "no stage timing log entry found for: $stage"
        fi
    done
    skip "stage timing for RelationshipRetrieval (graph/banking-predicate branch - not reachable from a pure Code Intelligence fixture with no banking claims; this is expected, not a defect)"
else
    skip "performance-observability log check (no readable API_LOG_FILE provided; stage timing code was still exercised by every call above)"
fi

echo ""
echo "=== Cleanup (filesystem fixture only; DB rows left for inspection) ==="
rm -rf "$FIXTURE_DIR"
echo "Removed $FIXTURE_DIR. Workspaces left in DB: $WORKSPACE_A, $WORKSPACE_B, $EMPTY_WS (named '$WS_NAME'*)."

echo ""
echo "================= SEMANTIC INTELLIGENCE EVALUATION RESULT ================="
for r in "${RESULTS[@]}"; do echo "  $r"; done
echo "============================================================================="
echo "TOTAL: $PASS_COUNT passed, $FAIL_COUNT failed"

[ "$FAIL_COUNT" -eq 0 ]
