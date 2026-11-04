/// <reference types="vite/client" />
import { convexTest } from "convex-test";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { internal } from "./_generated/api";
import schema from "./schema";

const modules = import.meta.glob("./**/*.ts");
const SITE = "https://contextsnap.vercel.app";
const TOKEN = "t".repeat(32);
const VISITOR = "3f2b8c1e-9a4d-4e7b-8c2a-1d5e6f7a8b9c";

const send = (t: ReturnType<typeof convexTest>, body: unknown, origin: string | null = SITE) =>
  t.fetch("/downloads", {
    method: "POST",
    headers: { "Content-Type": "text/plain;charset=UTF-8", ...(origin === null ? {} : { Origin: origin }) },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });

const stats = async (t: ReturnType<typeof convexTest>, days = 30) => {
  const response = await t.fetch(`/stats?days=${days}`, { headers: { Authorization: `Bearer ${TOKEN}` } });
  expect(response.status).toBe(200);
  return response.json();
};

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-11-04T12:00:00Z"));
  process.env.STATS_TOKEN = TOKEN;
  delete process.env.ALLOWED_VERSIONS;
  delete process.env.ALLOWED_ORIGINS;
});
afterEach(() => {
  vi.useRealTimers();
});

describe("POST /downloads", () => {
  test("records a Windows and a Linux request with the server's time", async () => {
    const t = convexTest(schema, modules);
    expect((await send(t, { platform: "windows", version: "1.0.1" })).status).toBe(204);
    expect((await send(t, { platform: "linux", version: "1.0.1" })).status).toBe(204);
    const rows = await t.run((ctx) => ctx.db.query("downloads").collect());
    expect(rows.map((row) => [row.platform, row.version, row.at, row.visitorId])).toEqual([
      ["windows", "1.0.1", Date.parse("2026-11-04T12:00:00Z"), undefined],
      ["linux", "1.0.1", Date.parse("2026-11-04T12:00:00Z"), undefined],
    ]);
    const s = await stats(t);
    expect(s.totalRequests).toBe(2);
    expect(s.byPlatform).toEqual({ windows: 1, linux: 1 });
    expect(s.uniqueRequesters).toBe(0);
  });

  test("ignores a missing, null, or foreign Origin", async () => {
    const t = convexTest(schema, modules);
    for (const origin of [null, "null", "https://evil.example", "http://contextsnap.vercel.app", "https://contextsnap.vercel.app.evil.example"]) {
      expect((await send(t, { platform: "windows", version: "1.0.1" }, origin)).status).toBe(204);
    }
    expect(await t.run((ctx) => ctx.db.query("downloads").collect())).toHaveLength(0);
  });

  test("accepts the old Vercel address and an origin in a different case", async () => {
    const t = convexTest(schema, modules);
    await send(t, { platform: "windows", version: "1.0.1" }, "https://context-snap-ashen.vercel.app");
    await send(t, { platform: "linux", version: "1.0.1" }, "https://ContextSnap.Vercel.App");
    expect((await stats(t)).totalRequests).toBe(2);
  });

  test("ALLOWED_ORIGINS replaces the default list", async () => {
    process.env.ALLOWED_ORIGINS = "http://localhost:8080, https://example.test/";
    const t = convexTest(schema, modules);
    await send(t, { platform: "windows", version: "1.0.1" }, SITE);
    await send(t, { platform: "windows", version: "1.0.1" }, "http://localhost:8080");
    await send(t, { platform: "linux", version: "1.0.1" }, "https://example.test");
    expect((await stats(t)).byPlatform).toEqual({ windows: 1, linux: 1 });
  });

  test("ALLOWED_VERSIONS limits the versions recorded", async () => {
    process.env.ALLOWED_VERSIONS = "1.0.0, 1.0.1";
    const t = convexTest(schema, modules);
    for (const version of ["1.0.0", "1.0.1", "1.0.2", "9.9.9", "1.0.1-beta.1"]) await send(t, { platform: "windows", version });
    expect((await stats(t)).byVersion.map((row: { version: string }) => row.version)).toEqual(["1.0.1", "1.0.0"]);
  });

  test("without ALLOWED_VERSIONS any well-formed version is recorded", async () => {
    const t = convexTest(schema, modules);
    await send(t, { platform: "linux", version: "2.3.4-rc.1" });
    expect((await stats(t)).totalRequests).toBe(1);
  });

  test("ignores malformed bodies, macOS, and bad IDs", async () => {
    const t = convexTest(schema, modules);
    const bad: unknown[] = [
      "not json",
      "[]",
      "null",
      { platform: "macos", version: "1.0.1" },
      { platform: "windows" },
      { platform: "windows", version: "1.0" },
      { platform: "windows", version: "1.0.1", visitorId: "abc" },
      { platform: "windows", version: "1.0.1", visitorId: 42 },
      JSON.stringify({ platform: "windows", version: "1.0.1", pad: "x".repeat(2000) }),
    ];
    for (const body of bad) expect((await send(t, body)).status).toBe(204);
    expect(await t.run((ctx) => ctx.db.query("downloads").collect())).toHaveLength(0);
  });

  test("the record mutation checks the version too", async () => {
    process.env.ALLOWED_VERSIONS = "1.0.1";
    const t = convexTest(schema, modules);
    expect(await t.mutation(internal.downloads.record, { platform: "windows", version: "1.0.0" })).toBe("rejected");
    expect(await t.mutation(internal.downloads.record, { platform: "windows", version: "x" })).toBe("rejected");
    expect(await t.mutation(internal.downloads.record, { platform: "windows", version: "1.0.1" })).toBe("recorded");
  });

  test("the same visitor within 10 s counts once; later, or another platform, counts again", async () => {
    const t = convexTest(schema, modules);
    const body = { platform: "windows", version: "1.0.1", visitorId: VISITOR };
    await send(t, body);
    vi.advanceTimersByTime(5_000);
    await send(t, body);
    await send(t, { ...body, platform: "linux" });
    vi.advanceTimersByTime(10_001);
    await send(t, body);
    const s = await stats(t);
    expect(s.byPlatform).toEqual({ windows: 2, linux: 1 });
    expect(s.uniqueRequesters).toBe(1);
  });
});

describe("GET /stats", () => {
  test("needs a configured token of 24+ characters and the right one", async () => {
    const t = convexTest(schema, modules);
    process.env.STATS_TOKEN = "short";
    expect((await t.fetch("/stats", { headers: { Authorization: `Bearer ${"short"}` } })).status).toBe(503);
    process.env.STATS_TOKEN = TOKEN;
    expect((await t.fetch("/stats")).status).toBe(401);
    expect((await t.fetch("/stats", { headers: { Authorization: "Bearer wrong" } })).status).toBe(401);
    expect((await t.fetch("/stats", { headers: { Authorization: `Bearer ${TOKEN}` } })).status).toBe(200);
  });

  test("daily covers the span with empty days; monthly covers 12 calendar months across a year end", async () => {
    const t = convexTest(schema, modules);
    const at = async (iso: string, platform: "windows" | "linux", times = 1) => {
      vi.setSystemTime(new Date(iso));
      for (let i = 0; i < times; i++) await send(t, { platform, version: "1.0.1" });
    };
    await at("2025-11-30T23:59:00Z", "windows"); // 13 months back: outside the monthly window
    await at("2025-12-01T00:00:00Z", "linux"); // first month shown
    await at("2026-01-15T10:00:00Z", "windows", 2);
    await at("2026-11-03T08:00:00Z", "linux", 3);
    await at("2026-11-04T12:00:00Z", "windows");

    const s = await stats(t, 7);
    expect(s.totalRequests).toBe(8);
    expect(s.daily).toHaveLength(7);
    expect(s.daily[0].day).toBe("2026-10-29");
    expect(s.daily.at(-2)).toEqual({ day: "2026-11-03", windows: 0, linux: 3 });
    expect(s.daily.at(-1)).toEqual({ day: "2026-11-04", windows: 1, linux: 0 });

    expect(s.monthly).toHaveLength(12);
    expect(s.monthly[0]).toEqual({ month: "2025-12", windows: 0, linux: 1 });
    expect(s.monthly[1]).toEqual({ month: "2026-01", windows: 2, linux: 0 });
    expect(s.monthly[2]).toEqual({ month: "2026-02", windows: 0, linux: 0 });
    expect(s.monthly.at(-1)).toEqual({ month: "2026-11", windows: 1, linux: 3 });
  });

  test("clamps the day span", async () => {
    const t = convexTest(schema, modules);
    expect((await stats(t, 0)).daily).toHaveLength(1);
    expect((await stats(t, 5000)).daily).toHaveLength(366);
  });
});
