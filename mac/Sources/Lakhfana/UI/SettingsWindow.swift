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
    /// nil: everything fits on screen. Otherwise the middle scrolls at this height (see WindowFactory.setContent).
    var scrollHeight: CGFloat? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
            header
            ScrollableMiddle(height: scrollHeight) {
                VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
                    PreferencesView(model: model)
                    AccessibilityCard(watcher: permission, isOnboarding: false)
                }
            }
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
        m.loadLoginItemState()
        model = m
        let watcher = permission
        let icon = WindowFactory.appIcon
        let version = WindowFactory.appVersion
        let w = window ?? WindowFactory.makeWindow(title: "إعدادات لخفنه")
        w.delegate = self
        window = w
        WindowFactory.setContent(of: w) { (scrollHeight: CGFloat?) -> SettingsView in
            return SettingsView(model: m,
                                permission: watcher,
                                icon: icon,
                                version: version,
                                onSave: { [weak self] in self?.save() },
                                onCancel: { [weak self] in self?.window?.close() },
                                onQuit: { NSApp.terminate(nil) },
                                scrollHeight: scrollHeight)
        }
        permission.start()
        WindowFactory.present(w)
    }

    /// Writes the draft, then applies it right away: hotkey (re)registration and launch at login, even when the
    /// values did not change (e.g. the shortcut was taken at launch, or the login item was removed meanwhile).
    /// Stays open when something needs the user's attention.
    private func save() {
        guard let m = model else { return }
        guard m.validate() else { return }
        m.write(to: AppSettings.shared)

        var keepOpen = false
        if !HotkeyRefresh.applySaved() {
            m.recorder.refreshStatus()
            m.formError = HotkeyRecorderModel.takenMessage + "، اختار غيره"
            keepOpen = true
        }

        let hadHint = m.loginItemHint != nil
        let hint = LoginItem.setEnabled(m.launchAtLogin)
        m.loginItemHint = hint
        if hint != nil && !hadHint {
            keepOpen = true
        }

        if !keepOpen {
            window?.close()
        }
    }

    // MARK: NSWindowDelegate

    func windowDidBecomeKey(_ notification: Notification) {
        model?.refreshLayouts()
        model?.refreshLoginItemHint()
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
