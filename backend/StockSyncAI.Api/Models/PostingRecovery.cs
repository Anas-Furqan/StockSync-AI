namespace StockSyncAI.Api.Models;

public static class PostingTransactionOutcomes
{
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Committed = "Committed";
    public const string RolledBack = "RolledBack";
    public const string Unknown = "Unknown";
}

public static class PostingRecoveryEventTypes
{
    public const string Prepared = "Prepared";
    public const string AttemptStarted = "AttemptStarted";
    public const string CommitConfirmed = "CommitConfirmed";
    public const string RollbackConfirmed = "RollbackConfirmed";
    public const string OutcomeBecameUnknown = "OutcomeBecameUnknown";
    public const string StartupInterruptedAttempt = "StartupInterruptedAttempt";
    public const string RecoveryInspected = "RecoveryInspected";
    public const string OperatorDecisionRecorded = "OperatorDecisionRecorded";
}

public static class PostingReconciliationDecisions
{
    public const string ReportedPosted = "ReportedPosted";
    public const string ReportedNotPosted = "ReportedNotPosted";
    public const string NeedsInvestigation = "NeedsInvestigation";
    public static bool IsValid(string value) => value is ReportedPosted or ReportedNotPosted or NeedsInvestigation;
}

public sealed record PostingAttemptRecord(Guid Id,Guid PostingId,int AttemptNumber,string CorrelationId,
    string TransactionOutcome,string LastOperation,DateTimeOffset StartedUtc,DateTimeOffset? CompletedUtc,
    string? SafeError);
public sealed record PostingRecoveryEventRecord(long Id,Guid PostingId,Guid? AttemptId,string EventType,
    string Source,string TechnicalOutcome,string Message,string? EvidenceJson,DateTimeOffset CreatedUtc);
public sealed record PostingReconciliationDecisionRecord(Guid Id,Guid PostingId,Guid? AttemptId,string Decision,
    string EvidenceNote,DateTimeOffset CreatedUtc);
public sealed record StartupRecoveryResult(int RecoveredCount,IReadOnlyList<Guid> PostingIds);
