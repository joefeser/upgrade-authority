// Shared: SPEC-003 §4.1 template set. Single source for the formatter
// (prerequisite ordering) and the validator (exhaustive-mapping check).
export const TEMPLATE_ORDER = ["T1", "T4", "T5", "T6a", "T7a", "T8a", "T9", "T6", "T7", "T8"];

export const TEMPLATES = [
  ["T1", "{producer} publishes {pkg} >= {version} (stated, not verified live)"],
  ["T4", "{producer} publishes {pkg} rebuilt against {dep} {depVersion} (stated, not verified live)"],
  ["T5", "{pkg} {version} available from the public feed ({pkg} declared external via producer-evidence.v0; stated, not verified live)"],
  ["T6", "external {team} releases {pkg} >= {version} (request/await — outside our change authority; escalation path is the coordination step, not a PR)"],
  ["T6a", "{pkg} >= {version} available and restorable from the consumer environment (stated, not verified live)"],
  ["T7", "awaiting publication of {pkg}: producer-evidence.v0 declares {pkg} publicationStatus = unpublished — {repo}'s wave can never be 'ready' while the sidecar says unpublished"],
  ["T7a", "{producer} publishes {pkg} (cite: producer-evidence.v0 {pkg} publicationStatus = unpublished; stated, not verified live)"],
  ["T8", "producer of {pkg} is unknown — no producer wave is derivable; {repo}'s wave proceeds only after the producer is identified and {pkg} {version} is available"],
  ["T8a", "{pkg} {version} exists on the configured feed and authenticated restore succeeds (stated, not verified live)"],
  ["T9", "{cid} resolved: authoritative producer of {pkg} determined by human decision; then {pkg} {version} published (stated, not verified live)"],
].map(([id, tpl]) => {
  const escaped = tpl.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\\\{[a-zA-Z]+\\\}/g, "(.+?)");
  return [id, tpl, new RegExp(`^${escaped}$`)];
});

export function templateMatches(str) {
  return TEMPLATES.filter(([, , rx]) => rx.test(str)).length;
}

export function templateRank(str) {
  for (const [id, , rx] of TEMPLATES) if (rx.test(str)) return TEMPLATE_ORDER.indexOf(id);
  return TEMPLATE_ORDER.length; // unmatched sorts last; validator flags it anyway
}

// SPEC-007 §6 finding templates. T10 carries an optional T10a honesty trailer
// (unsupported lockfile groups), so the matcher strips a trailing T10a first.
export const FINDING_TEMPLATES = [
  [
    "T10",
    "{repo} resolves {pkg} to {n} versions across its lockfile/TFM resolution groups: {resolutions} — evidenced disagreement, not an error; V0 schedules {repo} once at unit level and picks no winner (convergence is deliberately out of scope, §10)",
  ],
  [
    "T10a",
    "; {repo} also has lockfile groups the scanner could not parse and contributed no rows — those resolutions stay unevidenced (coverage gap)",
  ],
].map(([id, tpl]) => {
  const escaped = tpl.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\\\{[a-zA-Z]+\\\}/g, "(.+?)");
  return [id, tpl, new RegExp(`^${escaped}$`)];
});

export function findingTemplateMatches(str) {
  // T10a is a trailer: strip one match from the end, then T10 must match the rest exactly.
  const t10a = FINDING_TEMPLATES.find(([id]) => id === "T10a");
  const t10 = FINDING_TEMPLATES.find(([id]) => id === "T10");
  let rest = str;
  let sawTrailer = false;
  for (let i = str.length; i >= 0; i--) {
    const tail = str.slice(i);
    if (tail.startsWith(";") && t10a[2].test(tail)) { rest = str.slice(0, i); sawTrailer = true; break; }
  }
  return t10[2].test(rest) && (!sawTrailer || rest.length < str.length) ? 1 : 0;
}
