using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StockSyncAI.Tests.Api;

public sealed class InvoiceReviewEndpointTests
{
    [Fact]
    public async Task ReviewRoutes_RequireAuthenticationAndRejectMissingInvoiceSafely()
    {
        using var factory=new HealthEndpointTests.ApiFactory();
        using var client=factory.CreateClient(); var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/invoices/{id}/review")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync($"/api/invoices/{id}/review/match",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PutAsJsonAsync($"/api/invoices/{id}/review",new{lines=Array.Empty<object>()})).StatusCode);
        client.DefaultRequestHeaders.Add("X-StockSync-Token","test-secret-with-at-least-32-characters");
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/invoices/{id}/review")).StatusCode);
    }
}
