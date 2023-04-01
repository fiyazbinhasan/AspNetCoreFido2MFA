using System.Text;
using AspNetCoreFido2MFA.Models;
using Fido2NetLib;
using Microsoft.EntityFrameworkCore;

namespace AspNetCoreFido2MFA.Data;

public class AppDbContext : DbContext
{
    public DbSet<Fido2User> Fido2Users => Set<Fido2User>();
    public DbSet<StoredCredential> StoredCredentials => Set<StoredCredential>();

    public AppDbContext(DbContextOptions<AppDbContext> optionsBuilder) : base(optionsBuilder) 
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fido2User>()
            .HasData(new List<Fido2User>()
            {
                new()
                {
                    Id = Encoding.UTF8.GetBytes("fiyazhasan@fido.local"), 
                    DisplayName = "fiyazhasan@fido.local",
                    Name = "fiyazhasan@fido.local"
                }
            });
    }
}