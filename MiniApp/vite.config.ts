import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/app/",
  server: {
    proxy: {
      "/api": "http://127.0.0.1:5267",
      "/privacy": "http://127.0.0.1:5267"
    }
  },
  build: { outDir: "../wwwroot/app", emptyOutDir: true }
});
