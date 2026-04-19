#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${AICAN_BASE_URL:-http://127.0.0.1:5080}"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_cmd curl
require_cmd python3

fetch_json() {
  local method="$1"
  local url="$2"
  local body=""
  if [[ "$#" -ge 3 ]]; then
    body="$3"
    shift 3
  else
    shift 2
  fi
  local -a headers=("$@")

  local response_file="$TMP_DIR/response.json"
  local status
  local -a curl_args=(-sS -o "$response_file" -w "%{http_code}" -X "$method" "$url")

  if [[ -n "$body" ]]; then
    curl_args+=(-H "Content-Type: application/json" --data "$body")
  else
    :
  fi

  if [[ "${#headers[@]}" -gt 0 ]]; then
    curl_args+=("${headers[@]}")
  fi

  status="$(curl "${curl_args[@]}")"

  if [[ "$status" -lt 200 || "$status" -ge 300 ]]; then
    echo "Request failed: $method $url (HTTP $status)" >&2
    cat "$response_file" >&2
    exit 1
  fi

  cat "$response_file"
}

echo "==> API health"
fetch_json GET "$BASE_URL/healthz" >/dev/null

echo "==> System status"
SYSTEM_STATUS="$(fetch_json GET "$BASE_URL/system/status")"
python3 - <<'PY' "$SYSTEM_STATUS"
import json, sys
status = json.loads(sys.argv[1])
services = {item["key"]: item["state"] for item in status["services"]}
print(json.dumps(services, indent=2))
PY

echo "==> Session exchange"
SESSION_JSON="$(fetch_json POST "$BASE_URL/session/exchange" '{"email":"user@example.test","displayName":"Demo User","botName":"AiCan Assistant","department":"General","m365AccessToken":null}')"
SESSION_TOKEN="$(python3 - <<'PY' "$SESSION_JSON"
import json, sys
print(json.loads(sys.argv[1])["sessionToken"])
PY
)"

echo "==> Assistant profile"
fetch_json GET "$BASE_URL/assistant/profile" "" -H "X-AiCan-Session: $SESSION_TOKEN" >/dev/null

echo "==> Greeting chat"
CHAT_JSON="$(fetch_json POST "$BASE_URL/assistant/chat" '{"message":"hello"}' -H "X-AiCan-Session: $SESSION_TOKEN")"
python3 - <<'PY' "$CHAT_JSON"
import json, sys
payload = json.loads(sys.argv[1])
assert payload["message"], "chat response message was empty"
print(payload["message"])
PY

echo "==> Document intake"
INTAKE_JSON="$(fetch_json POST "$BASE_URL/documents/intake/register" '{"originalFilePath":"","fileName":"demo-note.txt","department":"General","visibility":"CommonShared","declaredCategory":"general","ownerEmail":"user@example.test","customerName":"demo","fileContentBase64":"VGhpcyBpcyBhIGRlbW8gZG9jdW1lbnQgZm9yIEFpQ2FuIHNtb2tlIHRlc3Rpbmcu","extractedText":"This is a demo document for AiCan smoke testing."}' -H "X-AiCan-Session: $SESSION_TOKEN")"
DOCUMENT_ID="$(python3 - <<'PY' "$INTAKE_JSON"
import json, sys
print(json.loads(sys.argv[1])["documentId"])
PY
)"

echo "==> Document search"
SEARCH_JSON="$(fetch_json POST "$BASE_URL/documents/search" '{"query":"demo smoke testing"}' -H "X-AiCan-Session: $SESSION_TOKEN")"
python3 - <<'PY' "$SEARCH_JSON" "$DOCUMENT_ID"
import json, sys
payload = json.loads(sys.argv[1])
doc_id = sys.argv[2]
ids = [item["documentId"] for item in payload["results"]]
assert doc_id in ids, f"expected document {doc_id} in search results, got {ids}"
print(f"Search returned {len(payload['results'])} result(s)")
PY

echo "Smoke test passed."
