using System.Net;
using System.Text.Json;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace FixFlow.Api.Tests;

[Collection("Api")]
public class FoundationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ready_health_check_reaches_the_database()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_code()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("resource.not_found", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Database_uses_the_agreed_collation()
    {
        factory.CreateClient(); // ensures the app started, so startup migration created the database

        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // String literals compare using the database's default collation.
        command.CommandText = """
            SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)),
                   CASE WHEN N'مُحَمَّد' = N'محمد' THEN 1 ELSE 0 END,
                   CASE WHEN N'FixFlow' = N'fixflow' THEN 1 ELSE 0 END
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal(FixFlowDbContext.DatabaseCollation, reader.GetString(0));
        // Documents a known limit (ADR 0003): tashkeel is NOT ignored, so Arabic search needs a normalized column.
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2)); // case ignored
    }
}
