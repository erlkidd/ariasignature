import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import pngToIco from "png-to-ico";

const root = dirname(fileURLToPath(import.meta.url));
const webRoot = join(root, "..");
const repoRoot = join(webRoot, "..", "..");
const logoPath = join(repoRoot, "assets", "branding", "logo.png");
const icoPath = join(repoRoot, "assets", "branding", "icon.ico");

if (!existsSync(logoPath)) {
  console.error(`sync-branding-icons-from-logo: missing ${logoPath}`);
  process.exit(1);
}

const pngBuf = readFileSync(logoPath);
const icoBuf = await pngToIco(pngBuf);
writeFileSync(icoPath, icoBuf);
console.log("sync-branding-icons-from-logo: wrote assets/branding/icon.ico from logo.png");
