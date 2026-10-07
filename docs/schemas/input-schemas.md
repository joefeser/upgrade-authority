# Fixture input schemas (pinned 2026-10-02, spec/fixture-corpus)

Golden fixtures under `fixtures/` consume exactly these five input shapes. All fixture files are `source: fixture-declared` — honest about being sanitized declarations, not observations of a real estate.

1. **`producer-evidence.v0.json`** (SPEC-001 §2.3 — V0's only producer rung):
   `{ schemaVersion: "producer-evidence.v0", source: "fixture-declared", externalPackages?: [ { packageId, note? } ], producers: [ { repo, packageId, producedVersion?, publicationStatus?: "published"|"unpublished", evidenceNote? } ] }`
   Absent `publicationStatus` ⇒ `unknown` — never assumed published. `externalPackages` declares a package external/public **as evidence** — producer absence alone means unknown producer, never "public" (rule proven by F3a vs F5).

2. **`ownership.v0.json`** (SPEC-001 §2.4):
   `{ schemaVersion: "ownership.v0", selfTeamId, ownerships: [ { repo, team } ] }`
   Repo absent from `ownerships` ⇒ ownership `unknown` — never "ours".

3. **`package-evidence.v0.json`** — sanitized consumer-side facts (represents tracemap `PackageReferenced` / packages.config output for fixtures):
   `{ schemaVersion: "package-evidence.v0", source: "fixture-declared", facts: [ { repo, packageId, declaredConstraint, format: "packagereference"|"packages.config"|"cpm", constraintSource: "Project"|"Directory.Packages.props"|"VersionOverride"|"packages.config", tfm?, projects?: [..], commitSha, path } ], scanCoverage: [ { repo, status: "complete"|"gaps", gaps?: [..] } ] }`
   ⚠️ **Reconciliation task (logged in SPEC-001 §9):** the exact real tracemap fact export shape is not yet pinned to a tracemap artifact; `package-evidence.v0` is the fixture shape. Before the runner consumes real tracemap output, pin the real schema and add a converter (or extend `producer-evidence`-style honesty markers).

4. **`delta.json`** — tracemap `package-delta.v1`, pinned and verified against tracemap `main @ 32ab4c3` (`samples/package-deltas/package-delta.example.json`):
   `{ version: "package-delta.v1", sourceRepo, sourceCommitSha, changes: [ { id, packageName, ecosystem, changeType, oldVersion, newVersion } ] }`

5. **`lockfile-rows.v0.json`** — checked-in `packages.lock.json` rows (optional, per repo; **frozen** — SPEC-007 §3):
   `{ schemaVersion: "lockfile-rows.v0", repo, rows: [ { packageId, type: "direct"|"transitive"|"unknown", version?, via?, names? } ] }`
   `names: null` (joined string >256 chars in tracemap's reader) ⇒ unknown child detail, never evidence of absence. One row per `packageId` per repo; duplicate `packageId` with different `version` is a typed error at plan load.

5a. **`lockfile-rows.v1.json`** (SPEC-007 §3) — multi-lockfile/multi-TFM rows; **`ua ingest` emits this shape only**:
   `{ schemaVersion: "lockfile-rows.v1", repo, rows: [ { packageId, type: "direct"|"transitive"|"unknown", version?, via?, names?, lockfile, tfm } ] }`
   Row identity = **(repo, lockfile, tfm, packageId)** — multiple rows per package are legal; cross-lockfile/cross-TFM version disagreement becomes a plan finding (T10), never an error. `lockfile` (repo-relative packages.lock.json path) and `tfm` (verbatim from tracemap; `""` allowed) are required keys on every row. Two rows sharing `(lockfile, tfm, packageId)` must be identical in every field to collapse (idempotent re-ingest); any differing field (`version`, `type`, `via`, `names`) is a conflict within one resolution group — typed error (ingest exit 4 / plan load exit 3). Null/empty `version` = unevidenced resolution: never counts toward disagreement, never renders. The plan's `lockfile-rows.*` evidence kind cites the input's actual schemaVersion (v1 inputs ⇒ `lockfile-rows.v1`).

5b. **`lockfile-rows.v2.json`** (SPEC-008 §3) — multi-REPO envelope; **`ua ingest` emits this shape once SPEC-008 ships** (until then ingest still emits v1 and exits 2 on multi-repo lockfiles):
   `{ schemaVersion: "lockfile-rows.v2", repos: [ { repo, rows: [ <v1 row shape> ] } ] }`
   Row shape and rules are exactly v1 (§5a) applied per repo. `repos[]` sorted by repo name (ordinal); duplicate repo entries and empty `repos` are typed errors; two lockfile files of different versions present in one input dir is a typed error.

Output contract: **`plan.schema.v1.json`** (JSON Schema 2020-12) — every golden `golden/plan.json` validates against it. SPEC-007 adds the optional `uncertainty.findings[]` array (`{ subject, detail }`, emitted only when non-empty).
