namespace UT_Utah_ParceliD.Models;

/// <summary>
/// Input configuration loaded from JSON (local input.json or Apify input).
/// Controls the Utah parcel scrape: ParcelId to look up.
/// </summary>
public class InputConfig
{
    /// <summary>
    /// Parcel ID to scrape (e.g. 35:840:0124).
    /// </summary>
    public string ParcelId { get; set; } = "";
}
