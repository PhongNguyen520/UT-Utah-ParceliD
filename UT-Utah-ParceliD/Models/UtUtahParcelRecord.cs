namespace UT_Utah_ParceliD.Models;

/// <summary>
/// Record for Utah parcel data extracted from property search.
/// </summary>
public class UtUtahParcelRecord
{
    // String (single value)
    public string OwnerName { get; set; } = "";
    public string PropertyAddress { get; set; } = "";
    public string MailingAddress { get; set; } = "";
    public string TaxingDescription { get; set; } = "";
    public string Acreage { get; set; } = "";
    public List<string> Years { get; set; } = new();
    public List<string> OwnerNames { get; set; } = new();
    public List<string> EntryNumbers { get; set; } = new();
    public List<string> Addresses { get; set; } = new();
    public List<string> DocumentNumbers { get; set; } = new();
    public List<string> ParentParcelIDs { get; set; } = new();
}
