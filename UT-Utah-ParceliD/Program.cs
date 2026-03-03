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

var service = host.Services.GetRequiredService<UtUtahScraperService>();

var parcelId = !string.IsNullOrWhiteSpace(config.ParcelId) ? config.ParcelId : "35:840:0124";

try
{
    var record = await service.ScrapeParcelAsync(parcelId);
    await ApifyHelper.PushSingleDataAsync(record);
    if (!isApify)
    {
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (!isApify)
    {
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
    throw;
}
