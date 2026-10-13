using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BankingBrain.IntegrationTests;

public sealed class BankingBrainApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName =
        $"BankingBrainTest_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Host=localhost;Port=5432;Database={_databaseName};Username=raghavendra";

    public string ConnectorSourcesRoot { get; } =
        Path.Combine(
            Path.GetTempPath(),
            $"BankingBrainTestSources_{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                    ["Jwt:Secret"] = "THIS_IS_A_DEVELOPMENT_SECRET_KEY_CHANGE_IN_PRODUCTION_32CHARS_MIN",
                    ["Seed:AdminEmail"] = "admin@brain.ai",
                    ["Seed:AdminPassword"] = "Password@123",
                    ["Storage:Local:RootPath"] = Path.Combine(
                        Path.GetTempPath(),
                        $"BankingBrainTestStorage_{Guid.NewGuid():N}"),
                    ["Connectors:FileSystem:AllowedRoots:0"] = ConnectorSourcesRoot
                });
        });
    }
}
