# Blazor Dev Tools

> **Early scaffolding — not yet functional.** Component inspection, parameter viewing, and dependency-injection introspection are planned but not implemented yet.

**Blazor Dev Tools** brings a [React DevTools](https://react.dev/learn/react-developer-tools)-like experience to [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) applications. Inspect your component tree, view parameters and cascading values, and understand what the dependency injection container resolved — all from a dedicated panel inside Chrome DevTools.

The project has two main parts:

| Part | Location | Role |
|------|----------|------|
| Chrome Extension | `src/Extension` | DevTools panel, background relay, and content-script bridge |
| Blazor library | `src/BlazorDevTools.Client` | Hooks into the Blazor runtime and exposes inspection data via a JSON protocol |

Sample apps under `samples/` exercise both **WebAssembly** and **Server** hosting models. Projects multi-target **.NET 9** and **.NET 10**.

## Getting Started

### Prerequisites

- [Google Chrome](https://www.google.com/chrome/) (or a Chromium-based browser with DevTools extension support)
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or later (9.0 and 10.0 are supported)

### Build from source

```bash
# Restore and build the solution
dotnet build BlazorDevTools.sln

# Run the WebAssembly sample
dotnet run --project samples/BlazorDevTools.Sample.Wasm

# Or run the Server sample
dotnet run --project samples/BlazorDevTools.Sample.Server
```

### Load the Chrome extension

Build the TypeScript extension scripts first:

```bash
cd src/Extension
npm install
npm run build
```

Then load the extension in Chrome:

1. Open `chrome://extensions`
2. Enable **Developer mode**
3. Click **Load unpacked**
4. Select the `src/Extension` folder

Re-run `npm run build` (or `npm run watch`) after changing extension TypeScript sources.

Open Chrome DevTools on a running sample app. A **Blazor** panel will appear (scaffolding UI only for now).

### Verify the messaging bridge (manual smoke test)

After building, you can confirm the C# → panel pipe with the **Server** sample:

```bash
dotnet build samples/BlazorDevTools.Sample.Server/BlazorDevTools.Sample.Server.csproj -f net10.0
dotnet run --project samples/BlazorDevTools.Sample.Server -f net10.0
```

1. Load the unpacked extension from `src/Extension` (see above).
2. Open the sample app in Chrome, then open DevTools and select the **Blazor** panel (registers the panel port). If the app loaded before the panel was open, hard-refresh the tab; the background worker also buffers the last envelope per tab and flushes when the panel connects.
3. Logs from `panel.js` appear in the **panel page's own console**, not the inspected page console: right-click inside the Blazor DevTools panel → **Inspect** → **Console** (DevTools-on-DevTools).
4. Confirm a real `componentTreeUpdate` message is logged. On the **Server** sample, static-SSR layout components (`Routes`, `MainLayout`) are not in the interactive renderer — expect `DevToolsInitializer` on every page and `Counter` when navigated to `/counter`. Use **Home ↔ Counter** navigation to verify a second envelope arrives (hash/debounce skips identical trees).

> NuGet packaging is not available yet. Reference `src/BlazorDevTools.Client` as a project reference until the library is published.

## Security

Blazor Dev Tools exposes sensitive application internals to the browser when enabled. The in-page bridge dispatches protocol envelopes via same-origin `window.postMessage`. Any same-origin script — not just the Chrome extension — can observe component names, serialized parameter values, DI injection metadata, and DOM locators.

### Default behavior

Dev Tools is **disabled unless** `IHostEnvironment.IsDevelopment()` is `true`. When the host environment is unavailable, Dev Tools stays off. This is enforced at runtime via `IBlazorDevToolsService.IsEnabled` — you do not need the Chrome extension installed for the gate to apply; the Blazor app simply does not dispatch envelopes when disabled.

### Integration patterns

**Pattern A (recommended):** Register and render unconditionally; the runtime gate disables Dev Tools in Production.

```csharp
builder.Services.AddBlazorDevTools();
```

```razor
<DevToolsInitializer />
```

**Pattern B (optional optimization):** Skip registration outside Development **and** conditionally render `<DevToolsInitializer />`. Both steps are required — registration alone without the component does nothing; rendering alone without registration causes a DI failure.

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddBlazorDevTools();
}
```

### Explicit override

`AddBlazorDevTools(o => o.Enabled = true)` is available for local debugging scenarios. **Never use this in production.**

### Edge environments

- **Staging** is off by default (not Development).
- **Misconfigured production** with `ASPNETCORE_ENVIRONMENT=Development` will enable Dev Tools — ensure production deployments use the Production environment.
- **Blazor WebAssembly:** the client bundle ships whatever you register. The WASM host environment may differ from the server host environment; verify production-like behavior with `dotnet publish -c Release` and serve the publish output.

### Verifying the production gate

When testing that Production suppresses Dev Tools, use a page-level listener in the inspected tab's DevTools Console — an empty Blazor panel alone is not sufficient proof (the extension may buffer stale envelopes):

```javascript
window.addEventListener("message", (e) => {
  if (e.data?.protocol === "blazorDevTools") console.log("LEAK", e.data);
});
```

Expect zero `LEAK` logs after page load, navigation, and panel refresh. Use a fresh incognito window or clear extension session storage before negative tests.

## Architecture Overview

The Chrome extension knows nothing about Blazor internals. It consumes a standardized JSON messaging protocol. The Blazor library handles reflection, runtime inspection, and dependency-injection introspection.

```mermaid
flowchart LR
  devtoolsPanel["DevTools Panel"]
  backgroundSw["Background Service Worker"]
  contentScript["Content Script"]
  jsonProtocol["JSON Messaging Protocol"]
  blazorLib["BlazorDevTools.Client"]

  devtoolsPanel --> backgroundSw
  backgroundSw --> contentScript
  contentScript --> jsonProtocol
  jsonProtocol --> blazorLib
```

**Message flow (planned):**

1. **DevTools panel** (`panel.html` / `panel.js`) — UI for the component tree and property inspector
2. **Background service worker** (`background.js`) — relays messages between the panel and the inspected tab
3. **Content script** (`content.js`) — injected into the page; bridges to the in-page Blazor runtime
4. **Blazor library** (`BlazorDevTools.Client`) — inspects components and serializes state into the JSON protocol

Wasm vs. Server hosting is abstracted inside the library so both sample apps can be exercised with the same extension.

## Contributing

This project is in early development. Interfaces and the messaging protocol are not finalized.

- Open an issue to discuss larger changes before submitting a PR
- Keep pull requests focused and small
- Follow the conventions in `.cursor/rules/architecture.mdc`

Contributions are welcome!

## License

Licensed under the [GNU Lesser General Public License v3.0 (LGPL-3.0)](LICENSE). See [COPYING](COPYING) and [COPYING.LESSER](COPYING.LESSER) for the full license texts.
