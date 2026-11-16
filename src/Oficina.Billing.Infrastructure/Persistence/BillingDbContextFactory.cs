using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Oficina.Billing.Infrastructure.Persistence;

/// <summary>Só para o <c>dotnet ef migrations</c>: não conecta no banco.</summary>
internal sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BillingDbContext>()
        .UseNpgsql("Host=localhost;Database=billing;Username=billing")
        .Options);
}
