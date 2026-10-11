using StockSyncAI.Api.Models;namespace StockSyncAI.Api.Database;
public interface IInvoicePostingRepository
{
 Task<InvoicePostingRecord?> GetAsync(Guid invoiceId,string fingerprint,CancellationToken token=default);
 Task<InvoicePostingRecord?> GetByIdAsync(Guid id,CancellationToken token=default);
 Task<IReadOnlyList<InvoicePostingRecord>> ListAsync(Guid invoiceId,CancellationToken token=default);
 Task<InvoicePostingRecord> PrepareAsync(Guid invoiceId,string fingerprint,string snapshot,DateTimeOffset now,CancellationToken token=default);
 Task<PostingAttemptRecord?> TryStartAsync(Guid id,DateTimeOffset now,CancellationToken token=default);
 Task SetOutcomeAsync(Guid id,string state,string? error,string? result,DateTimeOffset? confirmed,DateTimeOffset now,CancellationToken token=default);
 Task<StartupRecoveryResult> RecoverInterruptedAsync(DateTimeOffset now,CancellationToken token=default);
 Task<IReadOnlyList<PostingAttemptRecord>> ListAttemptsAsync(Guid postingId,CancellationToken token=default);
 Task<IReadOnlyList<PostingRecoveryEventRecord>> ListEventsAsync(Guid postingId,CancellationToken token=default);
 Task<IReadOnlyList<PostingReconciliationDecisionRecord>> ListDecisionsAsync(Guid postingId,CancellationToken token=default);
 Task AppendRecoveryInspectionAsync(Guid postingId,Guid requestId,string message,DateTimeOffset now,CancellationToken token=default);
 Task<PostingReconciliationDecisionRecord> RecordDecisionAsync(Guid postingId,Guid requestId,string decision,string evidenceNote,DateTimeOffset now,CancellationToken token=default);
}
