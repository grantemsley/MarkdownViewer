import { defineConfig } from "vitest/config";

// Environment is node, not jsdom: bridge.js reads the DOM once at IIFE time,
// so each test builds its own JSDOM instance via harness.js and re-executes
// the source against a fresh document.
export default defineConfig({
  test: {
    environment: "node",
    include: ["**/*.test.js"],
  },
});
