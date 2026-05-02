import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

/** Уникален на каждый vite build — ломает кэш WebView по `./logo.png` после обновления брендинга. */
const logoCacheBust = String(Date.now());

export default defineConfig({
  define: {
    __LOGO_CACHE_BUST__: JSON.stringify(logoCacheBust),
  },
  plugins: [
    react(),
    {
      name: "aria-logo-cache-bust-index",
      transformIndexHtml(html) {
        return html.replace(/\.\/logo\.png/g, `./logo.png?v=${logoCacheBust}`);
      },
    },
  ],
  base: "./",
  publicDir: path.resolve(__dirname, "public"),
  build: {
    outDir: path.resolve(__dirname, "../service/AriaSignature.Service/wwwroot"),
    emptyOutDir: true,
  },
});
