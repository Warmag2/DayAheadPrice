using DayAheadPrice.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace DayAheadPrice.Database.Contexts;

/// <summary>
/// Database context for stored electricity prices.
/// </summary>
public class PriceDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PriceDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public PriceDbContext(DbContextOptions<PriceDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// The stored price slots.
    /// </summary>
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();
}
