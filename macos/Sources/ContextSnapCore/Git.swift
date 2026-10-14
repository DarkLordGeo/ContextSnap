import Foundation

/// Parses `git status --porcelain=v2 --branch -z` (same rules as Windows).
public enum GitStatusParser {
    public static func parse(root: String, output: String, maxListedChanges: Int) -> GitRepository {
        var repo = GitRepository(root: root)
        var changes: [GitChange] = []

        func add(_ code: String, _ path: String, _ original: String?) {
            repo.totalChanges += 1
            if changes.count < maxListedChanges { changes.append(GitChange(code: code, path: path, originalPath: original)) }
        }

        let fields = output.components(separatedBy: "\0")
        var index = 0
        while index < fields.count {
            let entry = fields[index]
            defer { index += 1 }
            guard entry.count >= 2 else { continue }

            if entry.hasPrefix("# ") {
                let parts = entry.split(separator: " ", maxSplits: 2, omittingEmptySubsequences: false).map(String.init)
                guard parts.count == 3 else { continue }
                let value = parts[2]
                switch parts[1] {
                case "branch.oid":
                    if value == "(initial)" { repo.hasNoCommits = true } else { repo.headShortHash = String(value.prefix(7)) }
                case "branch.head":
                    if value == "(detached)" { repo.isDetached = true } else { repo.branch = value }
                case "branch.upstream":
                    repo.upstream = value
                case "branch.ab":
                    let counts = value.split(separator: " ")
                    if counts.count == 2 {
                        repo.ahead = Int(counts[0].dropFirst()) ?? 0
                        repo.behind = Int(counts[1].dropFirst()) ?? 0
                    }
                default:
                    break
                }
                continue
            }

            switch entry.first {
            case "1":
                let parts = entry.split(separator: " ", maxSplits: 8, omittingEmptySubsequences: false).map(String.init)
                guard parts.count == 9 else { continue }
                count(parts[1], &repo)
                add(parts[1].replacingOccurrences(of: ".", with: " "), parts[8], nil)
            case "2":
                let parts = entry.split(separator: " ", maxSplits: 9, omittingEmptySubsequences: false).map(String.init)
                let original = index + 1 < fields.count ? fields[index + 1] : nil
                index += 1
                guard parts.count == 10 else { continue }
                count(parts[1], &repo)
                add(parts[1].replacingOccurrences(of: ".", with: " "), parts[9], original)
            case "u":
                let parts = entry.split(separator: " ", maxSplits: 10, omittingEmptySubsequences: false).map(String.init)
                guard parts.count == 11 else { continue }
                repo.conflicted += 1
                add(parts[1], parts[10], nil)
            case "?":
                repo.untracked += 1
                add("??", String(entry.dropFirst(2)), nil)
            default:
                break
            }
        }

        repo.changes = changes
        return repo
    }

    /// Parses `git log --format=%h%x1f%s%x1f%cr`.
    public static func parseLog(_ output: String) -> [GitCommit] {
        output.split(separator: "\n").compactMap { line in
            let parts = line.split(separator: "\u{1f}", omittingEmptySubsequences: false).map { String($0).trimmingCharacters(in: .whitespaces) }
            guard parts.count >= 2, !parts[0].isEmpty else { return nil }
            return GitCommit(hash: parts[0], subject: parts[1], relativeDate: parts.count > 2 ? parts[2] : nil)
        }
    }

    private static func count(_ xy: String, _ repo: inout GitRepository) {
        let characters = Array(xy)
        guard characters.count == 2 else { return }
        if characters[0] != "." { repo.staged += 1 }
        if characters[1] != "." { repo.unstaged += 1 }
    }
}

public enum RepositoryLocator {
    /// Nearest folder at or above `path` that contains `.git`.
    public static func findRoot(_ path: String, fileManager: FileManager = .default) -> String? {
        var url = URL(fileURLWithPath: path).standardizedFileURL
        var isDirectory: ObjCBool = false
        if fileManager.fileExists(atPath: url.path, isDirectory: &isDirectory), !isDirectory.boolValue {
            url.deleteLastPathComponent()
        }
        for _ in 0..<64 {
            if fileManager.fileExists(atPath: url.appendingPathComponent(".git").path) { return url.path }
            let parent = url.deletingLastPathComponent()
            if parent.path == url.path { return nil }
            url = parent
        }
        return nil
    }
}

/// Runs read-only Git commands: absolute executable path, argument list (no shell), optional locks
/// and fsmonitor disabled, bounded output, and a timeout.
public struct GitRunner: Sendable {
    public static let candidates = [
        "/opt/homebrew/bin/git",
        "/usr/local/bin/git",
        "/Library/Developer/CommandLineTools/usr/bin/git",
        "/Applications/Xcode.app/Contents/Developer/usr/bin/git",
    ]

    public let executable: String?

    /// `/usr/bin/git` is deliberately not used: without developer tools it opens an install prompt.
    public init(executable: String? = GitRunner.candidates.first { FileManager.default.isExecutableFile(atPath: $0) }) {
        self.executable = executable
    }

    public static func arguments(root: String, _ arguments: [String]) -> [String] {
        ["--no-pager", "--no-optional-locks", "-c", "core.fsmonitor=false", "-c", "log.showSignature=false", "-c", "color.ui=false", "-C", root] + arguments
    }

    public func run(root: String, _ arguments: [String], timeout: TimeInterval = 5, maxOutput: Int = 512 * 1024) -> (status: Int32, output: String, error: String, timedOut: Bool) {
        guard let executable else { return (-1, "", "Git was not found.", false) }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = Self.arguments(root: root, arguments)
        process.currentDirectoryURL = URL(fileURLWithPath: root)
        var environment = ProcessInfo.processInfo.environment
        for name in ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_EXTERNAL_DIFF", "GIT_PAGER", "GIT_EDITOR", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_ASKPASS", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"] {
            environment.removeValue(forKey: name)
        }
        environment["GIT_TERMINAL_PROMPT"] = "0"
        environment["GIT_OPTIONAL_LOCKS"] = "0"
        process.environment = environment

        let out = Pipe(), err = Pipe()
        process.standardOutput = out
        process.standardError = err
        process.standardInput = FileHandle.nullDevice

        let finished = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in finished.signal() }
        do {
            try process.run()
        } catch {
            return (-1, "", "Git could not be started.", false)
        }

        // Read concurrently so a large output cannot block the child process.
        var outputData = Data(), errorData = Data()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue.global().async {
            outputData = out.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }
        group.enter()
        DispatchQueue.global().async {
            errorData = err.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }

        if finished.wait(timeout: .now() + timeout) == .timedOut {
            process.terminate()
            return (-1, "", "", true)
        }
        _ = group.wait(timeout: .now() + 2)
        let output = String(decoding: outputData.prefix(maxOutput), as: UTF8.self)
        let errorText = String(decoding: errorData.prefix(4096), as: UTF8.self)
        return (process.terminationStatus, output, errorText, false)
    }

    /// Status and recent commits for one repository.
    public func read(root: String, maxChanges: Int) -> GitRepository {
        let status = run(root: root, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal", "--ignore-submodules=dirty"])
        if status.timedOut {
            var failed = GitRepository(root: root)
            failed.error = "git status timed out after 5 seconds."
            return failed
        }
        guard status.status == 0 else {
            var failed = GitRepository(root: root)
            failed.error = status.error.contains("dubious ownership") || status.error.contains("safe.directory")
                ? "Git refused to read this repository because it is owned by another user (safe.directory)."
                : "git status exited with code \(status.status)."
            return failed
        }
        var repo = GitStatusParser.parse(root: root, output: status.output, maxListedChanges: maxChanges)
        if !repo.hasNoCommits {
            let log = run(root: root, ["log", "-3", "--no-color", "--format=%h%x1f%s%x1f%cr"])
            if log.status == 0 && !log.timedOut { repo.recentCommits = GitStatusParser.parseLog(log.output) }
        }
        return repo
    }
}
