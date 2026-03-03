using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UT_Utah_ParceliD.Models;
using UT_Utah_ParceliD.Utils;

namespace UT_Utah_ParceliD.Services;

/// <summary>
/// Scraper service for Utah parcel lookup. Navigates Abstract, Property Info, Serial History and extracts all record fields.
/// </summary>
public class UtUtahScraperService
{
    const string StartUrl = "https://www.utahcounty.gov/LandRecords/Index.asp";
    const int MaxRetries = 3;

    IPlaywright? _playwright;
    IBrowser? _browser;
    IBrowserContext? _context;
    IPage? _page;

    /// <summary>Initializes browser and context once. Must be called before ScrapeParcelAsync.</summary>
    public async Task InitAsync()
    {
        if (_context != null) return;
        await InitBrowserAsync();
    }

    /// <summary>Scrapes parcel data for the given parcel ID. Requires InitAsync to have been called. Opens a new tab, scrapes, then closes the tab.</summary>
    public async Task<UtUtahParcelRecord> ScrapeParcelAsync(string parcelId)
    {
        if (string.IsNullOrWhiteSpace(parcelId))
            throw new ArgumentException("Parcel ID is required.", nameof(parcelId));
        if (_context == null)
            throw new InvalidOperationException("InitAsync must be called before ScrapeParcelAsync.");

        var page = await _context.NewPageAsync();
        page.SetDefaultTimeout(30_000);

        try
        {
            _page = page;
            await ExecuteWithRetryAsync("Searching parcel", async () =>
            {
                await page.GotoAsync(StartUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                var serialSearchLink = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Serial Number Search" }).First;
                await serialSearchLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await serialSearchLink.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                var serialInput = page.Locator("#av_serial");
                await serialInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await serialInput.FillAsync(parcelId);

                var searchBtn = page.Locator("input[name='Submit'][type='submit'][value='Search']");
                await searchBtn.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(1500);
            });

            await ApifyHelper.SetStatusMessageAsync("Loading Abstract page...");

            await ExecuteWithRetryAsync("Selecting Abstract", async () =>
            {
                var cell = page.GetByRole(AriaRole.Cell, new PageGetByRoleOptions { Name = parcelId, Exact = true });
                if (await cell.CountAsync() == 0)
                    throw new InvalidParcelIdException(parcelId);

                var jumpMenu = cell.Locator("select#jumpMenu");
                await jumpMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await Task.WhenAll(
                    page.WaitForURLAsync("**/Abstract.asp*", new PageWaitForURLOptions { Timeout = 15_000 }),
                    jumpMenu.SelectOptionAsync(new SelectOptionValue { Label = "Abstract" })
                );
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(1000);
            });

            var record = new UtUtahParcelRecord { ParcelId = parcelId };
            await ExtractAbstractFieldsAsync(record);
            await ApifyHelper.SetStatusMessageAsync("Loading Property Info page...");

            await ExecuteWithRetryAsync("Navigating to Property Info", async () =>
            {
                var abstractJumpMenu = page.Locator("select#jumpMenu").First;
                await abstractJumpMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                await Task.WhenAll(
                    page.WaitForURLAsync("**/Property.asp*", new PageWaitForURLOptions { Timeout = 15_000, WaitUntil = WaitUntilState.DOMContentLoaded }),
                    abstractJumpMenu.SelectOptionAsync(new SelectOptionValue { Label = "Property Info" })
                );
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(1000);
            });

            await ApifyHelper.SetStatusMessageAsync("Extracting Property Info...");
            await ExtractAcreageAsync(record);
            await ExtractOwnerNamesTabAsync(record);
            await ExtractDocumentsTabAsync(record);
            await ExtractAddrsTabAsync(record);

            await ApifyHelper.SetStatusMessageAsync("Loading Serial History...");
            await ExtractSerialHistoryAsync(record);

            await ApifyHelper.SetStatusMessageAsync($"Scrape completed successfully for parcel {parcelId}.");
            Console.WriteLine($"Scrape completed successfully for parcel {parcelId}.");
            return record;
        }
        catch (Exception ex)
        {
            await ApifyHelper.SetStatusMessageAsync($"Error: {ex.Message}", isTerminal: true);
            throw;
        }
        finally
        {
            _page = null;
            try { await page.CloseAsync(); } catch { }
        }
    }

    static async Task ExecuteWithRetryAsync(string statusPrefix, Func<Task> action)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 1)
                    await ApifyHelper.SetStatusMessageAsync($"{statusPrefix} (retry {attempt}/{MaxRetries})");
                await action();
                return;
            }
            catch (InvalidParcelIdException)
            {
                throw;
            }
            catch (Exception)
            {
                if (attempt == MaxRetries) throw;
                await Task.Delay(2000 * attempt);
            }
        }
    }

    async Task ExtractAbstractFieldsAsync(UtUtahParcelRecord record)
    {
        try
        {
            record.OwnerName = (await _page!.Locator("xpath=//td[contains(.,'Owner Name:')]/following-sibling::td[1]").First.TextContentAsync())?.Trim() ?? "";
            record.PropertyAddress = (await _page.Locator("xpath=//td[contains(.,'Property Address:')]/following-sibling::td[1]").First.TextContentAsync())?.Trim() ?? "";
            record.MailingAddress = (await _page.Locator("xpath=//td[contains(.,'Mailing Address:')]/following-sibling::td[1]").First.TextContentAsync())?.Trim() ?? "";
            record.TaxingDescription = (await _page.Locator("xpath=//td[contains(.,'Taxing Description')]/following-sibling::td[1]").First.TextContentAsync())?.Trim() ?? "";
        }
        catch { }
    }

    async Task ExtractAcreageAsync(UtUtahParcelRecord record)
    {
        try
        {
            var acreageTd = _page!.Locator("xpath=//td[@colspan='3'][.//strong[contains(.,'Acreage')]]").First;
            var acreageText = (await acreageTd.TextContentAsync()) ?? "";
            var m = Regex.Match(acreageText, @"[\d.]+");
            record.Acreage = m.Success ? m.Value : acreageText.Replace("Acreage:", "").Replace("\u00A0", " ").Trim();
        }
        catch { }
    }

    async Task ExtractOwnerNamesTabAsync(UtUtahParcelRecord record)
    {
        try
        {
            var ownerNamesTab = _page!.Locator("li.TabbedPanelsTab").Filter(new LocatorFilterOptions { HasText = "Owner Names" }).First;
            await ownerNamesTab.ClickAsync();
            await Task.Delay(500);
            var ownerPanel = _page.Locator("div.TabbedPanelsContentVisible").First;
            var ownerRows = ownerPanel.Locator("tr:has(td a[href*='namesearch'])");
            var rowCount = await ownerRows.CountAsync();
            for (var r = 0; r < rowCount; r++)
            {
                var row = ownerRows.Nth(r);
                var tds = row.Locator("td");
                var yearText = (await tds.Nth(0).TextContentAsync())?.Trim() ?? "";
                var ownerText = (await tds.Nth(2).Locator("a").First.TextContentAsync())?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(yearText)) record.Years.Add(yearText);
                if (!string.IsNullOrWhiteSpace(ownerText)) record.OwnerNames.Add(ownerText);
            }
        }
        catch { }
    }

    async Task ExtractDocumentsTabAsync(UtUtahParcelRecord record)
    {
        try
        {
            var documentsTab = _page!.Locator("li.TabbedPanelsTab").Filter(new LocatorFilterOptions { HasText = "Documents" }).First;
            await documentsTab.ClickAsync();
            await Task.Delay(500);
            var docPanel = _page.Locator("div.TabbedPanelsContentVisible").First;
            var docRows = docPanel.Locator("tr:has(td em a[href*='Document.asp'])");
            var docRowCount = await docRows.CountAsync();
            for (var r = 0; r < docRowCount; r++)
            {
                var row = docRows.Nth(r);
                var entryLink = row.Locator("td").First.Locator("em a").First;
                var entryText = (await entryLink.TextContentAsync())?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(entryText)) record.EntryNumbers.Add(entryText);
            }
        }
        catch { }
    }

    async Task ExtractAddrsTabAsync(UtUtahParcelRecord record)
    {
        try
        {
            var addrsTab = _page!.Locator("li.TabbedPanelsTab").Filter(new LocatorFilterOptions { HasText = "Addrs" }).First;
            await addrsTab.ClickAsync();
            await Task.Delay(500);
            var addrsPanel = _page.Locator("div.TabbedPanelsContentVisible").First;
            var emTags = addrsPanel.Locator("em");
            var emCount = await emTags.CountAsync();
            for (var i = 0; i < emCount; i++)
            {
                var text = (await emTags.Nth(i).TextContentAsync())?.Replace("\r", "").Replace("\n", " ").Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(text)) record.Addresses.Add(text);
            }
        }
        catch { }
    }

    async Task ExtractSerialHistoryAsync(UtUtahParcelRecord record)
    {
        try
        {
            var navSelect = _page!.Locator("form#page-changer select[name='nav']");
            await navSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            var optValue = await navSelect.Locator("option:has-text('Serial History')").First.GetAttributeAsync("value");
            if (string.IsNullOrWhiteSpace(optValue)) return;

            await ExecuteWithRetryAsync("Navigating to Serial History", async () =>
            {
                var baseUri = new Uri(_page.Url);
                var targetUrl = optValue.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? optValue
                    : new Uri(baseUri, optValue).ToString();
                await _page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await Task.Delay(1000);
            });

            var historyRows = _page.Locator("xpath=//h1[contains(.,'Serial History')]/following-sibling::table//td[@valign='top']/table/tbody/tr[.//a[contains(@href,'Document')]]");
            var rowCount = await historyRows.CountAsync();
            for (var r = 0; r < rowCount; r++)
            {
                var row = historyRows.Nth(r);
                var docLink = row.Locator("td").First.Locator("a[href*='Document']");
                var parentLink = row.Locator("td").Nth(2).Locator("a[href*='SerialHistory']");
                if (await docLink.CountAsync() > 0)
                {
                    var docNum = (await docLink.First.TextContentAsync())?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(docNum)) record.DocumentNumbers.Add(docNum);
                }
                if (await parentLink.CountAsync() > 0)
                {
                    var parentId = (await parentLink.First.TextContentAsync())?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(parentId)) record.ParentParcelIDs.Add(parentId);
                }
            }
        }
        catch { }
    }

    /// <summary>Initializes browser and context for Apify (headless) or local run.</summary>
    async Task InitBrowserAsync()
    {
        _playwright = await Playwright.CreateAsync();
        var isApify = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APIFY_CONTAINER_PORT"));
        var browserArgs = new[]
        {
            "--no-default-browser-check",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--no-sandbox",
            "--disable-software-rasterizer",
            "--disable-extensions",
            "--disable-background-networking",
            "--disable-default-apps",
            "--disable-sync",
            "--disable-translate",
            "--mute-audio",
            "--no-first-run",
            "--disable-renderer-backgrounding"
        };
        try
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "chrome",
                Headless = isApify,
                Timeout = 60_000,
                Args = browserArgs
            });
        }
        catch
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = isApify,
                Timeout = 60_000,
                Args = browserArgs
            });
        }
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
    }

    /// <summary>Stops browser and disposes Playwright resources.</summary>
    public async Task StopAsync()
    {
        if (_page != null)
        {
            try { await _page.CloseAsync(); } catch { }
            _page = null;
        }
        if (_context != null)
        {
            try { await _context.CloseAsync(); } catch { }
            _context = null;
        }
        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { }
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }
}
