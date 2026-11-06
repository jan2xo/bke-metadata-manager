import SwiftUI
import AppKit
import UniformTypeIdentifiers
import Foundation

// Native macOS presentation. The transfer engine remains the verified .NET
// MediaOrganizer and is invoked by a small JSON bridge bundled inside the app.
private enum Palette {
    static let gold = Color(red: 0.98, green: 0.81, blue: 0.51)
}

struct StoredSettings: Codable {
    var destinationFolder: String = ""
    var categories: [String] = ["Establishing", "Detail", "Drone"]
}

struct OrganizeBatchRequest: Encodable {
    let eventName: String
    let category: String
    let destinationFolder: String
    let paths: [String]
}
struct OrganizeFileResult: Decodable {
    let sourcePath: String
    let destinationPath: String?
    let warning: String?
    let error: String?
}
struct OrganizeBatchResponse: Decodable {
    let results: [OrganizeFileResult]
}

private enum BackendBridge {
    static func organize(_ request: OrganizeBatchRequest) throws -> OrganizeBatchResponse {
        guard let resourceURL = Bundle.main.resourceURL else {
            throw NSError(domain: "BKE", code: 1, userInfo: [NSLocalizedDescriptionKey: "App resources unavailable."])
        }
        let binary = resourceURL.appendingPathComponent("Backend/Bke.MetadataManager")
        guard FileManager.default.isExecutableFile(atPath: binary.path) else {
            throw NSError(domain: "BKE", code: 2, userInfo: [NSLocalizedDescriptionKey: "Media mover is missing from the app bundle."])
        }

        let task = Process()
        task.executableURL = binary
        task.arguments = ["--organize-json"]
        let input = Pipe()
        let output = Pipe()
        let errors = Pipe()
        task.standardInput = input
        task.standardOutput = output
        task.standardError = errors
        let payload = try JSONEncoder().encode(request)
        try task.run()
        try input.fileHandleForWriting.write(contentsOf: payload)
        try input.fileHandleForWriting.close()
        let resultData = output.fileHandleForReading.readDataToEndOfFile()
        task.waitUntilExit()
        if task.terminationStatus != 0 {
            let details = String(decoding: errors.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
            throw NSError(domain: "BKE", code: Int(task.terminationStatus),
                          userInfo: [NSLocalizedDescriptionKey: details.isEmpty ? "The media mover stopped unexpectedly." : details])
        }
        return try JSONDecoder().decode(OrganizeBatchResponse.self, from: resultData)
    }
}

// NSItemProvider calls must BEGIN inside the drop handler, not afterward.
// The accumulator avoids concurrent writes from multiple provider completions.
private final class DroppedURLs: @unchecked Sendable {
    private let lock = NSLock()
    private var values: [URL] = []
    func add(_ url: URL) {
        lock.lock()
        values.append(url)
        lock.unlock()
    }
    func snapshot() -> [URL] {
        lock.lock()
        defer { lock.unlock() }
        return values
    }
}

@MainActor
final class OrganizerState: ObservableObject {
    @Published var eventName = ""
    @Published var destinationFolder = ""
    @Published var categories = ["Establishing", "Detail", "Drone"]
    @Published var busy = false
    @Published var movedCount = 0
    @Published var movedPerCategory: [String: Int] = [:]
    @Published var status = "Choose a destination in Settings, then drop your footage."
    @Published var detail = "Your original files are removed only after a verified move."
    @Published var hasError = false

    private var settingsURL: URL {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return root.appendingPathComponent("BKE/MediaOrganizer/settings.json")
    }

    func load() {
        do {
            let contents = try Data(contentsOf: settingsURL)
            let settings = try JSONDecoder().decode(StoredSettings.self, from: contents)
            destinationFolder = settings.destinationFolder
            categories = settings.categories // An intentionally empty registry must stay empty after restart.
            status = "Ready to organize"
            detail = destinationFolder.isEmpty
                ? "Choose your destination in Settings."
                : "Drop files onto a category. The original source plate will clear as files move."
        } catch {
            // A missing settings file is normal on the first launch.
            if FileManager.default.fileExists(atPath: settingsURL.path) {
                status = "Settings could not be read"
                detail = error.localizedDescription
                hasError = true
            }
        }
    }

    @discardableResult
    func save() -> Bool {
        do {
            let settings = StoredSettings(destinationFolder: destinationFolder, categories: categories)
            let parent = settingsURL.deletingLastPathComponent()
            try FileManager.default.createDirectory(at: parent, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(settings).write(to: settingsURL, options: .atomic)
            return true
        } catch {
            status = "Settings could not be saved"
            detail = error.localizedDescription
            hasError = true
            return false
        }
    }

    func addCategory(_ proposed: String) throws {
        let value = Self.sanitized(proposed)
        guard !value.isEmpty else {
            throw NSError(domain: "BKE", code: 10, userInfo: [NSLocalizedDescriptionKey: "Enter a category name."])
        }
        guard !categories.contains(where: { $0.caseInsensitiveCompare(value) == .orderedSame }) else {
            throw NSError(domain: "BKE", code: 11, userInfo: [NSLocalizedDescriptionKey: "That category already exists."])
        }
        categories.append(value)
        if !save() {
            categories.removeLast()
            throw NSError(domain: "BKE", code: 12,
                          userInfo: [NSLocalizedDescriptionKey: "Could not persist the new category."])
        }
    }

    // Removing a tile only updates the JSON registry, never any on-disk footage.
    func removeCategory(_ name: String) {
        guard !busy else { return }
        let previous = categories
        categories.removeAll { $0 == name }
        guard categories.count != previous.count else { return }
        if !save() {
            categories = previous
            return
        }
        movedPerCategory.removeValue(forKey: name)
        hasError = false
        status = "Category removed"
        detail = "\(name) was removed from the tiles. Existing event media remains untouched."
    }

    static func sanitized(_ value: String) -> String {
        value.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "[^\\p{L}\\p{N}_-]+", with: "-", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: "-_"))
    }

    var destinationPreview: String {
        guard !destinationFolder.isEmpty else { return "Choose a destination in Settings" }
        let name = Self.sanitized(eventName)
        guard !name.isEmpty else { return destinationFolder }
        return (destinationFolder as NSString).appendingPathComponent(name)
    }

    var filenamePreview: String {
        let event = Self.sanitized(eventName)
        let date = DateFormatter()
        date.dateFormat = "yyyy-MM-dd"
        return date.string(from: Date()) + "-C0123-" + (event.isEmpty ? "EVENT" : event) + "-Drone.MP4"
    }

    var canAccept: Bool {
        !busy && !Self.sanitized(eventName).isEmpty &&
        !destinationFolder.isEmpty && FileManager.default.fileExists(atPath: destinationFolder)
    }

    // Each tile is its own drop target. No category selection required.
    func accept(_ providers: [NSItemProvider], category: String) -> Bool {
        guard canAccept else {
            status = "Enter an event and choose a destination first"
            detail = "Nothing has been moved."
            hasError = true
            return false
        }
        guard !providers.isEmpty else { return false }

        let group = DispatchGroup()
        let urls = DroppedURLs()
        var started = 0

        for provider in providers where provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            started += 1
            group.enter()
            provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
                let url: URL?
                if let file = item as? URL { url = file }
                else if let file = item as? NSURL { url = file as URL }
                else if let data = item as? Data { url = URL(dataRepresentation: data, relativeTo: nil) }
                else if let value = item as? String { url = URL(string: value) }
                else { url = nil }
                if let url = url, url.isFileURL { urls.add(url) }
                group.leave()
            }
        }
        if started == 0 { return false }
        group.notify(queue: .main) { [weak self] in
            guard let self = self else { return }
            let paths = urls.snapshot().map(\.path).filter { !($0 as NSString).lastPathComponent.isEmpty }
            Task { await self.organize(paths: paths, category: category) }
        }
        return true
    }

    func organize(paths: [String], category: String) async {
        guard !busy else { return }
        guard !paths.isEmpty else {
            status = "No usable files were dropped"
            detail = "Try dragging files directly from Finder."
            hasError = true
            return
        }
        busy = true
        hasError = false
        status = "Organizing \(paths.count) file(s)…"
        detail = "Validating and moving to \(category)"
        let request = OrganizeBatchRequest(eventName: eventName,
                                           category: category,
                                           destinationFolder: destinationFolder,
                                           paths: paths)
        do {
            let report = try await Task.detached(priority: .userInitiated) {
                try BackendBridge.organize(request)
            }.value
            let moved = report.results.filter { $0.destinationPath != nil }
            let rejected = report.results.filter { $0.error != nil }
            let warnings = report.results.compactMap(\.warning)
            movedCount += moved.count
            movedPerCategory[category, default: 0] += moved.count
            hasError = !rejected.isEmpty || !warnings.isEmpty
            if rejected.isEmpty && warnings.isEmpty {
                status = movedCount == 1 ? "1 file moved and ready" : "\(movedCount) files moved and ready"
                detail = "Organized \(moved.count) into \(category) · Renamed and moved into \(Self.sanitized(eventName))."
            } else {
                status = "\(moved.count) moved · \(rejected.count) not moved"
                detail = (rejected.first?.error ?? warnings.first ?? "Check file operation details.")
                if !warnings.isEmpty { detail += " · Manifest warning: \(warnings[0])" }
            }
        } catch {
            hasError = true
            status = "Could not process dropped files"
            detail = error.localizedDescription
        }
        busy = false
    }

    func chooseDestination() {
        let picker = NSOpenPanel()
        picker.title = "Select a destination for organized media"
        picker.prompt = "Use Folder"
        picker.canChooseDirectories = true
        picker.canChooseFiles = false
        picker.allowsMultipleSelection = false
        picker.canCreateDirectories = true
        guard picker.runModal() == .OK, let path = picker.url?.path else { return }
        destinationFolder = path
        save()
        if !hasError {
            status = "Destination configured"
            detail = "Drop footage directly onto a category to move and rename it."
        }
    }

    func revealDestination() {
        guard !destinationFolder.isEmpty else {
            status = "Choose a destination in Settings first"
            hasError = true
            return
        }
        let candidate = URL(fileURLWithPath: destinationPreview, isDirectory: true)
        let parent = URL(fileURLWithPath: destinationFolder, isDirectory: true)
        let url = FileManager.default.fileExists(atPath: candidate.path) ? candidate : parent
        NSWorkspace.shared.open(url)
    }
}

private struct GlassPanel<Content: View>: View {
    let content: Content
    init(@ViewBuilder content: () -> Content) { self.content = content() }
    var body: some View {
        content
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(.white.opacity(0.19), lineWidth: 0.85)
            }
            .shadow(color: .black.opacity(0.12), radius: 14, y: 9)
    }
}

private struct DropTile: View {
    let category: String
    @ObservedObject var state: OrganizerState
    let compact: Bool
    @State private var targeted = false
    @State private var hovered = false
    @State private var confirmingRemoval = false

    private var icon: String {
        switch category.lowercased() {
        case "establishing": return "video"
        case "detail": return "photo"
        case "drone": return "airplane"
        case "interview": return "mic"
        default: return "folder"
        }
    }

    var body: some View {
        VStack(spacing: compact ? 7 : 9) {
            Image(systemName: icon)
                .font(.system(size: compact ? 23 : 27, weight: .light))
                .foregroundStyle(Palette.gold)
                .frame(height: compact ? 31 : 36)
                .accessibilityHidden(true)
            Text(category)
                .font(.system(size: compact ? 13 : 15, weight: .semibold))
                .foregroundStyle(.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(state.movedPerCategory[category, default: 0] > 0
                ? "\(state.movedPerCategory[category, default: 0]) moved"
                : "Drop files here")
                .font(.system(size: compact ? 10 : 11))
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity)
        .frame(height: compact ? 114 : 134)
        .padding(.horizontal, compact ? 6 : 10)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .strokeBorder(targeted ? Palette.gold : Palette.gold.opacity(0.39),
                              lineWidth: targeted ? 2 : 0.8)
        }
        .shadow(color: Palette.gold.opacity(targeted ? 0.18 : 0.03),
                radius: targeted ? 12 : 4, y: 3)
        .scaleEffect(targeted ? 1.014 : 1.0)
        .animation(.easeOut(duration: 0.15), value: targeted)
        .onDrop(of: [.fileURL], isTargeted: $targeted) { providers in
            state.accept(providers, category: category)
        }
        .onHover { hovered = $0 }
        .overlay(alignment: .topTrailing) {
            // Always faintly visible, stronger on hover. Removing a category never
            // touches any organized media folder, file or event manifest.
            Button {
                confirmingRemoval = true
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(.primary)
                    .frame(width: 24, height: 24)
                    .background(.regularMaterial, in: Circle())
            }
            .buttonStyle(.plain)
            .opacity(hovered ? 1 : 0.6)
            .padding(7)
            .disabled(state.busy)
            .help("Remove \(category) tile (files are kept)")
            .accessibilityLabel("Remove \(category) category")
        }
        .contextMenu {
            Button(role: .destructive) {
                confirmingRemoval = true
            } label: {
                Label("Remove Category", systemImage: "trash")
            }
        }
        .confirmationDialog("Remove \(category)?",
                            isPresented: $confirmingRemoval) {
            Button("Remove Tile", role: .destructive) {
                state.removeCategory(category)
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This removes the category tile only. Already organized files and folders are not deleted.")
        }
        .accessibilityLabel("\(category), drop files here")
    }
}

private struct AddCategoryTile: View {
    let compact: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: compact ? 7 : 11) {
                Image(systemName: "plus")
                    .font(.system(size: compact ? 17 : 20, weight: .light))
                    .frame(width: compact ? 35 : 41, height: compact ? 35 : 41)
                    .background(.thinMaterial, in: Circle())
                    .overlay { Circle().stroke(.white.opacity(0.20), lineWidth: 0.8) }
                Text("Add Category")
                    .font(.system(size: compact ? 12 : 14, weight: .medium))
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity)
            .frame(height: compact ? 114 : 134)
            .padding(.horizontal, compact ? 6 : 10)
            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 16))
            .overlay {
                RoundedRectangle(cornerRadius: 16)
                    .strokeBorder(.white.opacity(0.27),
                                  style: StrokeStyle(lineWidth: 1, dash: [5, 4]))
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Add a media category")
    }
}

private struct SettingsSheet: View {
    @ObservedObject var state: OrganizerState
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 23) {
            HStack {
                Text("Settings").font(.system(size: 23, weight: .semibold))
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            GlassPanel {
                VStack(alignment: .leading, spacing: 13) {
                    Label("Destination Folder", systemImage: "folder")
                        .font(.system(size: 14, weight: .semibold))
                    Text(state.destinationFolder.isEmpty ? "No folder selected" : state.destinationFolder)
                        .foregroundStyle(.secondary)
                        .font(.system(size: 12))
                        .textSelection(.enabled)
                    Button("Choose Folder…") { state.chooseDestination() }
                        .controlSize(.large)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(22)
            }
            Text("Files are moved, renamed, and recorded in JSON. No copies are retained in the original plate after a verified move.")
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer()
        }
        .padding(28)
        .frame(width: 470, height: 325)
        .background(.regularMaterial)
    }
}

private struct AddCategorySheet: View {
    @ObservedObject var state: OrganizerState
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var errorMessage = ""
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 19) {
            Text("New Category").font(.system(size: 22, weight: .semibold))
            Text("Create a reusable drop target.")
                .foregroundStyle(.secondary)
            TextField("e.g. Interview", text: $name)
                .textFieldStyle(.roundedBorder)
                .focused($focused)
                .onSubmit(add)
            if !errorMessage.isEmpty {
                Text(errorMessage).foregroundStyle(.red).font(.system(size: 12))
            }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                Button("Add Category", action: add)
                    .keyboardShortcut(.defaultAction)
                    .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(28)
        .frame(width: 390)
        .onAppear { focused = true }
    }

    private func add() {
        do {
            try state.addCategory(name)
            dismiss()
        } catch { errorMessage = error.localizedDescription }
    }
}

// True macOS desktop-backed frost: AppKit samples pixels BEHIND the window,
// rather than a manufactured gradient inside it. Keep the native view transparent.
private final class DesktopGlassEffectView: NSVisualEffectView {
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard let window = window else { return }
        window.isOpaque = false
        window.backgroundColor = .clear
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.hasShadow = true
    }
}

private struct DesktopBackdrop: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = DesktopGlassEffectView()
        // Apple explicitly documents this material + blending-mode combination
        // for peeking through the back of a macOS window to the desktop.
        view.material = .underWindowBackground
        view.blendingMode = .behindWindow
        view.state = .active
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {
        // System Appearance/Reduce Transparency controls material rendering.
    }
}

private struct MainView: View {
    @StateObject private var state = OrganizerState()
    @State private var settingsOpen = false
    @State private var addOpen = false

    var body: some View {
        ZStack {
            DesktopBackdrop()
                .ignoresSafeArea()
            VStack(spacing: 10) {
                header
                GlassPanel { eventForm }
                GlassPanel { destinationStrip }
                GeometryReader { geo in
                    // Adaptive columns derive from the ACTUAL resizable window width,
                    // not a hard-coded 3/4-column count. The tile heights also shrink
                    // when the usable viewport is small.
                    let compact = geo.size.width < 720 || geo.size.height < 230
                    let gap: CGFloat = compact ? 9 : 12
                    let columns = [
                        GridItem(.adaptive(
                            minimum: compact ? 128 : 155,
                            maximum: compact ? 200 : 225), spacing: gap)
                    ]
                    ScrollView {
                        LazyVGrid(columns: columns, spacing: gap) {
                            ForEach(state.categories, id: \.self) { category in
                                DropTile(category: category, state: state, compact: compact)
                            }
                            AddCategoryTile(compact: compact) { addOpen = true }
                        }
                        .padding(.vertical, 4)
                        .animation(.easeOut(duration: 0.12), value: compact)
                    }
                    .scrollIndicators(.automatic)
                }
                VStack(spacing: 3) {
                    Text("Drop files directly onto a category")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(.secondary)
                    HStack(spacing: 7) {
                        Image(systemName: "info.circle")
                            .font(.system(size: 12))
                        Text("Filename example:")
                        Text(state.filenamePreview)
                            .font(.system(size: 12, weight: .medium, design: .monospaced))
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                    }
                    .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity)
                GlassPanel { statusFooter }
            }
            .padding(.horizontal, 18)
            .padding(.top, 9)
            .padding(.bottom, 13)
        }
        .frame(minWidth: 540, minHeight: 430)
        .preferredColorScheme(.dark)
        .sheet(isPresented: $settingsOpen) { SettingsSheet(state: state) }
        .sheet(isPresented: $addOpen) { AddCategorySheet(state: state) }
        .onAppear { state.load() }
    }

    private var header: some View {
        ZStack {
            HStack(spacing: 10) {
                Image(systemName: "folder.fill")
                    .font(.system(size: 18, weight: .light))
                    .foregroundStyle(Palette.gold)
                    .frame(width: 31, height: 31)
                    .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 9))
                Text("BKE Media Organizer")
                    .font(.system(size: 17, weight: .semibold))
                    .tracking(-0.5)
            }
            HStack {
                Spacer()
                Button {
                    settingsOpen = true
                } label: {
                    Label("Settings", systemImage: "gearshape")
                        .font(.system(size: 12, weight: .medium))
                        .padding(.horizontal, 9)
                        .padding(.vertical, 6)
                }
                .buttonStyle(.plain)
                .background(.regularMaterial, in: Capsule())
                .overlay { Capsule().strokeBorder(.white.opacity(0.18)) }
            }
        }
        .frame(height: 36)
        .padding(.leading, 56) // native Mac traffic lights occupy the upper left
    }

    private var eventForm: some View {
        HStack(spacing: 11) {
            Text("Event").foregroundStyle(.secondary)
                .font(.system(size: 13, weight: .medium))
                .frame(width: 52, alignment: .leading)
            TextField("Enter event (e.g. NHLB)", text: $state.eventName)
                .textFieldStyle(.plain)
                .font(.system(size: 16, weight: .medium))
                .tint(Palette.gold)
                .padding(.horizontal, 12)
                .padding(.vertical, 9)
                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 13))
                .overlay {
                    RoundedRectangle(cornerRadius: 13)
                        .strokeBorder(.white.opacity(0.27), lineWidth: 0.7)
                }
                .accessibilityLabel("Event name")
            Button {
                state.eventName = ""
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.system(size: 15))
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("Clear event")
        }
        .padding(11)
    }

    private var destinationStrip: some View {
        HStack(spacing: 9) {
            Image(systemName: "folder")
                .font(.system(size: 15, weight: .light))
                .foregroundStyle(Palette.gold)
            Text("Destination:")
                .foregroundStyle(.secondary)
            Text(state.destinationPreview)
                .lineLimit(1)
                .truncationMode(.middle)
                .fontWeight(.medium)
                .help(state.destinationPreview)
            Spacer(minLength: 8)
            Rectangle().fill(.white.opacity(0.20)).frame(width: 1, height: 20)
            Image(systemName: "square.stack.3d.up")
                .foregroundStyle(Palette.gold)
            Text("Move + Rename").fontWeight(.medium)
        }
        .font(.system(size: 12))
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
    }

    private var statusFooter: some View {
        HStack(spacing: 10) {
            Image(systemName: state.hasError ? "exclamationmark.triangle" : "checkmark.circle")
                .font(.system(size: 22, weight: .ultraLight))
                .foregroundStyle(Palette.gold)
                .frame(width: 32, height: 32)
            VStack(alignment: .leading, spacing: 5) {
                Text(state.status)
                    .font(.system(size: 13, weight: .semibold))
                    .lineLimit(1)
                Text(state.detail)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Spacer(minLength: 6)
            Button {
                state.revealDestination()
            } label: {
                Label("Reveal in Finder", systemImage: "folder")
                    .font(.system(size: 11, weight: .medium))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 7)
            }
            .buttonStyle(.plain)
            .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 11))
            .overlay { RoundedRectangle(cornerRadius: 11).strokeBorder(.white.opacity(0.17)) }
        }
        .padding(.horizontal, 13)
        .padding(.vertical, 10)
    }
}

@main
struct BKEMediaOrganizerApp: App {
    var body: some Scene {
        WindowGroup("BKE Media Organizer") {
            MainView()
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 890, height: 605)
    }
}
