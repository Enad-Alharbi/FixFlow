using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace FixFlow.Api.Tests;

/// <summary>
/// Starts a real SQL Server in Docker (same image as compose.yaml) and hosts the API in memory against it.
/// Shared by every test class in the "Api" collection, so the container starts once per test run.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _database =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU11-ubuntu-24.04").Build();

    public string ConnectionString => new SqlConnectionStringBuilder(_database.GetConnectionString())
    {
        InitialCatalog = "FixFlow_Tests",
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);

    public Task InitializeAsync() => _database.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
