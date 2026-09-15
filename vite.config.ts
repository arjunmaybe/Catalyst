import { defineConfig } from "vite";

// Tauri expects a fixed dev-server port.
export default defineConfig({
  clearScreen: false,
  server: {
    port: 1420,
    strictPort: true,
  },
  envPrefix: ["VITE_", "TAURI_"],
  build: {
    // WebView2 is evergreen Chromium; esnext output is fine.
    target: "esnext",
    minify: "esbuild",
  },
});
