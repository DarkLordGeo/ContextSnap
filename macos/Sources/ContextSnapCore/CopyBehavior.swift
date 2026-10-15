/// What a capture does with a fresh snapshot; the same rule as the Windows app's global shortcut.
public enum CopyBehavior {
    /// A snapshot that includes visible screen text is shown for review before anything is copied,
    /// unless the user turned on one-step copying. Snapshots without screen text are copied immediately.
    public static func copiesImmediately(copyWithoutReview: Bool, visibleText: Section<WindowText>) -> Bool {
        copyWithoutReview || !(visibleText.status == .ok && !visibleText.items.isEmpty)
    }
}
