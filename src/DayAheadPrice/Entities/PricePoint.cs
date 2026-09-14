using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RepositoryPrototype.Entities;

namespace DayAheadPrice.Entities;

/// <summary>
/// A single stored electricity price slot.
/// </summary>
/// <remarks>
/// The slot spans <see cref="TemporalEntityBase{TKey}.DateBegin"/> (inclusive) to
/// <see cref="TemporalEntityBase{TKey}.DateEnd"/> (exclusive), both UTC. The slot length is whatever
/// resolution the source published, so the table can hold 15-minute, hourly or any other slots side by side.
/// </remarks>
[Index(nameof(Domain), nameof(DateBegin), IsUnique = true)]
public class PricePoint : TemporalEntityBase<long>
{
    /// <summary>
    /// The raw price for the slot, in the source unit (c/kWh), before margin and VAT.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// The bidding zone / domain the price is for (for example <c>10YFI-1--------U</c>).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The ISO currency the price is expressed in.
    /// </summary>
    [Required]
    [MaxLength(8)]
    public string Currency { get; set; } = "EUR";
}
