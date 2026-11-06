import SwiftUI
import AppKit
import UniformTypeIdentifiers
import Foundation

// Native macOS presentation. The transfer engine remains the verified .NET
// MediaOrganizer and is invoked by a small JSON bridge bundled inside the app.
private enum Palette {
    static let gold = Color(red: 0.98, green: 0.81, blue: 0.51)
    static let navy = Color(red: 0.08, green: 0.12, blue: 0.22)
    static let cream = Color(red: 0.98, green: 0.98, blue: 1.0)
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
private final class DroppedURLs {
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
            categories = settings.categories.isEmpty ? ["Establishing", "Detail", "Drone"] : settings.categories
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

    func save() {
        do {
            let settings = StoredSettings(destinationFolder: destinationFolder, categories: categories)
            let parent = settingsURL.deletingLastPathComponent()
            try FileManager.default.createDirectory(at: parent, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(settings).write(to: settingsURL, options: .atomic)
        } catch {
            status = "Settings could not be saved"
            detail = error.localizedDescription
            hasError = true
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
        save()
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
    @State private var targeted = false

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
        VStack(spacing: 13) {
            Image(systemName: icon)
                .font(.system(size: 31, weight: .ultraLight))
                .foregroundStyle(Palette.gold)
                .frame(height: 43)
                .accessibilityHidden(true)
            Text(category)
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.75)
            Text(state.movedPerCategory[category, default: 0] > 0
                ? "\(state.movedPerCategory[category, default: 0]) moved · Drop more"
                : "Drop files here")
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .frame(height: 180)
        .padding(.horizontal, 12)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 21, style: .continuous))
        .background {
            RoundedRectangle(cornerRadius: 21)
                .fill(Palette.gold.opacity(targeted ? 0.23 : 0.02))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 21, style: .continuous)
                .strokeBorder(targeted ? Palette.gold : Palette.gold.opacity(0.47),
                              lineWidth: targeted ? 2.5 : 0.9)
        }
        .shadow(color: Palette.gold.opacity(targeted ? 0.22 : 0.06),
                radius: targeted ? 20 : 7, y: 6)
        .scaleEffect(targeted ? 1.018 : 1.0)
        .animation(.easeOut(duration: 0.16), value: targeted)
        .onDrop(of: [.fileURL], isTargeted: $targeted) { providers in
            state.accept(providers, category: category)
        }
        .accessibilityLabel("\(category), drop files here")
    }
}

private struct AddCategoryTile: View {
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            VStack(spacing: 14) {
                Image(systemName: "plus")
                    .font(.system(size: 22, weight: .light))
                    .frame(width: 49, height: 49)
                    .background(.thinMaterial, in: Circle())
                    .overlay { Circle().stroke(.white.opacity(0.26), lineWidth: 0.8) }
                Text("Add Category")
                    .font(.system(size: 15, weight: .medium))
            }
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity)
            .frame(height: 180)
            .padding(.horizontal, 12)
            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 21))
            .overlay {
                RoundedRectangle(cornerRadius: 21)
                    .strokeBorder(.white.opacity(0.31), style: StrokeStyle(lineWidth: 1, dash: [6, 5]))
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
        .background(Palette.navy.opacity(0.75))
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

private struct AmbientBackdrop: View {
    var body: some View {
        GeometryReader { proxy in
            ZStack {
                LinearGradient(
                    colors: [
                        Color(red: 0.25, green: 0.34, blue: 0.54),
                        Color(red: 0.13, green: 0.19, blue: 0.35),
                        Color(red: 0.18, green: 0.20, blue: 0.35)
                    ],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                Ellipse()
                    .fill(Color(red: 0.95, green: 0.69, blue: 0.48).opacity(0.43))
                    .frame(width: 510, height: 400)
                    .blur(radius: 110)
                    .offset(x: proxy.size.width * 0.46, y: -proxy.size.height * 0.42)
                Ellipse()
                    .fill(Color(red: 0.48, green: 0.57, blue: 0.96).opacity(0.40))
                    .frame(width: 530, height: 450)
                    .blur(radius: 125)
                    .offset(x: -proxy.size.width * 0.47, y: proxy.size.height * 0.36)
            }
            .ignoresSafeArea()
        }
    }
}

private struct MainView: View {
    @StateObject private var state = OrganizerState()
    @State private var settingsOpen = false
    @State private var addOpen = false

    var body: some View {
        ZStack {
            AmbientBackdrop()
            VStack(spacing: 18) {
                header
                GlassPanel { eventForm }
                GlassPanel { destinationStrip }
                GeometryReader { geo in
                    let count = geo.size.width > 905 ? 4 : (geo.size.width > 650 ? 3 : 2)
                    let columns = Array(repeating: GridItem(.flexible(minimum: 150), spacing: 15), count: count)
                    ScrollView {
                        LazyVGrid(columns: columns, spacing: 15) {
                            ForEach(state.categories, id: \.self) { category in
                                DropTile(category: category, state: state)
                            }
                            AddCategoryTile { addOpen = true }
                        }
                        .padding(.vertical, 9)
                    }
                    .scrollIndicators(.automatic)
                }
                VStack(spacing: 7) {
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
            .padding(.horizontal, 31)
            .padding(.top, 15)
            .padding(.bottom, 23)
        }
        .frame(minWidth: 810, minHeight: 650)
        .preferredColorScheme(.dark)
        .sheet(isPresented: $settingsOpen) { SettingsSheet(state: state) }
        .sheet(isPresented: $addOpen) { AddCategorySheet(state: state) }
        .onAppear { state.load() }
    }

    private var header: some View {
        ZStack {
            HStack(spacing: 10) {
                Image(systemName: "folder.fill")
                    .font(.system(size: 23, weight: .light))
                    .foregroundStyle(Palette.gold)
                    .frame(width: 39, height: 39)
                    .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 11))
                Text("BKE Media Organizer")
                    .font(.system(size: 21, weight: .semibold))
                    .tracking(-0.5)
            }
            HStack {
                Spacer()
                Button {
                    settingsOpen = true
                } label: {
                    Label("Settings", systemImage: "gearshape")
                        .font(.system(size: 13, weight: .medium))
                        .padding(.horizontal, 11)
                        .padding(.vertical, 8)
                }
                .buttonStyle(.plain)
                .background(.regularMaterial, in: Capsule())
                .overlay { Capsule().strokeBorder(.white.opacity(0.18)) }
            }
        }
        .frame(height: 48)
        .padding(.leading, 68) // native Mac traffic lights occupy the upper left
    }

    private var eventForm: some View {
        HStack(spacing: 17) {
            Text("Event").foregroundStyle(.secondary)
                .font(.system(size: 15, weight: .medium))
                .frame(width: 67, alignment: .leading)
            TextField("Enter event (e.g. NHLB)", text: $state.eventName)
                .textFieldStyle(.plain)
                .font(.system(size: 20, weight: .medium))
                .tint(Palette.gold)
                .padding(.horizontal, 17)
                .padding(.vertical, 14)
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
                    .font(.system(size: 18))
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("Clear event")
        }
        .padding(18)
    }

    private var destinationStrip: some View {
        HStack(spacing: 13) {
            Image(systemName: "folder")
                .font(.system(size: 18, weight: .light))
                .foregroundStyle(Palette.gold)
            Text("Destination:")
                .foregroundStyle(.secondary)
            Text(state.destinationPreview)
                .lineLimit(1)
                .truncationMode(.middle)
                .fontWeight(.medium)
                .help(state.destinationPreview)
            Spacer(minLength: 14)
            Rectangle().fill(.white.opacity(0.20)).frame(width: 1, height: 25)
            Image(systemName: "square.stack.3d.up")
                .foregroundStyle(Palette.gold)
            Text("Move + Rename").fontWeight(.medium)
        }
        .font(.system(size: 13))
        .padding(.horizontal, 20)
        .padding(.vertical, 18)
    }

    private var statusFooter: some View {
        HStack(spacing: 15) {
            Image(systemName: state.hasError ? "exclamationmark.triangle" : "checkmark.circle")
                .font(.system(size: 27, weight: .ultraLight))
                .foregroundStyle(Palette.gold)
                .frame(width: 43, height: 43)
            VStack(alignment: .leading, spacing: 5) {
                Text(state.status)
                    .font(.system(size: 15, weight: .semibold))
                    .lineLimit(1)
                Text(state.detail)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Spacer(minLength: 10)
            Button {
                state.revealDestination()
            } label: {
                Label("Reveal in Finder", systemImage: "folder")
                    .font(.system(size: 12, weight: .medium))
                    .padding(.horizontal, 13)
                    .padding(.vertical, 10)
            }
            .buttonStyle(.plain)
            .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 11))
            .overlay { RoundedRectangle(cornerRadius: 11).strokeBorder(.white.opacity(0.17)) }
        }
        .padding(.horizontal, 19)
        .padding(.vertical, 14)
    }
}

@main
struct BKEMediaOrganizerApp: App {
    var body: some Scene {
        WindowGroup("BKE Media Organizer") {
            MainView()
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1120, height: 745)
    }
}
