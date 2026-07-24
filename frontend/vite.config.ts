import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      manifest: false,
      injectRegister: "auto",
      registerType: "autoUpdate",
      includeAssets: ["icon.svg", "icons/*.png"],
      workbox: {
        globPatterns: ["**/*.{js,css,html,svg,png,ico}"],
        // Anything that looks like a static file (has a dot-extension) should never fall back
        // to the cached app shell - only real client-side routes (which have no extension) need
        // that fallback. Without this, navigating straight to e.g. /manifest.webmanifest or
        // /icons/icon-192.png serves index.html instead of the actual file.
        navigateFallbackDenylist: [/^\/api\//, /^\/health/, /\.[a-zA-Z0-9]+$/]
      }
    })
  ],
  server: {
    port: 5173,
    // The API runs on 5084 in development, not the 8080 it publishes in the container, so a
    // dev container can run on 8080 at the same time without the two fighting over the port.
    proxy: {
      "/api": "http://localhost:5084",
      "/health": "http://localhost:5084"
    }
  }
});
