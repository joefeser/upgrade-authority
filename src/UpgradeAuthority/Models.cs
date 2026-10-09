using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// ---- Input models (docs/schemas/input-schemas.md) ----

public sealed class ProducerEvidence
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("externalPackages")] public List<ExternalPackage> ExternalPackages { get; set; } = new();
    [JsonPropertyName("producers")] public List<ProducerEntry> Producers { get; set; } = new();
}

public sealed class ExternalPackage
{
    [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class ProducerEntry
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
    [JsonPropertyName("producedVersion")] public string? ProducedVersion { get; set; }
    [JsonPropertyName("publicationStatus")] public string? PublicationStatus { get; set; }
    [JsonPropertyName("evidenceNote")] public string? EvidenceNote { get; set; }
    // SPEC-020 §2: recorded ONCE at fusion time (sidecar ⇒ operator-declared, PackageProduced fact ⇒
    // project-declared, the future tracemap CI fact ⇒ ci-defined). Absent ⇒ the entry predates this
    // spec — classified by the in-band marker (EvidenceNote "tracemap PackageProduced (…)").
    [JsonPropertyName("provenance")] public string? Provenance { get; set; }
}

public sealed class OwnershipFile
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("selfTeamId")] public string SelfTeamId { get; set; } = "";
    [JsonPropertyName("ownerships")] public List<OwnershipEntry> Ownerships { get; set; } = new();
    [JsonPropertyName("mirrors")] public List<MirrorMapping> Mirrors { get; set; } = new();
}

public sealed class MirrorMapping
{
    [JsonPropertyName("canonical")] public string Canonical { get; set; } = "";
    [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();
}

public sealed class OwnershipEntry
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("team")] public string Team { get; set; } = "";
}

public sealed class PackageEvidence
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("facts")] public List<Fact> Facts { get; set; } = new();
    [JsonPropertyName("scanCoverage")] public List<ScanCoverage> ScanCoverage { get; set; } = new();
}

public sealed class Fact
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
    [JsonPropertyName("declaredConstraint")] public string DeclaredConstraint { get; set; } = "";
    [JsonPropertyName("format")] public string Format { get; set; } = "packagereference";
    [JsonPropertyName("constraintSource")] public string ConstraintSource { get; set; } = "Project";
    [JsonPropertyName("tfm")] public string? Tfm { get; set; }
    // SPEC-009 §3a: complete evidence span (each omitted when null) — apply verifies against it.
    [JsonPropertyName("line")] public int? Line { get; set; }
    [JsonPropertyName("endLine")] public int? EndLine { get; set; }
    [JsonPropertyName("projects")] public List<string> Projects { get; set; } = new();
    [JsonPropertyName("commitSha")] public string CommitSha { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

public sealed class ScanCoverage
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "complete";
    [JsonPropertyName("gaps")] public List<string> Gaps { get; set; } = new();
    [JsonPropertyName("notes")] public List<string>? Notes { get; set; } // SPEC-015: informational (compile health); never affects status
}

public sealed class DeltaFile
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sourceRepo")] public string SourceRepo { get; set; } = "";
    [JsonPropertyName("sourceCommitSha")] public string SourceCommitSha { get; set; } = "";
    [JsonPropertyName("changes")] public List<DeltaChange> Changes { get; set; } = new();
}

public sealed class DeltaChange
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("packageName")] public string PackageName { get; set; } = "";
    [JsonPropertyName("ecosystem")] public string Ecosystem { get; set; } = "";
    [JsonPropertyName("changeType")] public string ChangeType { get; set; } = "";
    [JsonPropertyName("oldVersion")] public string OldVersion { get; set; } = "";
    [JsonPropertyName("newVersion")] public string NewVersion { get; set; } = "";
}

public sealed class LockfileRows
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("rows")] public List<LockRow> Rows { get; set; } = new();

    [JsonIgnore] public bool IsV1 => SchemaVersion == "lockfile-rows.v1";
}

// SPEC-008: multi-repo envelope. Row shape and rules are exactly v1, applied per repo.
public sealed class LockfileRowsV2
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("repos")] public List<LockfileRepo> Repos { get; set; } = new();
}

public sealed class LockfileRepo
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("rows")] public List<LockRow> Rows { get; set; } = new();
}

public sealed class LockRow
{
    [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "unknown";
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("via")] public string? Via { get; set; }
    [JsonPropertyName("names")] public object? Names { get; set; }
    // SPEC-007: v1 row identity = (repo, lockfile, tfm, packageId). Null on frozen v0 rows.
    [JsonPropertyName("lockfile")] public string? Lockfile { get; set; }
    [JsonPropertyName("tfm")] public string? Tfm { get; set; }
    // SPEC-019 §4: deps.json rows carry their reserved upstream placeholders verbatim —
    // lockfile rows never set it. Joins row identity (SameRow compares it).
    [JsonPropertyName("provenance")] public RowProvenance? Provenance { get; set; }
}

public sealed class RowProvenance
{
    [JsonPropertyName("manifestSha256")] public string ManifestSha256 { get; set; } = "";
    [JsonPropertyName("freshness")] public string Freshness { get; set; } = "unknown";
    [JsonPropertyName("buildCommitSha")] public string BuildCommitSha { get; set; } = "unknown";
}

// SPEC-019 §5.3: the freshness channel — scan-estate computes it (repo access), the fixture carries
// it, the planner gates build-resolved closures on it. Absent file ⇒ conservative.
public sealed class BuildFreshnessFile
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("repos")] public List<BuildFreshnessEntry> Repos { get; set; } = new();
}

public sealed class BuildFreshnessEntry
{
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("freshness")] public string Freshness { get; set; } = "";
    [JsonPropertyName("basis")] public string? Basis { get; set; }
}

// ---- Plan model (plan.v1), serialized by Canonical in fixed key order ----

public sealed class Plan
{
    public string SchemaVersion = "plan.v1";
    public PlanDelta Delta = new();
    public List<PlanRepo> Repos = new();
    public PlanScope? Scope; // SPEC-017: emitted only when a scope input exists (echo of config, never a coverage claim)
    public List<PlanWave> Waves = new();
    public PlanStop? Stop;
    public PlanUncertainty Uncertainty = new();
}

// SPEC-017 §5: the estate scope echo. outOfScope omitted when empty (exclude-mode with no
// exclude names cannot occur — it validates to no scope); includedCount only in include mode.
public sealed class PlanScope
{
    public string Mode = "exclude";
    public int? IncludedCount;
    public List<string>? OutOfScope;
}

public sealed class PlanDelta
{
    public string PackageName = "", Ecosystem = "", ChangeType = "", OldVersion = "", NewVersion = "";
    public string? Origin;
}

public sealed class PlanRepo
{
    public string Repo = "";
    public string Classification = "";
    public List<string> Reasons = new();
    public string Ownership = "";
    public string ActionType = "";
    public List<string> EvidenceKinds = new();
    public (string Rung, int Corroboration) Confidence = ("declared", 1);
    public List<string>? ScanNotes; // SPEC-015: compile-health notes, emitted only when non-empty
}

public sealed class PlanWave
{
    public int Index;
    public string Status = "ready";
    public string? BlockedOn;
    public string? Condition;
    public List<string> Prerequisites = new();
    public List<PlanUnit> ReleaseUnits = new();
}

public sealed class PlanUnit
{
    public string Repo = "";
    public List<string> Packages = new();
    public string Basis = "assumed-same-repo";
    public List<string>? Notes;
    public SortedDictionary<string, string>? PublicationStatus;
}

public sealed class PlanStop
{
    public string Reason = "";
    public string Detail = "";
    public List<string>? CyclePath;
}

public sealed class PlanUncertainty
{
    public List<PlanContradiction> Contradictions = new();
    public List<PlanGap> Gaps = new();
    public List<PlanFinding>? Findings; // SPEC-007: emitted only when non-empty (like notes/stop)
}

public sealed class PlanFinding
{
    public string Subject = "";
    public string Detail = "";
}

public sealed class PlanContradiction
{
    public string Id = "";
    public string Subject = "";
    public List<(string Repo, string PackageId)> Claims = new();
    public List<string>? DownstreamProvisional;
}

public sealed class PlanGap
{
    public string Subject = "";
    public string Detail = "";
}
