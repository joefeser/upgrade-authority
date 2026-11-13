# The Upgrade Plan — design brief

2026-11-10 design talk, rounds 1–3. This is the agreement before any code. The per-package
engine (delta → plan → report → apply → push) is **unchanged**; everything here is the layer
that drives it at estate scale. Sanitized examples throughout (`UASamples.*` = internal,
coverlet/Newtonsoft/EFCore = public).

## The goal, one line

npm outdated + npm-overrides + npm update for .NET estates, **TFM-aware**: group repos by
target framework (the 8's, the 10's; framework *upgrades* are phase 2 — flags only), discover
per package how far it can go **on that TFM**, show the human, let them peg, then update.

## The two surfaces

1. **`ua <repo> update [-keep-dot-net]`** — the daily driver. Moves every live pin in one repo
   to its **allowedMax** (peg- and compatibility-bounded), whatever major distance: a repo at
   coverlet 6 with a peg allowing 8.x goes to 8.x; Newtonsoft 11 goes to 13. Already-current
   pins never downgrade; prerelease/floating/redacted sites stay unresolved-and-visible (N3,
   unchanged). `-update-dot-net` is a **reserved loud no-op in V0** (TFM-touching moves need
   compat-cache proof plus an explicit hand; defined now so the flag exists).
2. **`ua upgrade-plan`** — the estate view, per TFM group × package: pins observed /
   `potentialMax` / `allowedMax` / status / license-shift flags. The VS nuget dialog,
   blanket. Read-only — the only editable artifact in this whole design is the peg list.

## The artifacts

1. **Compatibility cache** — one JSON file per package, the work-proven format:
   `{ id, versionsInspected, totalVersionsAvailable, fetchedUtc, allVersionsSimple[],
   usedVersions { version: [consumers] }, usedByRepos[], versions[]: { version,
   supportsNetStandard, inUse, isLatestForMajor, targetFrameworks[], supportsNet8,
   supportsNet10, license? } }`. The `usedVersions`/`usedByRepos` back-references ride in the
   same file so depends-lookups never rescan. ~900 lazy-read files parsed into a dictionary
   is proven fast; JSON-first, a DB is a later optimization (WITS-shaped service if it ever
   grows one — ua reads files, always).
   **Probe** = download the .nupkg (it is a zip) and list `lib/**` — the folder names ARE the
   supported TFMs; dependency groups ride in the embedded .nuspec. One download per
   package-version; cache per major. Per-TFM booleans are derived projections of
   `targetFrameworks` + the netstandard rules (netstandard2.0 → every netcoreapp/net5+).
   **Ceiling derivation**: a repo on TFM `t` can take the max version whose TFMs are
   compatible with `t`. The live proof: an internal package shipping 15.0.x (net10.0 only)
   and 13.0.2 (net8.0) caps net8 repos at 13.0.2 while net10 repos take 15.0.4.
   **Probe selection rule (V1, from the work-proven tool)**: probe latest-per-major + every
   in-use version + event-driven versions (patch-day targets); `allVersionsSimple` lists the
   rest cheaply. Per-version records are also the defense against the silent-TFM-bump
   failure: a package that retargets net10 **without bumping its major** (14.0.4 net10-only
   while consumers stay net8) is invisible to net8 repos — the ceiling simply stays at the
   last compatible version, automatically. **V2 backlog: a content hash per cached version**
   (republish detection — when a version's bytes change underneath its number) **and the
   internal level-cascade checks** (packages are layered 1–5; the plan flags level
   misalignment — "level 3 pinned 15.0.2 while its dependency moved to 15.0.4" — and treats
   pegs on internal packages as a smell: the policy is trust the cascade, don't pin APIs to
   lower levels). **Patch day is the headline scenario for surface 2**: a .NET security
   release means the whole chain updates — on the order of a hundred-fifty PRs in two days,
   which is exactly `update --all-max` + the existing push machinery.
2. **`pegs.v0`** — master (org-shared) + estate override. Three bound shapes: **exact** ("pegged at
   13.0.3"), **maxExclusive** (`< 4.20` — Moq), **forceMax** (LiteBus), plus the **allowPrerelease**
   flag (2026-11-13, Joe's forgotten knob): the operator opts a package into prerelease eligibility —
   without it every ceiling is stable-only, which made prerelease-only feeds (System.CommandLine
   family) permanently compat-blocked. The flag OR-merges across master/estate (either operator
   turning it on is a deliberate act — the stated exception to tighten-only). Bare
   `{"packageName": "…", "allowPrerelease": true}` entries are legal. **No cross-package peer
   ranges** — not npm's angular-typescript style; pegs bind one package each. Human `notes` ride along.
3. **Onboarding review file** — when a package's TFM chain cannot be auto-determined
   (`discover-max` fails or a new major appears overnight), ua writes a candidate file (delta-
   style: human edits it or throws lines out, then applies). `--dont-ask-again` promotes the
   line to the **master** cache/peg list; removing the flag there re-asks. The human approves
   (not a PR — dependabot eats line feeds).
4. **Upgrade-plan report** — read-only; the review surface for surface 2. Carries a
   **license column** per major: a license shift between majors (Polly-class) flags the row;
   "paid now" is not auto-detected — a peg + note does that.

## The law (semantics)

- **Pin scope (V1)**: live pins only — `PackageReference Version` attributes and CPM
  `PackageVersion` entries that a live reference actually uses. **Dead CPM entries are out of
  scope** (the pin with no dll behind it — Microsoft's cleanup failure, maybe V2 hygiene).
- **Update target** = `allowedMax` per package = `min(potentialMax, pegs, compat(repo TFM))`.
  - external `potentialMax` = feed latest (already classified by drift).
  - internal `potentialMax` = what the source repo publishes (registry knows producers —
    "source versions"); its per-TFM max comes from the version's own targetFrameworks.
  - cross-major moves are in scope (explicitly — the net10-api-still-on-coverlet-6 case).
  - already-at-or-above-target sites are never downgraded; every edit site re-checks against
    the actual file and refuses as stale — the apply law, unchanged.
- **Compat gate**: a version whose TFMs don't admit the repo's TFM is invisible to that repo's
  update — the ceiling, not an error.
- **License shift** between majors flags the package in every surface.
- **`-keep-dot-net`** is the V0 default behavior made explicit: package updates only, TFMs
  untouched. Framework-update FLAGS are phase 2.

## Verbs (proposed)

- `ua discover-max [--dotnet N] [--package <id>]` — probe/cache/onboarding-file generator
  (bulk first hit once, then one probe per new major, forever a lookup).
- `ua <repo> update [-keep-dot-net]` — surface 1.
- `ua upgrade-plan [--dotnet N]` — surface 2's report.
- Campaign verbs (`delta`/`plan`/`report`/`apply`/`push`) — unchanged; the engine underneath
  both surfaces. Pegs and the compat cache feed the planner as inputs.

## V1 / later

- **Later**: package→package update chains ("what is the chain of updates needed" — report
  edges first, ordering later); dead-CPM cleanup; `-update-dot-net` real; cache served by a
  WITS-shaped service; framework upgrades (phase 2 — flags first).
- **Deliberately not modeled**: peer-range constraints between packages; policing packages
  that lie about their TFM — pegs are the guard for the repeat offenders, and when a liar
  breaks us, we peg.
