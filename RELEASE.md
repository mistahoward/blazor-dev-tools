# Blazor Dev Tools 1.0.0 Release

Step-by-step guide to publish the Chrome extension and NuGet package.

## Prerequisites

Run these from the repo root before publishing:

```bash
# Extension store zip
cd src/Extension
npm install
npm run package
# -> src/Extension/web-ext-artifacts/blazor-dev-tools-1.0.0.zip

# NuGet package
cd ../..
dotnet pack src/BlazorDevTools.Client/BlazorDevTools.Client.csproj -c Release -o artifacts
# -> artifacts/Blazor.DevTools.1.0.0.nupkg + .snupkg
```

## Chrome Web Store

### 1. Upload

1. Open the [Chrome Web Store Developer Dashboard](https://chrome.google.com/webstore/devconsole).
2. Select your **Blazor Dev Tools** item.
3. Upload `src/Extension/web-ext-artifacts/blazor-dev-tools-1.0.0.zip`.

### 2. Store listing

**Short description** (132 chars max):

> Inspect Blazor component trees, parameters, cascading values, and dependency injection from a dedicated Chrome DevTools panel.

**Detailed description** (suggested):

> Blazor Dev Tools brings a React DevTools-like experience to Blazor applications (Server and WebAssembly). Inspect your component tree, view parameters and cascading values, and understand what the dependency injection container resolved — all from a dedicated **Blazor** panel inside Chrome DevTools.
>
> Requires the companion **Blazor.DevTools** NuGet package in your Blazor app (`dotnet add package Blazor.DevTools`). Dev Tools is disabled by default outside the Development environment.

- **Category:** Developer Tools
- **Language:** English
- **Icon:** already embedded in the zip (`icons/icon128.png`)
- **Screenshots:** at least one 1280×800 or 640×400 screenshot of the Blazor panel with a sample app running

### 3. Privacy

**Single purpose:** Developer tooling for inspecting Blazor component trees during local development.

**Permissions justification:**

| Permission | Justification |
|------------|---------------|
| `storage` | Buffers the last component-tree snapshot per tab in `chrome.storage.session` so the DevTools panel can display data when it connects after the page loads. |
| Content script on `http://*/*` and `https://*/*` | Bridges messages between the in-page Blazor runtime and the DevTools panel. Developers run Blazor on many local and remote origins during development; host permissions cannot be narrowed without breaking the tool. |

**Data handling:** The extension does not collect, transmit, or sell user data. All inspection data stays local between the inspected tab, the extension, and the DevTools panel.

### 4. Submit

Submit for review. First upload is version **1.0.0** (no version bump needed).

## NuGet

### Push

```bash
dotnet nuget push artifacts/Blazor.DevTools.1.0.0.nupkg \
  --api-key <YOUR_NUGET_API_KEY> \
  --source https://api.nuget.org/v3/index.json
```

The matching `.snupkg` is pushed automatically when present alongside the `.nupkg`.

### Verify

After indexing (usually a few minutes), confirm on [nuget.org/packages/Blazor.DevTools](https://www.nuget.org/packages/Blazor.DevTools):

- README renders correctly
- Icon displays
- Version is `1.0.0`

## GitHub Release

After both artifacts are live, create the `v1.0.0` release (or update its notes) with links:

- NuGet: https://www.nuget.org/packages/Blazor.DevTools
- Chrome Web Store: _(add your listing URL once approved)_

Release notes should mirror [CHANGELOG.md](CHANGELOG.md).
