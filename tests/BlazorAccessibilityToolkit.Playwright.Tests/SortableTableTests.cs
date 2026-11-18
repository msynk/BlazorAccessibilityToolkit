using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents.RenderModes;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class SortableTableTests(BrowserFixture browser, HostFixture host)
{
    private static readonly PatternOptions Fast = new() { Timeout = TimeSpan.FromMilliseconds(600) };

    [Fact]
    public async Task QuickGrid_sortable_columns_follow_the_pattern()
    {
        await using var component = await host.Host.RenderAsync<OrdersGrid>(browser.Browser);

        var total = await AriaPatterns.VerifySortableTableAsync(component.Page.GetByRole(AriaRole.Table), column: 2);
        Assert.True(total.Passed, total.Describe());
        var customer = await AriaPatterns.VerifySortableTableAsync(component.Page.GetByRole(AriaRole.Table), column: 1);
        Assert.True(customer.Passed, customer.Describe());
    }

    [Fact]
    public async Task Table_that_sorts_without_aria_sort_fails()
    {
        const string html = """
            <main><h1>x</h1><table><thead><tr><th><button id="b">Name</button></th><th>Age</th></tr></thead>
            <tbody><tr><td>Cleo</td><td>3</td></tr><tr><td>Ana</td><td>5</td></tr><tr><td>Bo</td><td>4</td></tr></tbody></table></main>
            <script>
              document.getElementById('b').addEventListener('click', () => {
                const body = document.querySelector('tbody');
                Array.from(body.rows).sort((a, b) => a.cells[0].textContent.localeCompare(b.cells[0].textContent)).forEach(r => body.appendChild(r));
              });
            </script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifySortableTableAsync(session.Page.Locator("table"), options: Fast);

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Enter on the header button sets aria-sort", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement == "Sortable column headers contain a button");
    }

    /// <summary>
    /// QuickGrid's Paginator does not announce page changes, and its "next" button becomes disabled on the last page
    /// while it has focus, which drops focus to the document body. The verifier must keep reporting both.
    /// </summary>
    [Fact]
    public async Task QuickGrid_paginator_does_not_announce_pages_and_loses_focus_on_the_last_page()
    {
        await using var component = await host.Host.RenderAsync<OrdersGrid>(browser.Browser);

        var result = await AriaPatterns.VerifyPaginationAsync(
            component.Page.GetByRole(AriaRole.Button, new() { Name = "Go to next page" }),
            component.Page.GetByRole(AriaRole.Table),
            Fast);

        Assert.Contains(result.Checks, c => c.Passed && c.Requirement == "Enter shows the next page");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("The page change is conveyed", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "Focus is never lost to the document body");
    }

    [Fact]
    public async Task Pagination_with_a_status_and_aria_disabled_passes()
    {
        const string html = """
            <main><h1>x</h1>
              <ul id="list"><li>Item 1</li></ul>
              <button id="next">Next page</button>
              <p role="status" id="status"></p>
            </main>
            <script>
              let page = 1;
              document.getElementById('next').addEventListener('click', e => {
                if (page === 3) return;
                page++;
                document.getElementById('list').innerHTML = '<li>Item ' + page + '</li>';
                document.getElementById('status').textContent = 'Page ' + page + ' of 3';
                if (page === 3) e.target.setAttribute('aria-disabled', 'true');
              });
            </script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyPaginationAsync(session.Page.Locator("#next"), session.Page.Locator("#list"), Fast);
        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public async Task A_table_with_one_row_cannot_prove_its_order()
    {
        const string html = """
            <main><h1>x</h1><table><thead><tr><th aria-sort="none"><button id="b">Name</button></th></tr></thead>
            <tbody><tr><td>Ana</td></tr></tbody></table></main>
            <script>
              const th = document.querySelector('th');
              document.getElementById('b').addEventListener('click', () => th.setAttribute('aria-sort', th.getAttribute('aria-sort') === 'ascending' ? 'descending' : 'ascending'));
            </script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifySortableTableAsync(session.Page.Locator("table"), options: Fast);

        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Rows are in ascending order", StringComparison.Ordinal) && c.Detail == "fewer than two rows to compare");
    }
}
