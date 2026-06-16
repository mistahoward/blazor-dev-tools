# Contributing to Blazor Dev Tools

Thanks for your interest in contributing! This project has two cooperating parts: a Blazor NuGet library (`src/BlazorDevTools.Client`) and a Chrome extension (`src/Extension`). Sample apps under `samples/` exercise both the WebAssembly and Server hosting models.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or later (both 9.0 and 10.0 are targeted)
- [Node.js](https://nodejs.org/) (LTS) and npm for the Chrome extension
- [Google Chrome](https://www.google.com/chrome/) (or a Chromium-based browser with DevTools extension support)

## Building

### Blazor library and samples

```bash
# Restore and build the whole solution
dotnet build BlazorDevTools.sln

# Run the WebAssembly sample
dotnet run --project samples/BlazorDevTools.Sample.Wasm

# Or run the Server sample
dotnet run --project samples/BlazorDevTools.Sample.Server
```

### Chrome extension

```bash
cd src/Extension
npm install
npm run build      # tsc + esbuild bundle the content script
npm run watch      # rebuild on change during development
npm run typecheck  # type-check without emitting
```

Load the unpacked extension:

1. Open `chrome://extensions`
2. Enable **Developer mode**
3. Click **Load unpacked**
4. Select the `src/Extension` folder

Open Chrome DevTools on a running sample app and select the **Blazor** panel.

## Conventions

This repository follows the architecture and code-generation guidelines in
[`.cursor/rules/architecture.mdc`](.cursor/rules/architecture.mdc). In short:

- **Separation of concerns**: the Chrome extension knows nothing about Blazor internals and only consumes the JSON messaging protocol. The Blazor library handles all reflection and runtime inspection.
- **SOLID first**: single-responsibility C# services and TypeScript modules; define interfaces for inter-process boundaries.
- **Modern C#**: file-scoped namespaces, global usings, and `record` types for immutable messaging payloads. Include XML documentation for all public C# APIs.
- **Defensive reflection**: the component tree can mutate rapidly; guard reflection-based inspection accordingly.
- **Manifest V3** standards for the extension, with a clear message-passing architecture between the DevTools page, background service worker, and content script. Use JSDoc for complex functions.

## Pull Requests

- Open an issue to discuss larger changes before submitting a PR.
- Keep pull requests focused and small.
- Ensure `dotnet build BlazorDevTools.sln` and `npm run build` (in `src/Extension`) succeed before submitting.
- Update [CHANGELOG.md](CHANGELOG.md) under an `Unreleased` section when your change is user-facing.

## License

By contributing, you agree that your contributions will be licensed under the
[GNU Lesser General Public License v3.0 (LGPL-3.0)](LICENSE).
