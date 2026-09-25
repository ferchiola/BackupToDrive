using BackupToDrive.Configuration;

namespace BackupToDrive.Pipeline;

public sealed class PipelineContext
{
    public required JobOptions Job { get; init; }
    public required DateTime RunAt { get; init; }
    public bool DryRun { get; init; }
    public List<BackupArtifact> Artifacts { get; } = [];
    public List<string> Messages { get; } = [];
    public bool Failed { get; set; }
    public DriveSpaceReport? DriveSpace { get; set; }

    public void Info(string message) => Messages.Add(message);

    public void Fail(string message)
    {
        Failed = true;
        Messages.Add(message);
    }
}

public sealed class BackupArtifact
{
    public required string FullPath { get; set; }
    public required string FileName { get; set; }
    public long SizeBytes { get; set; }
    public string? DriveFileId { get; set; }
    public ArtifactStatus Status { get; set; } = ArtifactStatus.Pending;
    public string? Detail { get; set; }
}

public enum ArtifactStatus
{
    Pending,
    Compressed,
    Uploaded,
    Skipped,
    Failed
}

public sealed class DriveSpaceReport
{
    public string Used { get; init; } = "—";
    public string Total { get; init; } = "—";
    public string Free { get; init; } = "—";
    public string Days { get; init; } = "—";
    public string LastFile { get; init; } = "—";
    public int ThresholdDays { get; init; }
    public bool Alert { get; init; }
}
