import AppKit
import SwiftUI

/// Settings: same controls as onboarding + Accessibility status + version + quit.
struct SettingsView: View {
    @ObservedObject var model: PreferencesModel
    @ObservedObject var permission: PermissionWatcher
    let icon: NSImage
    let version: String
    let onSave: () -> Void
    let onCancel: () -> Void
    let onQuit: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
            header
            PreferencesView(model: model)
            AccessibilityCard(watcher: permission, isOnboarding: false)
            footer
        }
        .padding(Theme.windowPadding)
        .frame(width: Theme.windowWidth)
        .fixedSize(horizontal: false, vertical: true)
        .background(Theme.windowBackground)
        .environment(\.layoutDirection, .rightToLeft)
    }

    private var header: some View {
        HStack(alignment: .center, spacing: 14) {
            Image(nsImage: icon)
                .resizable()
                .interpolation(.high)
                .frame(width: 48, height: 48)
            VStack(alignment: .leading, spacing: 2) {
                Text("الإعدادات")
                    .font(Theme.titleFont)
                Text("لخفنه · الإصدار " + version)
                    .font(Theme.smallFont)
                    .foregroundColor(Color.secondary)
            }
            Spacer(minLength: 0)
        }
    }

    private var footer: some View {
        HStack(spacing: 10) {
            Button(action: onQuit) {
                Text("خروج من لخفنه")
                    .font(Theme.smallFont)
                    .foregroundColor(Color.secondary)
                    .underline()
            }
            .buttonStyle(.plain)
            Spacer(minLength: 8)
            if let e = model.formError {
                Text(e)
                    .font(Theme.smallFont)
                    .foregroundColor(Theme.danger)
                    .lineLimit(2)
            }
            Button("إلغاء") { onCancel() }
                .controlSize(.large)
                .keyboardShortcut(.cancelAction)
            Button("حفظ") { onSave() }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
        }
    }
}

@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var model: PreferencesModel?
    private let permission: PermissionWatcher

    override init() {
        permission = PermissionWatcher()
        super.init()
    }

    var isVisible: Bool {
        return window?.isVisible ?? false
    }

    /// Opens the window (or brings it to the front). A fresh draft each time it opens, so "إلغاء" discards edits.
    func show() {
        if let w = window, w.isVisible {
            model?.refreshLayouts()
            permission.refresh()
            WindowFactory.present(w)
            return
        }
        let m = PreferencesModel(settings: AppSettings.shared)
        model = m
        let root = SettingsView(model: m,
                                permission: permission,
                                icon: WindowFactory.appIcon,
                                version: WindowFactory.appVersion,
                                onSave: { [weak self] in self?.save() },
                                onCancel: { [weak self] in self?.window?.close() },
                                onQuit: { NSApp.terminate(nil) })
        let w = window ?? WindowFactory.makeWindow(title: "إعدادات لخفنه")
        w.delegate = self
        window = w
        WindowFactory.setContent(root, of: w)
        permission.start()
        WindowFactory.present(w)
    }

    private func save() {
        guard let m = model else { return }
        guard m.validate() else { return }
        m.write(to: AppSettings.shared)
        window?.close()
    }

    // MARK: NSWindowDelegate

    func windowDidBecomeKey(_ notification: Notification) {
        model?.refreshLayouts()
        permission.refresh()
    }

    func windowDidResignKey(_ notification: Notification) {
        model?.stopRecording()
    }

    func windowWillClose(_ notification: Notification) {
        model?.stopRecording()
        permission.stop()
    }
}
