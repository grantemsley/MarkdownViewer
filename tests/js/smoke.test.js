import { test, expect } from "vitest";
import { boot } from "./harness.js";

test("bridge announces ready on load", () => {
  const h = boot();
  expect(h.sent).toContainEqual({ type: "ready" });
});
