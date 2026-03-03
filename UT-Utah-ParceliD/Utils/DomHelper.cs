using System.Collections.Generic;
using System.Linq;
using Microsoft.Playwright;

namespace UT_Utah_ParceliD.Utils;

/// <summary>DOM manipulation and extraction utilities for Utah parcel scraper.</summary>
public static class DomHelper
{
    /// <summary>Wait for loading backdrop to become hidden.</summary>
    public static async Task WaitForLoadingBackdropHiddenAsync(IPage page, int timeoutMs = 15_000)
    {
        var backdrop = page.Locator("#loadingBackDrop");
        if (await backdrop.CountAsync() == 0) return;
        await backdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
    }

    /// <summary>Click via Playwright or fallback to JS evaluate. Handles overlays/scroll.</summary>
    public static async Task DomClickAsync(ILocator locator)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        try
        {
            await locator.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
        }
        catch
        {
            await locator.EvaluateAsync("el => el.click()");
        }
    }
}
