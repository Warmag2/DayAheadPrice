using System.ComponentModel.DataAnnotations;
using Faemiyah.Repository.Entities;
using Microsoft.EntityFrameworkCore;

namespace Faemiyah.DayAheadPrice.Database.Entities;

/// <summary>
/// A single stored electricity price slot.
/// </summary>
/// <remarks>
/// The slot spans <see cref="TemporalEntityBase{TKey}.DateBegin"/> (inclusive) to
/// <see cref="TemporalEntityBase{TKey}.DateEnd"/> (exclusive), both UTC. Only 15-minute slots are stored, and history
/// is fetched back to the 15-minute market time unit switch and no further.
/// </remarks>
[Index(nameof(Domain), nameof(DateBegin), IsUnique = true)]
public class PricePoint : TemporalEntityBase<long>
{
    /// <summary>
    /// The raw price for the slot, in the source unit (c/kWh), before margin and VAT, or <b>null</b> when the source
    /// has no price for this past slot. An unavailable slot is stored so it counts as covered and is never requested
    /// again; delete it to have it fetched once more.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// The bidding zone / domain the price is for (for example <c>10YFI-1--------U</c>).
    /// </summary>
    [Required]
    [MaxLength(16)]
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The ISO currency the price is expressed in.
    /// </summary>
    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = string.Empty;
}
