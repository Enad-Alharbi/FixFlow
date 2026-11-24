using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FixFlow.Api.Tests;

// No container here: the point is a database that cannot be reached at all.
public class UnreachableDatabaseTests
{
    [Fact]
    public async Task Ready_check_fails_fast_when_the_database_is_unreachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing"); // skips the Development-only startup migration
            builder.UseSetting("ConnectionStrings:Default",
                "Server=unreachable.invalid;Database=FixFlow;User Id=sa;Password=x;TrustServerCertificate=True");
        });
        var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Ready check took {stopwatch.Elapsed}.");
    }
}
