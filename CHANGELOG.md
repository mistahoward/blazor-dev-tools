# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-06-17

### Fixed

- **Static web asset 404 after package rename**: pinned `StaticWebAssetBasePath` to `_content/BlazorDevTools.Client` so the runtime JS interop (`blazorDevToolsInterop.js`) is served at a stable path independent of `PackageId`. Without this, the renamed NuGet package served the interop under `_content/Blazor.Browser.DevTools/`, causing a 404 and an empty DevTools panel.
- **Docs**: corrected the production-gate verification snippet in the README to use the actual protocol identifier `blazor-devtools` (was `blazorDevTools`).

## [1.0.0] - 2026-06-17

Initial production release of the **Blazor.Browser.DevTools** NuGet library and the companion **Blazor Dev Tools** Chrome extension.

### Added

- **Blazor.Browser.DevTools NuGet library** targeting .NET 9 and .NET 10.
- **Component tree inspection**: live, nested view of the rendered Blazor component hierarchy.
- **Parameter & cascading value viewing**: parameter names, declared types, and serialized values per component.
- **Dependency injection introspection**: injected services with declared service type and resolved implementation type.
- **Element picker & highlight overlay**: pick a page element to locate its component and highlight components using best-effort CSS locators.
- **Development-only runtime gate**: Dev Tools is disabled unless `IHostEnvironment.IsDevelopment()` is `true`, with an explicit override (`Enabled`) for local debugging.
- **Clean registration**: `builder.Services.AddBlazorDevTools()` plus a single `<DevToolsInitializer />` component.
- **Chrome extension (Manifest V3)**: DevTools "Blazor" panel, background relay service worker, and content-script bridge consuming the JSON messaging protocol.
- **Official Blazor branding icons** for the Chrome extension and NuGet package.
- **Automated store-ready zip packaging** via `npm run package` in `src/Extension`.

[1.0.0]: https://github.com/mistahoward/blazor-dev-tools/releases/tag/v1.0.0
