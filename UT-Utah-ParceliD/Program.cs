using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UT_Utah_ParceliD.Models;
using UT_Utah_ParceliD.Services;
using UT_Utah_ParceliD.Utils;

var isApify = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APIFY_CONTAINER_PORT"));

Microsoft.Playwright.Program.Main(["install", "chromium"]);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<UtUtahScraperService>();
    })
    .Build();

var config = await ApifyHelper.GetInputFromApifyAsync<InputConfig>();
if (config == null)
    config = await ApifyHelper.LoadLocalInputAsync<InputConfig>();
config ??= new InputConfig();

var parcelIdsRaw = !string.IsNullOrWhiteSpace(config.ParcelId) ? config.ParcelId : "35:840:0124";
var parcelIds = parcelIdsRaw
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .ToList();

if (parcelIds.Count == 0)
{
    await ApifyHelper.SetStatusMessageAsync("No parcel IDs provided.", isTerminal: true);
    return;
}

var service = host.Services.GetRequiredService<UtUtahScraperService>();
var succeeded = 0;
var failed = 0;
var invalidParcelIds = new List<string>();

try
{
    await ApifyHelper.SetStatusMessageAsync("Starting UT-Utah-ParceliD scraper...");
    await service.InitAsync();

    for (var i = 0; i < parcelIds.Count; i++)
    {
        var parcelId = parcelIds[i];
        await ApifyHelper.SetStatusMessageAsync($"Processing parcel {i + 1}/{parcelIds.Count}: {parcelId}");

        try
        {
            var record = await service.ScrapeParcelAsync(parcelId);
            await ApifyHelper.SetStatusMessageAsync($"Pushing to Dataset (parcel {parcelId})...");
            await ApifyHelper.PushSingleDataAsync(record);
            Console.WriteLine($"Data pushed to Dataset successfully for parcel {parcelId}.");
            succeeded++;
        }
        catch (InvalidParcelIdException)
        {
            Console.WriteLine($"Parcel {parcelId} is invalid or not found on search results page. Skipping.");
            invalidParcelIds.Add(parcelId);
            failed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parcel {parcelId}: {ex.Message}");
            failed++;
        }
    }

    var detail = invalidParcelIds.Count > 0
        ? $" Invalid parcel IDs: {string.Join(", ", invalidParcelIds)}"
        : string.Empty;

    await ApifyHelper.SetStatusMessageAsync(
        $"Finished. total={parcelIds.Count}, succeeded={succeeded}, failed={failed}.{detail}",
        isTerminal: true);
    Console.WriteLine($"Scrape completed. total={parcelIds.Count}, succeeded={succeeded}, failed={failed}.{detail}");
}
catch (Exception ex)
{
    await ApifyHelper.SetStatusMessageAsync($"Error: {ex.Message}", isTerminal: true);
    if (!isApify)
    {
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
    throw;
}
finally
{
    await service.StopAsync();
}

if (!isApify)
{
    Console.WriteLine("Press Enter to exit...");
    Console.ReadLine();
}
