import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";

// Download requests from the ContextSnap website (website/app.js). A click on a download button is a
// request, not a completed download or an installation. No names, emails, IP addresses, user agents, or
// fingerprints are stored: only the platform, the release version, the server's time, and a random
// per-browser ID that is sent only after the visitor accepted analytics cookies.
export const platform = v.union(v.literal("windows"), v.literal("linux"));

export default defineSchema({
  // One row per recorded request. `visitorId` is absent unless the visitor accepted analytics.
  downloads: defineTable({
    platform,
    version: v.string(),
    at: v.number(),
    visitorId: v.optional(v.string()),
  })
    .index("by_visitor_and_at", ["visitorId", "at"])
    .index("by_at", ["at"]),

  // Each random browser ID seen at least once: the estimate of unique requesters.
  visitors: defineTable({
    visitorId: v.string(),
    firstSeen: v.number(),
  }).index("by_visitor", ["visitorId"]),

  // Requests per UTC day, platform, and version, kept up to date by `record` so the dashboard reads a
  // few small rows instead of every event.
  dailyCounts: defineTable({
    day: v.string(), // YYYY-MM-DD (UTC)
    platform,
    version: v.string(),
    requests: v.number(),
  }).index("by_day_platform_version", ["day", "platform", "version"]),

  // Running totals (currently only "uniqueVisitors").
  counters: defineTable({
    name: v.string(),
    value: v.number(),
  }).index("by_name", ["name"]),
});
