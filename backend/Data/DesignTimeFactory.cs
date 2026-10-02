using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Atlas.Api.Data;
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var basePath = File.Exists(Path.Combine(current, "backend.csproj")) ? current : Path.Combine(current, "backend");
        var config = new ConfigurationBuilder().SetBasePath(basePath).AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables().Build();
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(config.GetConnectionString("Database") ?? "Host=localhost;Database=atlas_demo;Username=atlas;Password=unused-design-time").Options);
    }
}
