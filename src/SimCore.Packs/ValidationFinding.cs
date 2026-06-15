namespace SimCore.Packs;

/// <summary>Error = pack is broken/unplayable (an importer should refuse to start a
/// match). Warning = unusual but legal (surfaced to the author, never blocks).</summary>
public enum Severity { Error, Warning }

/// <summary>One author-facing validator result. <see cref="Code"/> is a STABLE
/// machine-readable identifier (the fix-it contract); <see cref="TargetId"/> is the
/// offending unit/building/upgrade id (null for faction-level findings).</summary>
public sealed record ValidationFinding(Severity Severity, string Code, string? TargetId, string Message);
