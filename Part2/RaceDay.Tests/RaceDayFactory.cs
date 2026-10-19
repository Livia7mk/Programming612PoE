using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;

namespace RaceDay.Tests;

// Starts the real API for a test, but with a temporary in-memory database instead of SQL Server.
public class RaceDayFactory : WebApplicationFactory<Program>
{
    // A new name per factory, so every test gets its own empty database.
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the SQL Server registration of the DbContext...
            var toRemove = services
                .Where(d => d.ServiceType.FullName != null &&
                            d.ServiceType.FullName.Contains("RaceDayDbContext"))
                .ToList();

            foreach (var descriptor in toRemove)
                services.Remove(descriptor);

            // ...and use an in-memory database instead.
            services.AddDbContext<RaceDayDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}