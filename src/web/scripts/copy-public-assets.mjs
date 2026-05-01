import { copyFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const webRoot = join(root, "..");
const logoSrc = join(webRoot, "public", "logo.png");
const wwwroot = join(webRoot, "..", "service", "AriaSignature.Service", "wwwroot");
const logoDest = join(wwwroot, "logo.png");

if (existsSync(logoSrc)) {
  copyFileSync(logoSrc, logoDest);
}
