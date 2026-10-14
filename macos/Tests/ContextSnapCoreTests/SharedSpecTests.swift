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
}
