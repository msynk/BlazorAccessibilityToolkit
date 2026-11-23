# MCP server for coding agents

`blazor-a11y mcp` runs a [Model Context Protocol](https://modelcontextprotocol.io) server, so that coding agents such as GitHub Copilot in VS Code or Visual Studio, Claude Code and Cursor can use the toolkit while they edit a Blazor app. The agent checks the markup it writes, applies the safe fixes, reads the findings of your accessibility tests and looks up rules. It gets the toolkit's results without having to read the build output or the report files.

The server is part of the `BlazorAccessibilityToolkit.Cli` tool and runs locally over standard input and output. It does not connect to the network.

## Set up

Install the tool in the repository with a tool manifest, so that everybody gets the same version:

```bash
dotnet new tool-manifest   # once per repository
dotnet tool install BlazorAccessibilityToolkit.Cli --prerelease
```

Then add the server to your agent's configuration. Run the commands from the repository root, which is the folder the server may use (see [Which files it can use](#which-files-it-can-use)).

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "blazor-a11y": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["tool", "run", "blazor-a11y", "mcp"]
    }
  }
}
```

**Visual Studio** reads the same format from `.mcp.json` in the solution folder. Use `"servers"` as above.

**Claude Code:**

```bash
claude mcp add --scope project blazor-a11y -- dotnet tool run blazor-a11y mcp
```

**Other clients** start the command `dotnet tool run blazor-a11y mcp` with the repository root as the working directory.

Without a tool manifest, a client can run the package directly with `dnx` from the .NET 10 SDK: the command is `dnx`, with the arguments `BlazorAccessibilityToolkit.Cli@<version>`, `--yes` and `mcp`. The NuGet.org page of the package also lists the server, with this configuration ready to copy.

## Tools

| Tool | Changes files | What it does |
| --- | --- | --- |
| `check_razor` | no | Runs the [Razor analyzer](analyzers.md) on `.razor` and `.cshtml` files or folders, or on a markup snippet before the agent writes it. Returns each finding with its line and column, the message, and whether a fix is automatic, a choice, or manual. It follows `blazor-a11y-ignore` comments and the analyzer's `.editorconfig` settings: files it is turned off for, component mapping, and rule severities (`dotnet_diagnostic.BA11Y007.severity`). Project settings such as `<NoWarn>` or `TreatWarningsAsErrors` are not read, so the build can report a different severity. |
| `fix_razor` | yes | Applies the analyzer's safe fixes, as [`blazor-a11y fix`](analyzers.md#fixes) does. It changes only findings that have exactly one safe edit and lists those that need a choice, such as `current-password` or `new-password`. Files keep their encoding and byte order mark. It has a `dryRun` option. |
| `get_results` | no | Reads the `*.a11y.json` results your tests wrote. By default it reads `a11y-results` or `$BLAZOR_A11Y_RESULTS_DIR`. Returns the summary and whether the [CI gate](ci-integration.md) would pass with its default policy. It also returns the findings, with the element, the component that rendered it, the WCAG criteria and how to fix it. It can filter by rule, status or audit, compare with a baseline to mark new findings, and page through long lists. |
| `explain_rule` | no | Explains an analyzer rule (`BA11Y007`), a toolkit rule (`blazor-duplicate-id`) or an axe-core rule (`color-contrast`). Given a WCAG success criterion (`1.3.1`), it lists the rules that check part of it. |
| `list_rules` | no | Lists the analyzer rules and the toolkit's rules, with their WCAG criteria and whether they have an automatic fix. |

The results are JSON. Each tool also declares an output schema, which clients can use to check them.

A typical session: the agent edits a page and calls `check_razor` on it. It runs `fix_razor` for the safe fixes and writes a real label where one is missing. After `dotnet test`, it calls `get_results` and fixes what the rendered page still fails. The server's instructions describe this order to the agent. They also tell the agent not to silence findings with ignore comments unless they are false positives.

## Which files it can use

The server reads and changes files only inside its root folders. By default that is the working directory it was started in. Name the folders with `--root`, which you can repeat. Relative paths from the agent are resolved against the first root:

```bash
blazor-a11y mcp --root . --root ../SharedComponents
```

A path outside the roots is refused, whether it is absolute, climbs out with `..`, or passes through a symbolic link or junction that points outside. When the tools search a folder, they do not enter links at all. A confused agent, or one misled by text in a page, cannot read or change other files through the server.

`fix_razor` is marked as changing files, so clients ask before they run it, unless you allow it. Its edits are small and stay in your working tree for review. Commit or stash your work before you let an agent run it on a whole folder.

## Security notes

- Results contain HTML and text from your app's pages. A page that shows user content could contain text written to mislead an agent. The server shortens element HTML and tells the agent to treat it as data, but review what the agent does with the findings, as with any tool output.
- The server needs no secrets and makes no network requests. `explain_rule` links to Deque's rule pages; it does not fetch them.

## Limitations

- Automated checks find only part of the accessibility problems. When the agent has nothing left to fix, that does not mean the app conforms to WCAG; see [Manual review](manual-review.md).
- `check_razor` judges only what the markup states, like the analyzer. Values computed at run time and content from child components need the [component](component-testing.md) and [browser](interactive-testing.md) tests, whose findings `get_results` reads.
- The server does not run your tests or start the app. The agent runs `dotnet test` itself and then reads the results.
- axe-core rule details come from your results. For a rule that no result mentions, `explain_rule` returns only the link to Deque's page.
