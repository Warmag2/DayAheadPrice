namespace DayAheadPrice.Options;

/// <summary>
/// Options controlling price history persistence.
/// </summary>
internal class PersistenceOptions
{
    /// <summary>
    /// Whether to store fetched prices in the database and enable the history / zoom feature.
    /// </summary>
    /// <remarks>
    /// When disabled the application behaves as it did before persistence existed: it fetches the current
    /// window from the API on demand and shows only the live 15-minute page, with no database access.
    /// The domain and currency stored alongside prices are taken from <see cref="EndpointOptions"/> and the
    /// parsed document, so adding more domains later needs no change here.
    /// </remarks>
    public bool Enabled { get; set; }
}
