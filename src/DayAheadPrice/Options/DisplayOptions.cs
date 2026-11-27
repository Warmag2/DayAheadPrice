using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Faemiyah.DayAheadPrice.Options;

/// <summary>
/// Options controlling how prices are laid out on the calendar and shown.
/// </summary>
internal class DisplayOptions : IValidatableObject
{
    /// <summary>
    /// The time zone (IANA or Windows id) whose calendar is used for bar alignment, day summaries and displayed times.
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Helsinki";

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
        {
            yield return new ValidationResult($"DisplayOptions:TimeZone '{TimeZone}' is not a known time zone.", [nameof(TimeZone)]);
        }
    }
}
