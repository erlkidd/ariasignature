import { copyFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const webRoot = join(root, "..");
const repoRoot = join(webRoot, "..", "..");
const logoSrcPublic = join(webRoot, "public", "logo.png");
const logoSrcBranding = join(repoRoot, "assets", "branding", "logo.png");
const melezhSrcPublic = join(webRoot, "public", "melezh_long.png");
const melezhSrcBranding = join(repoRoot, "assets", "branding", "melezh_long.png");
const wwwroot = join(webRoot, "..", "service", "AriaSignature.Service", "wwwroot");
const logoDest = join(wwwroot, "logo.png");
const melezhDest = join(wwwroot, "melezh_long.png");

if (existsSync(logoSrcPublic)) {
  copyFileSync(logoSrcPublic, logoDest);
} else if (existsSync(logoSrcBranding)) {
  copyFileSync(logoSrcBranding, logoDest);
} else {
  console.warn(
    "copy-public-assets: skip logo.png — neither src/web/public/logo.png nor assets/branding/logo.png found"
  );
}

if (existsSync(melezhSrcPublic)) {
  copyFileSync(melezhSrcPublic, melezhDest);
} else if (existsSync(melezhSrcBranding)) {
  copyFileSync(melezhSrcBranding, melezhDest);
} else {
  console.warn(
    "copy-public-assets: skip melezh_long.png — neither src/web/public/melezh_long.png nor assets/branding/melezh_long.png found"
  );
}
