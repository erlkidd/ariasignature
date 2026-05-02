import { copyFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const webRoot = join(root, "..");
const repoRoot = join(webRoot, "..", "..");
const logoSrcPublic = join(webRoot, "public", "logo.png");
const logoSrcBranding = join(repoRoot, "assets", "branding", "logo.png");
const wwwroot = join(webRoot, "..", "service", "AriaSignature.Service", "wwwroot");
const logoDest = join(wwwroot, "logo.png");

if (existsSync(logoSrcPublic)) {
  copyFileSync(logoSrcPublic, logoDest);
} else if (existsSync(logoSrcBranding)) {
  copyFileSync(logoSrcBranding, logoDest);
} else {
  console.warn(
    "copy-public-assets: skip logo.png — neither src/web/public/logo.png nor assets/branding/logo.png found"
  );
}
