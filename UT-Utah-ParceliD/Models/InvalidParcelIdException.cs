namespace UT_Utah_ParceliD.Models;

public class InvalidParcelIdException : Exception
{
    public string ParcelId { get; }

    public InvalidParcelIdException(string parcelId)
        : base($"Parcel ID '{parcelId}' was not found on the search results page.")
    {
        ParcelId = parcelId;
    }
}

