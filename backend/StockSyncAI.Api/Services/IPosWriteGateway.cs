using StockSyncAI.Api.Models;namespace StockSyncAI.Api.Services;
public interface IPosWriteGateway{Task<PosPostingResult> PostAsync(PostingSnapshot snapshot,CancellationToken token=default);}
public sealed class PosConfirmedRollbackException(string message,Exception? inner=null):Exception(message,inner);
public sealed class PosOutcomeUnknownException(string message,Exception? inner=null):Exception(message,inner);
