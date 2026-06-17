/**
 * Produces a Chrome Web Store-ready zip of the extension runtime files.
 * Excludes TypeScript sources, dev tooling, and the high-res icon master.
 */
import archiver from "archiver";
import {
  createReadStream,
  createWriteStream,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
} from "node:fs";
import { join, relative } from "node:path";

interface ExtensionManifest {
  version: string;
}

const extensionRoot = join(__dirname, "..");

const manifest = JSON.parse(
  readFileSync(join(extensionRoot, "manifest.json"), "utf8"),
) as ExtensionManifest;

const version = manifest.version;
const outputDir = join(extensionRoot, "web-ext-artifacts");
const outputPath = join(outputDir, `blazor-dev-tools-${version}.zip`);

const rootFiles: readonly string[] = [
  "manifest.json",
  "devtools.html",
  "panel.html",
  "panel.css",
  "icons/icon16.png",
  "icons/icon48.png",
  "icons/icon128.png",
];

/**
 * Recursively collects `.js` files under a directory.
 *
 * @param dir - Directory to scan.
 * @returns Relative paths from the extension root.
 */
function collectJsFiles(dir: string): string[] {
  const results: string[] = [];

  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const fullPath = join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...collectJsFiles(fullPath));
      continue;
    }

    if (entry.isFile() && entry.name.endsWith(".js")) {
      results.push(relative(extensionRoot, fullPath).replaceAll("\\", "/"));
    }
  }

  return results;
}

const distDir = join(extensionRoot, "dist");
if (!existsSync(distDir)) {
  console.error("dist/ not found. Run `npm run build` first.");
  process.exit(1);
}

const distJsFiles = collectJsFiles(distDir);
const filesToPack = [...rootFiles, ...distJsFiles];

for (const file of rootFiles) {
  if (!existsSync(join(extensionRoot, file))) {
    console.error(`Missing required file: ${file}`);
    process.exit(1);
  }
}

if (distJsFiles.length === 0) {
  console.error("No JavaScript files found under dist/. Run `npm run build` first.");
  process.exit(1);
}

mkdirSync(outputDir, { recursive: true });

const output = createWriteStream(outputPath);
const archive = archiver("zip", { zlib: { level: 9 } });

output.on("close", () => {
  console.log(`Created ${outputPath} (${archive.pointer()} bytes)`);
  console.log(`Packed ${filesToPack.length} files for version ${version}.`);
});

archive.on("error", (error: Error) => {
  console.error(error);
  process.exit(1);
});

archive.pipe(output);

for (const file of filesToPack) {
  archive.append(createReadStream(join(extensionRoot, file)), { name: file });
}

async function main(): Promise<void> {
  await archive.finalize();
}

main().catch((error: unknown) => {
  console.error(error);
  process.exit(1);
});
