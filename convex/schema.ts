import { defineSchema, defineTable } from "convex/server";
import { v } from "convex/values";

// Download attempts from the ContextSnap website (website/app.js). A click on a download button is an
// attempt, not a completed download. No names, emails, IP addresses, user agents, or fingerprints are
// stored: only the platform, the release version, the time, and a random per-browser ID (optional).
export const platform = v.union(v.literal("windows"), v.literal("linux"));

export default defineSchema({
  // One row per recorded attempt. `visitorId` is absent when the visitor declined analytics.
  downloads: defineTable({
    platform,
    version: v.string(),
    at: v.number(),
    visitorId: v.optional(v.string()),
  })
    .index("by_visitor_and_at", ["visitorId", "at"])
    .index("by_at", ["at"]),

  // Each random browser ID seen at least once: the estimate of unique downloaders.
  visitors: defineTable({
    visitorId: v.string(),
    firstSeen: v.number(),
  }).index("by_visitor", ["visitorId"]),

  // Attempts per UTC day, platform, and version, kept up to date by `record` so the dashboard reads a
  // few small rows instead of every event.
  dailyCounts: defineTable({
    day: v.string(), // YYYY-MM-DD (UTC)
    platform,
    version: v.string(),
    attempts: v.number(),
  }).index("by_day_platform_version", ["day", "platform", "version"]),

  // Running totals (currently only "uniqueVisitors").
  counters: defineTable({
    name: v.string(),
    value: v.number(),
  }).index("by_name", ["name"]),
});
