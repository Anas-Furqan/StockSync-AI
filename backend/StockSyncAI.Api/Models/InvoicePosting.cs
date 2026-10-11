namespace StockSyncAI.Api.Models;
public static class PostingStates{public const string Prepared="Prepared";public const string Posting="Posting";public const string Posted="Posted";public const string FailedSafeToRetry="FailedSafeToRetry";public const string OutcomeUnknown="OutcomeUnknown";public const string Blocked="Blocked";}
public sealed record InvoicePostingRecord(Guid Id,Guid InvoiceId,string ReviewFingerprint,string SnapshotJson,string State,int AttemptCount,string? SafeError,string? ResultJson,DateTimeOffset? ConfirmedUtc,DateTimeOffset CreatedUtc,DateTimeOffset UpdatedUtc);
