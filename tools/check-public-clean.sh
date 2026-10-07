#!/bin/bash
# Public-repo cleanliness gate: fails if employer-identifying or estate-revealing
# terms appear in the tree. Run before any release/visibility change.
# Allowlist: docs/COMPARISONS.md may name vendors in third-party product context.
set -u
cd "$(dirname "$0")/.."
fail=0
scan() {
  local pattern="$1" label="$2"
  while IFS= read -r line; do
    local file="${line%%:*}"
    if [ "$file" = "docs/COMPARISONS.md" ]; then continue; fi
    echo "FAIL [$label] $line"; fail=1
  done < <(grep -rniE "$pattern" --include="*.md" --include="*.json" --include="*.cs" --include="*.mjs" --include="*.props" --include="*.csproj" . 2>/dev/null | grep -v "testdata-ingest" || true)
}
scan '\bunited\b|united'"'"'s' "employer-name"
scan '\bwiz\b|sonarqube|sonarqube' "alert-stack"
scan 'CI in Harness|CI = Harness|lives in GitHub and Harness' "ci-stack"
scan '~150|150 repos|150-repo' "estate-scale"
scan '8 packages compiled|publishes ~8 packages' "producer-scale"
scan 'employment agreement|invention-assignment|outside-work policy' "employment-terms"
scan '88mph|88mphServer' "other-ventures"
if [ $fail -eq 0 ]; then echo "PUBLIC-CLEAN: no employer-identifying or estate-revealing terms found"; else exit 1; fi
