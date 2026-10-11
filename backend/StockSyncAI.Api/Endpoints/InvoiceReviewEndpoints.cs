using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;
namespace StockSyncAI.Api.Endpoints;
public static class InvoiceReviewEndpoints
{
 public static IEndpointRouteBuilder MapInvoiceReviewEndpoints(this IEndpointRouteBuilder e){e.MapGet("/api/invoices/{id:guid}/review",Get);e.MapPost("/api/invoices/{id:guid}/review/match",Match);e.MapPut("/api/invoices/{id:guid}/review",Save);return e;}
 private static async Task<IResult> Get(Guid id,IInvoiceReviewService s,CancellationToken t)=>await Run(()=>s.GetAsync(id,t));
 private static async Task<IResult> Match(Guid id,MatchRequest? r,IInvoiceReviewService s,CancellationToken t)=>await Run(()=>s.MatchAsync(id,r?.ResetManualDecisions??false,t));
 private static async Task<IResult> Save(Guid id,SaveRequest? r,IInvoiceReviewService s,CancellationToken t)=>r?.Lines is null?Results.BadRequest(new{error="Review lines are required."}):await Run(()=>s.SaveAsync(id,r.Lines,r.SelectedVendorName,t));
 private static async Task<IResult> Run(Func<Task<InvoiceReviewResult>> action){try{return Results.Ok(await action());}catch(InvoiceNotFoundException){return Results.NotFound(new{error="The requested invoice was not found."});}catch(ReviewNotFoundException x){return Results.NotFound(new{error=x.Message});}catch(ReviewValidationException x){return Results.UnprocessableEntity(new{error=x.Message});}catch(ReviewPosUnavailableException x){return Results.Json(new{error=x.Message},statusCode:503);}}
 public sealed record MatchRequest(bool ResetManualDecisions=false);
 public sealed record SaveRequest(IReadOnlyList<ReviewLineUpdate> Lines,string? SelectedVendorName);
}
