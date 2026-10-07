#!/usr/bin/env node
// Canonical golden formatter (normative serializer for plan.v1).
// Enforces EVERY ordering rule from SPEC-003 §4.2 in one place:
//   - key order per docs/schemas/plan.schema.v1.json (unit: notes before publicationStatus)
//   - evidenceKinds canonical order; gaps sorted by subject (lexicographic)
//   - unit packages natural-sorted (numeric-suffix aware) — permutation-stable
//   - publicationStatus keys natural-sorted
//   - 2-space indent, fully expanded, LF, trailing newline, minimal escaping
// Idempotent; deterministic. `node tools/canonicalize-goldens.mjs` rewrites files;
// validate-fixtures.mjs imports toCanonicalText() for side-effect-free checking.
import { readFileSync, writeFileSync, readdirSync, statSync, existsSync } from "node:fs";
import { join } from "node:path";
import { templateRank } from "./templates.mjs";

const EVIDENCE_ORDER = ["package-evidence.v0", "producer-evidence.v0", "lockfile-rows.v0", "lockfile-rows.v1", "lockfile-rows.v2", "ownership.v0", "scan-coverage"];

const KEY_ORDER = {
  root: ["schemaVersion", "delta", "repos", "waves", "stop", "uncertainty"],
  delta: ["packageName", "ecosystem", "changeType", "oldVersion", "newVersion", "origin"],
  repo: ["repo", "classification", "reasons", "ownership", "actionType", "evidenceKinds", "confidence"],
  confidence: ["rung", "corroboration"],
  wave: ["index", "status", "blockedOn", "condition", "prerequisites", "releaseUnits"],
  unit: ["repo", "packages", "basis", "notes", "publicationStatus"],
  stop: ["reason", "detail", "cyclePath"],
  uncertainty: ["contradictions", "gaps", "findings"],
  contradiction: ["id", "subject", "claims", "downstreamProvisional"],
  claim: ["repo", "packageId"],
  gap: ["subject", "detail"],
  finding: ["subject", "detail"],
};

function naturalCmp(a, b) {
  return a.localeCompare(b, "en", { numeric: true, sensitivity: "base" });
}

// code-unit (ordinal) comparison — matches .NET string.CompareOrdinal, which the engine uses for findings
function codeUnitCmp(a, b) {
  return a < b ? -1 : a > b ? 1 : 0;
}

function order(value, table) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return value;
  const out = {};
  for (const k of KEY_ORDER[table]) if (k in value) out[k] = value[k];
  for (const k of Object.keys(value)) if (!(k in out)) out[k] = value[k];
  return out;
}

export function toCanonical(g) {
  g = order(g, "root");
  if (g.delta) g.delta = order(g.delta, "delta");
  g.repos = (g.repos ?? []).map(r => {
    r = order(r, "repo");
    if (r.confidence) r.confidence = order(r.confidence, "confidence");
    if (Array.isArray(r.evidenceKinds)) {
      r.evidenceKinds = [...new Set(r.evidenceKinds)].sort(
        (a, b) => (EVIDENCE_ORDER.indexOf(a) + 1 || 99) - (EVIDENCE_ORDER.indexOf(b) + 1 || 99) || a.localeCompare(b));
    }
    return r;
  });
  g.waves = (g.waves ?? [])
    .slice()
    .sort((a, b) => (a.index ?? 0) - (b.index ?? 0))
    .map(w => {
    w = order(w, "wave");
    if (Array.isArray(w.prerequisites)) w.prerequisites = [...w.prerequisites].sort((a, b) => templateRank(a) - templateRank(b) || a.localeCompare(b));
    w.releaseUnits = (w.releaseUnits ?? [])
      .slice()
      .sort((a, b) => naturalCmp(a.repo ?? "", b.repo ?? ""))
      .map(u => {
      u = order(u, "unit");
      if (Array.isArray(u.packages)) u.packages = [...u.packages].sort(naturalCmp);
      if (u.publicationStatus && typeof u.publicationStatus === "object" && !Array.isArray(u.publicationStatus)) {
        u.publicationStatus = Object.fromEntries(Object.entries(u.publicationStatus).sort(([a], [b]) => naturalCmp(a, b)));
      }
      return u;
    });
    return w;
  });
  if (g.stop) g.stop = order(g.stop, "stop");
  if (g.uncertainty) {
    g.uncertainty = order(g.uncertainty, "uncertainty");
    g.uncertainty.contradictions = (g.uncertainty.contradictions ?? [])
      .slice()
      .sort((a, b) => naturalCmp(a.id ?? "", b.id ?? ""))
      .map(c => {
      c = order(c, "contradiction");
      c.claims = [...(c.claims ?? [])].sort((a, b) => naturalCmp(a.repo, b.repo));
      if (Array.isArray(c.downstreamProvisional)) c.downstreamProvisional = [...c.downstreamProvisional].sort(naturalCmp);
      return c;
    });
    g.uncertainty.gaps = [...(g.uncertainty.gaps ?? [])].sort((a, b) => codeUnitCmp(a.subject ?? "", b.subject ?? "")); // ordinal, matching Engine.BuildGaps (localeCompare diverges on mixed case — PR #11 known issue, now load-bearing)
    if (Array.isArray(g.uncertainty.findings))
      g.uncertainty.findings = [...g.uncertainty.findings].sort((a, b) =>
        codeUnitCmp(a.subject ?? "", b.subject ?? "") || codeUnitCmp(a.detail ?? "", b.detail ?? "")); // ordinal, matching Engine.BuildFindings (localeCompare would diverge on mixed case)
  }
  return g;
}

export function toCanonicalText(g) {
  return JSON.stringify(toCanonical(g), null, 2) + "\n";
}

// CLI mode: rewrite all goldens in place.
if (process.argv[1] && process.argv[1].endsWith("canonicalize-goldens.mjs")) {
  const root = new URL("..", import.meta.url).pathname;
  const fixturesDir = join(root, "fixtures");
  for (const d of readdirSync(fixturesDir).filter(d => statSync(join(fixturesDir, d)).isDirectory())) {
    const p = join(fixturesDir, d, "golden", "plan.json");
    if (!existsSync(p)) continue; // skip non-fixture dirs (e.g. ingest-real without goldens yet)
    writeFileSync(p, toCanonicalText(JSON.parse(readFileSync(p, "utf8"))));
    console.log("canonicalized", d);
  }
}
