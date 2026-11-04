import { defineConfig } from "vitest/config";

// Tests for the Convex download-request functions (convex/*.test.ts), run with convex-test.
export default defineConfig({
  test: {
    environment: "edge-runtime",
    include: ["convex/**/*.test.ts"],
    server: { deps: { inline: ["convex-test"] } },
  },
});
