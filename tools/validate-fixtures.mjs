#!/usr/bin/env node
// Structural validator for the V0 fixture corpus (authoring-time sanity).
// Enforces corpus-wide invariants (fixtures/MANIFEST.md) and the SPEC-003 §4
// determinism contract: canonical serialization bytes (in-memory, no mutation)
// and exhaustive template mapping for every prerequisite/condition string.
// These are STRUCTURAL checks mirrored from the schema and specs — full
// JSON-Schema validation and input→golden execution land with SPEC-005.
import { readFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { join } from "node:path";
import { toCanonicalText } from "./canonicalize-goldens.mjs";
import { templateMatches, findingTemplateMatches } from "./templates.mjs";

const classifications = new Set(["affected", "not-affected", "unknown"]);
const ownerships = new Set(["self", "external", "unknown"]);
const actions = new Set(["executable", "external-request-await", "none", "unknown"]);
const statuses = new Set(["ready", "provisional", "conditional"]);
const basis = new Set(["assumed-same-repo", "evidenced"]);
const pubs = new Set(["published", "unpublished", "unknown"]);
const rungs = new Set(["receipt", "manifest", "evaluated", "declared", "fixture-declared"]);

const root = new URL("..", import.meta.url).pathname;
const fixturesDir = join(root, "fixtures");
let fails = 0;

// SPEC-018: drift.v1 canonical bytes (mirrors Drift.Write's fixed key order — the C# writer owns the shape).
// ord is STRICT: unknown keys are dropped from the rendering, so a golden carrying an out-of-shape
// property fails the byte comparison instead of passing silently (PR #3 Baz round 2).
function toDriftCanonicalText(d) {
  const ord = (o, keys) => { const r = {}; for (const k of keys) if (k in o) r[k] = o[k]; return r; };
  d = ord(d, ["schemaVersion", "feed", "summary", "packages"]);
  d.feed = ord(d.feed ?? {}, ["source", "asOf", "packageCount"]);
  d.summary = ord(d.summary ?? {}, ["packagesObserved", "behind", "current", "ahead", "unclassified", "unknownFeedPackages"]);
  d.packages = (d.packages ?? []).map(p => {
    p = ord(p, ["packageId", "latest", "statuses", "installations"]);
    p.statuses = ord(p.statuses ?? {}, ["behind", "current", "ahead", "unclassified"]);
    p.installations = (p.installations ?? []).map(i => ord(i, ["repo", "version", "evidence", "status", "reason"]));
    return p;
  });
  return JSON.stringify(d, null, 2) + "\n";
}

const dirs = readdirSync(fixturesDir).filter(d => statSync(join(fixturesDir, d)).isDirectory());
for (const d of dirs) {
  const p = join(fixturesDir, d, "golden", "plan.json");
  if (!existsSync(p)) { console.log(`FAIL ${d}: no golden plan.json`); fails++; continue; }
  let g, text;
  try { text = readFileSync(p, "utf8"); g = JSON.parse(text); }
  catch (e) { console.log(`FAIL ${d}: invalid JSON (${e.message})`); fails++; continue; }
  const errs = [];
  if (g.schemaVersion !== "plan.v1") errs.push("schemaVersion");
  for (const k of ["delta", "repos", "waves", "uncertainty"]) if (!(k in g)) errs.push("missing " + k);
  // SPEC-017 §5: optional scope echo — mode include|exclude; outOfScope is a string array when
  // present (omitted when empty); include mode carries an integer includedCount
  if (g.scope !== undefined) {
    if (!["include", "exclude"].includes(g.scope?.mode)) errs.push("scope mode " + g.scope?.mode);
    if (g.scope?.outOfScope !== undefined && (!Array.isArray(g.scope.outOfScope) || g.scope.outOfScope.some(x => typeof x !== "string")))
      errs.push("scope outOfScope shape");
    if (g.scope?.mode === "include" && !Number.isInteger(g.scope?.includedCount)) errs.push("scope includedCount");
    if (g.scope?.mode === "exclude" && g.scope?.includedCount !== undefined) errs.push("scope includedCount in exclude mode");
  }
  if (!g.delta?.packageName || !g.delta?.newVersion) errs.push("delta fields");
  (g.repos ?? []).forEach(r => {
    for (const k of ["classification", "reasons", "ownership", "actionType", "evidenceKinds", "confidence"])
      if (!(k in r)) errs.push("repo missing " + k + " " + r.repo);
    if (!classifications.has(r.classification)) errs.push("classification " + r.repo);
    if (!ownerships.has(r.ownership)) errs.push("ownership " + r.repo);
    if (!actions.has(r.actionType)) errs.push("actionType " + r.repo);
    if (r.ownership === "external" && r.actionType !== "external-request-await") errs.push("external-but-not-request-await " + r.repo);
    if (!Array.isArray(r.reasons) || r.reasons.length < 1) errs.push("reasons " + r.repo);
    if (!Array.isArray(r.evidenceKinds) || r.evidenceKinds.length < 1) errs.push("evidenceKinds " + r.repo);
    if (!rungs.has(r.confidence?.rung) || !Number.isInteger(r.confidence?.corroboration) || r.confidence?.corroboration < 1) errs.push("confidence " + r.repo);
    if (r.classification === "not-affected" && !(r.reasons ?? []).some(x => /complete|positive|lockfile/i.test(x)))
      errs.push("not-affected without positive evidence " + r.repo);
  });
  (g.waves ?? []).forEach(w => {
    if (!Number.isInteger(w.index) || w.index < 1) errs.push("wave index");
    if (!statuses.has(w.status)) errs.push("wave status " + w.index);
    if (w.status === "provisional" && !w.blockedOn) errs.push("provisional without blockedOn");
    if (w.status === "conditional" && !w.condition) errs.push("conditional without condition");
    if (!Array.isArray(w.releaseUnits) || w.releaseUnits.length < 1) errs.push("wave " + w.index + " releaseUnits");
    (w.releaseUnits ?? []).forEach(u => {
      if (typeof u.repo !== "string" || !u.repo) errs.push("unit repo");
      if (!Array.isArray(u.packages)) errs.push("unit packages " + u.repo);
      if (!basis.has(u.basis)) errs.push("basis " + u.repo);
      if (u.publicationStatus) Object.values(u.publicationStatus).forEach(v => { if (!pubs.has(v)) errs.push("publicationStatus " + v); });
    });
    for (const s of [...(w.prerequisites ?? []), ...(w.condition ? [w.condition] : [])]) {
      const hits = templateMatches(s);
      if (hits !== 1) errs.push(`template match count ${hits}: ${s.slice(0, 60)}…`);
    }
  });
  if (g.stop) {
    if (g.stop.reason !== "CYCLE_DETECTED" && g.stop.reason !== "HUMAN_DECISION_REQUIRED") errs.push("stop reason");
    if ((g.waves ?? []).length !== 0) errs.push("typed stop with waves emitted");
    if (g.stop.reason === "CYCLE_DETECTED" && (!Array.isArray(g.stop.cyclePath) || g.stop.cyclePath.length < 2)) errs.push("cycle path missing/too short");
  }
  if (!Array.isArray(g.uncertainty?.contradictions) || !Array.isArray(g.uncertainty?.gaps)) errs.push("uncertainty shape");
  // SPEC-007: findings (optional) must map to exactly one template (T10, optional T10a trailer)
  if (g.uncertainty?.findings !== undefined) {
    if (!Array.isArray(g.uncertainty.findings) || g.uncertainty.findings.length === 0) errs.push("findings shape (empty array must be omitted)");
    else g.uncertainty.findings.forEach(f => {
      if (!f.subject || !f.detail) errs.push("finding fields");
      if (findingTemplateMatches(f.detail ?? "") !== 1) errs.push(`finding template match count: ${(f.detail ?? "").slice(0, 60)}…`);
    });
  }
  // SPEC-012 §4.3: trailer-once — a repo whose coverage gaps name unsupported lockfile groups and
  // that has >=1 finding carries T10a on EXACTLY ONE finding (the subject-first); zero or >1 fails.
  {
    const unsupported = (g.repos ?? []).some(() => true); // repos known; coverage gaps come from input below
    const covPath = join(fixturesDir, d, "input", "package-evidence.v0.json");
    if (existsSync(covPath)) {
      try {
        const cov = JSON.parse(readFileSync(covPath, "utf8")).scanCoverage ?? [];
        const unsupportedByRepo = new Map();
        for (const c of cov)
          unsupportedByRepo.set(c.repo, (c.gaps ?? []).some(g => g.includes("packages-lock-group-unsupported") || g.includes("target-framework group is unsupported")));
        const findings = g.uncertainty?.findings ?? [];
        if (findings.length > 0) {
          const byRepo = {};
          for (const f of findings) {
            const repo = (f.subject ?? "").split(" version disagreement:")[0];
            byRepo[repo] ??= { trailers: 0, trailerFirst: false, firstSubject: null };
            const isTrailer = typeof f.detail === "string" && f.detail.includes("could not parse and contributed no rows");
            if (byRepo[repo].firstSubject === null) { byRepo[repo].firstSubject = f.subject; byRepo[repo].trailerFirst = isTrailer; }
            if (isTrailer) byRepo[repo].trailers++;
          }
          for (const [repo, st] of Object.entries(byRepo)) {
            if (st.trailers > 1) errs.push(`T10a trailer appears ${st.trailers}x for ${repo} (once per repo, SPEC-012 §2.2)`);
            const qualifies = unsupportedByRepo.get(repo) === true; // per-REPO coverage, not run-wide
            if (qualifies && st.trailers !== 1) errs.push(`repo ${repo} qualifies for T10a (its own coverage names unsupported lockfile groups) but carries ${st.trailers} trailers (expected exactly 1)`);
            if (qualifies && st.trailers === 1 && !st.trailerFirst) errs.push(`repo ${repo}: T10a trailer is not on the subject-first finding (SPEC-012 §2.2)`);
          }
        }
      } catch { /* malformed coverage input is the ingest contract's problem */ }
    }
  }
  // SPEC-007 §5.6 / SPEC-008 §5.3: evidence-kind provenance — the lockfile-rows.* kind cites the input's actual schemaVersion
  {
    const versions = ["v0", "v1", "v2"].map(v => [v, existsSync(join(fixturesDir, d, "input", `lockfile-rows.${v}.json`))]);
    const present = versions.filter(([, e]) => e);
    if (present.length > 1) errs.push("multiple lockfile input files present: " + present.map(([v]) => v).join(","));
    if (present.length === 1) {
      const [want] = present[0];
      const wrongs = ["v0", "v1", "v2"].filter(v => v !== want).map(v => `lockfile-rows.${v}`);
      (g.repos ?? []).forEach(r => {
        for (const w of wrongs) if ((r.evidenceKinds ?? []).includes(w)) errs.push(`evidence-kind provenance ${r.repo}: cites ${w} but input is lockfile-rows.${want}`);
      });
    }
  }
  // repos[] schedule-order check (§4.2): repos appearing in waves must be ordered
  // by (first containing wave index, repo name); the formatter cannot know this
  // semantic order, so the validator enforces it against the plan's own waves.
  {
    const firstWave = new Map();
    (g.waves ?? []).forEach(w => (w.releaseUnits ?? []).forEach(u => { if (!firstWave.has(u.repo)) firstWave.set(u.repo, w.index); }));
    const scheduled = (g.repos ?? []).filter(r => firstWave.has(r.repo)).map(r => r.repo);
    const expected = [...firstWave.entries()].sort((a, b) => a[1] - b[1] || a[0].localeCompare(b[0])).map(([r]) => r);
    if (JSON.stringify(scheduled) !== JSON.stringify(expected)) errs.push("repos[] not in wave-schedule order");
  }
  // Canonical serialization — in-memory comparison; the corpus is never mutated here.
  if (text !== toCanonicalText(g)) errs.push("non-canonical serialization bytes");
  // SPEC-018: drift.v1 goldens — structural contract (the C# writer owns the bytes; selftest compares them)
  {
    const driftPath = join(fixturesDir, d, "golden", "drift.v1.json");
    if (existsSync(driftPath)) {
      try {
        const driftText = readFileSync(driftPath, "utf8");
        const dr = JSON.parse(driftText);
        if (driftText !== toDriftCanonicalText(dr)) errs.push("drift non-canonical serialization bytes");
        const statuses = new Set(["behind", "current", "ahead", "unclassified", "unknown-feed"]);
        if (dr.schemaVersion !== "drift.v1") errs.push("drift schemaVersion");
        if (!["operator-provided", "folder-feed"].includes(dr.feed?.source)) errs.push("drift feed source");
        if (!Number.isInteger(dr.feed?.packageCount) || dr.feed.packageCount < 0) errs.push("drift packageCount");
        for (const k of ["packagesObserved", "behind", "current", "ahead", "unclassified", "unknownFeedPackages"])
          if (!Number.isInteger(dr.summary?.[k]) || dr.summary[k] < 0) errs.push("drift summary " + k);
        const pkgs = dr.packages ?? [];
        if (dr.summary.packagesObserved !== pkgs.length) errs.push("drift summary/packages mismatch");
        const ids = pkgs.map(p => p.packageId);
        if (JSON.stringify(ids) !== JSON.stringify([...ids].sort((a, b) => a < b ? -1 : a > b ? 1 : 0))) errs.push("drift packages not ordinal-sorted");
        let nBehind = 0, nCurrent = 0, nAhead = 0, nUnc = 0, nUnknown = 0;
        for (const p of pkgs) {
          const rows = p.installations ?? [];
          if (rows.length === 0) errs.push("drift package with no installations " + p.packageId);
          const anyUnknown = rows.some(r => r.status === "unknown-feed");
          if (anyUnknown && p.latest !== null) errs.push("drift unknown-feed package with a latest " + p.packageId);
          if (!anyUnknown && p.latest == null) errs.push("drift known package without a latest " + p.packageId);
          const rowKeys = rows.map(r => [r.repo, r.version]);
          if (JSON.stringify(rowKeys) !== JSON.stringify([...rowKeys].sort((a, b) => a[0].localeCompare(b[0]) || a[1].localeCompare(b[1])))) errs.push("drift installations not sorted " + p.packageId);
          for (const r of rows) {
            if (!statuses.has(r.status)) errs.push("drift row status " + r.status);
            if (!["lockfile", "declared-pin", "deps.json"].includes(r.evidence)) errs.push("drift row evidence " + r.evidence);
            if ((r.status === "unclassified") !== (r.reason != null)) errs.push("drift reason must ride unclassified rows only " + p.packageId);
          }
          nBehind += rows.filter(r => r.status === "behind").length;
          nCurrent += rows.filter(r => r.status === "current").length;
          nAhead += rows.filter(r => r.status === "ahead").length;
          nUnc += rows.filter(r => r.status === "unclassified").length;
          if (anyUnknown) nUnknown++;
          for (const k of ["behind", "current", "ahead", "unclassified"])
            if ((p.statuses?.[k] ?? -1) !== rows.filter(r => r.status === k).length) errs.push(`drift per-package statuses ${k} ${p.packageId}`);
        }
        if (dr.summary.behind !== nBehind || dr.summary.current !== nCurrent || dr.summary.ahead !== nAhead
          || dr.summary.unclassified !== nUnc || dr.summary.unknownFeedPackages !== nUnknown)
          errs.push("drift summary counts do not match packages[]");
        const deltasDir = join(fixturesDir, d, "golden", "deltas");
        if (existsSync(deltasDir)) {
          for (const df of readdirSync(deltasDir).filter(f => f.endsWith(".delta.json"))) {
            try {
              const dd = JSON.parse(readFileSync(join(deltasDir, df), "utf8"));
              if (dd.version !== "package-delta.v1" || dd.sourceCommitSha !== "0".repeat(40)) errs.push("drift delta envelope " + df);
              if (dd.changes?.length !== 1) errs.push("drift delta single-change " + df);
              const c = dd.changes?.[0];
              if (c && (c.ecosystem !== "nuget" || c.changeType !== "updated" || !c.id?.startsWith("drift-") || !c.packageName || !c.oldVersion || !c.newVersion)) errs.push("drift delta change shape " + df);
              if (c && !ids.includes(c.packageName)) errs.push("drift delta packageName not an observed estate spelling " + df);
            } catch (e) { errs.push("drift delta parse " + df); }
          }
        }
      } catch (e) { errs.push("drift.v1 parse"); }
    }
  }
  if (errs.length) { console.log(`FAIL ${d}: ${errs.join("; ")}`); fails++; }
  else console.log(`ok   ${d}`);
}
console.log(fails === 0
  ? `ALL GOLDENS PASS STRUCTURAL + TEMPLATE + CANONICAL-BYTE CHECKS (${dirs.length})`
  : `${fails} FIXTURES FAILED`);
process.exit(fails === 0 ? 0 : 1);
