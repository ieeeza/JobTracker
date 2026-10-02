using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Microsoft.Playwright;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Scraping;

public class PlaywrightJobPageReader : IJobPageReader
{
    public async Task<RenderedPage> ReadAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true
        });

        var page = await browser.NewPageAsync();

        await page.GotoAsync(url, new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });

        var title = await page.TitleAsync();
        var content = await page.Locator("body").InnerTextAsync();

        if (title.Contains("Blocked", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("You have been blocked", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Request Blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new JobPageBlockedException(url);
        }

        return new RenderedPage(
            page.Url,
            title,
            content
        );
    }
}