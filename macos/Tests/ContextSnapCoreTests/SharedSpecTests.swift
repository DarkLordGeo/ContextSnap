import CoreGraphics
import Foundation
import XCTest
@testable import ContextSnapCore

/// Runs the cross-platform fixtures in ../spec, shared with the Windows tests.
final class SharedSpecTests: XCTestCase {
    private func load(_ name: String) throws -> [String: Any] {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("spec").appendingPathComponent(name)
        return try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as! [String: Any]
    }

    func testRedactionMatchesSharedSpec() throws {
        for item in try load("redaction-cases.json")["cases"] as! [[String: String]] {
            XCTAssertEqual(Redactor.default.redact(item["input"]!), item["expected"]!, item["name"]!)
        }
    }

    func testMarkdownMatchesSharedSpec() throws {
        let spec = try load("markdown-cases.json")
        XCTAssertEqual(spec["truncationNotice"] as? String, MarkdownFormatter.truncationNotice)
        for item in spec["escape"] as! [[String: String]] { XCTAssertEqual(MarkdownText.escape(item["input"]!), item["expected"]!) }
        for item in spec["codeSpan"] as! [[String: String]] { XCTAssertEqual(MarkdownText.code(item["input"]!), item["expected"]!) }
        for item in spec["fence"] as! [[String: String]] { XCTAssertEqual(MarkdownText.fence(item["input"]!), item["expected"]!) }
        for item in spec["limitOutput"] as! [[String: Any]] {
            XCTAssertEqual(MarkdownFormatter.limitOutput(item["input"] as! String, maxCharacters: item["maxCharacters"] as! Int), item["expected"] as! String)
        }
        for item in spec["textStatistics"] as! [[String: Any]] {
            let stats = TextStatistics.measure(item["input"] as! String)
            XCTAssertEqual(stats.characters, item["characters"] as! Int)
            XCTAssertEqual(stats.words, item["words"] as! Int)
            XCTAssertEqual(stats.bytes, item["utf8Bytes"] as! Int)
        }
        for item in spec["formatSize"] as! [[String: Any]] {
            XCTAssertEqual(MarkdownFormatter.formatSize(item["bytes"] as! Int), item["expected"] as! String)
        }
    }

    func testGitStatusParsing() {
        let output = ["# branch.oid 0123456789abcdef", "# branch.head main", "# branch.upstream origin/main", "# branch.ab +2 -1",
                      "1 M. N... 100644 100644 100644 a b src/a.swift", "? notes.txt", ""].joined(separator: "\u{0}")
        let repo = GitStatusParser.parse(root: "/r", output: output, maxListedChanges: 10)
        XCTAssertEqual(repo.branch, "main")
        XCTAssertEqual(repo.ahead, 2)
        XCTAssertEqual(repo.staged, 1)
        XCTAssertEqual(repo.untracked, 1)
        XCTAssertEqual(repo.changes.map(\.path), ["src/a.swift", "notes.txt"])
    }

    func testAssemblerSkipsSecureFieldsAndOffscreenText() {
        func row(_ y: CGFloat) -> CGRect { CGRect(x: 10, y: y, width: 200, height: 20) }
        let root = AXNode(role: "AXWindow", frame: CGRect(x: 0, y: 0, width: 800, height: 600), children: [
            AXNode(role: "AXStaticText", text: "Visible", frame: row(10)),
            AXNode(role: "AXTextField", subrole: "AXSecureTextField", text: "hunter2", frame: row(40)),
            AXNode(role: "AXStaticText", text: "Below", frame: row(900)),
        ])
        let result = VisibleTextAssembler.assemble(root, visibleArea: CGRect(x: 0, y: 0, width: 800, height: 600), maxCharacters: 1000)
        XCTAssertEqual(result.text, "Visible")
        XCTAssertEqual(result.skippedSecureFields, 1)
    }

    func testOCROrderReadsColumns() {
        let lines = [OCRLine(text: "Message", frame: CGRect(x: 300, y: 20, width: 400, height: 16)),
                     OCRLine(text: "Sidebar", frame: CGRect(x: 10, y: 60, width: 150, height: 16))]
        XCTAssertEqual(OCRLayout.order(lines), ["Sidebar", "Message"])
    }

    func testLimitCutsOverlongLineAtWordBoundary() {
        let token = "ghp_" + String(repeating: "A", count: 36)
        let (text, truncated) = VisibleTextAssembler.limit(["see \(token) here"], maxCharacters: 20)
        XCTAssertTrue(truncated)
        XCTAssertEqual(text, "see…")
    }

    func testGitFilterOverridesDisableEveryConfiguredFilter() throws {
        let overrides = try XCTUnwrap(GitRunner.filterOverrides(configOutput:
            "filter.evil.clean\necho x; cat\0filter.lfs.process\ngit-lfs filter-process\0filter.My Name.required\ntrue\0"))
        for name in ["evil", "lfs", "My Name"] {
            for option in ["clean=", "smudge=", "process=", "required=false"] {
                XCTAssertTrue(overrides.contains("filter.\(name).\(option)"), "\(name).\(option)")
            }
        }
        XCTAssertEqual(overrides.count, 3 * 8)
        XCTAssertEqual(GitRunner.filterOverrides(configOutput: ""), [])
        XCTAssertNil(GitRunner.filterOverrides(configOutput: "filter.a=b.clean\necho x\0"))
    }

    /// A repository's own config can define filter drivers that Git runs as shell commands while reading
    /// files. Reading Git context from such a repository must not run them.
    func testGitDoesNotRunRepositoryFilters() throws {
        let runner = GitRunner()
        guard let git = runner.executable else { throw XCTSkip("Git is not installed.") }
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("ContextSnapMaliciousGit-\(UUID().uuidString)").resolvingSymlinksInPath().path
        try FileManager.default.createDirectory(atPath: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: root) }
        func runGit(_ arguments: String...) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: git)
            process.arguments = ["-C", root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"] + arguments
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try? process.run()
            process.waitUntilExit()
        }
        let marker = root + "/filter-ran.marker"

        runGit("init", "--quiet", "--initial-branch=main")
        try "a".write(toFile: root + "/a.txt", atomically: true, encoding: .utf8)
        try "a.txt filter=evil\n".write(toFile: root + "/.gitattributes", atomically: true, encoding: .utf8)
        runGit("add", "a.txt", ".gitattributes")
        runGit("commit", "--quiet", "-m", "First commit")
        // Malicious configuration added after the commit; a newer timestamp makes Git re-read (and filter) the file.
        runGit("config", "filter.evil.clean", "echo clean >> filter-ran.marker; cat")
        runGit("config", "filter.evil.required", "true")
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(300)], ofItemAtPath: root + "/a.txt")

        let repo = runner.read(root: root, maxChanges: 10)

        XCTAssertFalse(FileManager.default.fileExists(atPath: marker), "A repository filter ran while reading Git context.")
        XCTAssertNil(repo.error)
        XCTAssertEqual(repo.branch, "main")
        XCTAssertEqual(repo.recentCommits.first?.subject, "First commit")

        // Control: a plain `git status` does run the filter, so the fixture is really malicious.
        runGit("status")
        XCTAssertTrue(FileManager.default.fileExists(atPath: marker))
    }

    func testVisibleTextIsReviewedBeforeCopyingByDefault() {
        let window = WindowText(appName: "Safari", windowTitle: "Chat", isFocused: true, accessibilityCharacters: 4, ocrCharacters: 0,
                                text: "Chat", truncated: false, warnings: [])
        XCTAssertFalse(CopyBehavior.copiesImmediately(copyWithoutReview: false, visibleText: .collected("Visible screen text", [window])))
        XCTAssertTrue(CopyBehavior.copiesImmediately(copyWithoutReview: true, visibleText: .collected("Visible screen text", [window])))
        XCTAssertTrue(CopyBehavior.copiesImmediately(copyWithoutReview: false, visibleText: .disabled("Visible screen text")))
        XCTAssertTrue(CopyBehavior.copiesImmediately(copyWithoutReview: false, visibleText: .collected("Visible screen text", [])))
    }

    func testTruncationNeverLeavesAnOpenFenceOrPutsTheNoticeInsideContent() {
        let snapshot = "# Context Snapshot\n\n## Visible text\n\n### Active window: Chat\n\n````text\nuse ```code``` here\n"
            + (0..<80).map { "visible line \($0)" }.joined(separator: "\n")
            + "\n````\n\n## System context\n\n" + (0..<40).map { "- item \($0)" }.joined(separator: "\n") + "\n"
        for max in 50...1_200 {
            let output = MarkdownFormatter.limitOutput(snapshot, maxCharacters: max)
            XCTAssertLessThanOrEqual((output as NSString).length, max, "max \(max)")
            XCTAssertTrue(output.hasSuffix(MarkdownFormatter.truncationNotice), "max \(max)")
            let body = String(output.dropLast(MarkdownFormatter.truncationNotice.count))
            XCTAssertNil(MarkdownFormatter.openFence(body as NSString), "max \(max)")
        }
    }
}
