import AppKit
import SwiftUI

/// First-run window: welcome + live example, shortcut, languages, options, then the Accessibility step.
struct OnboardingView: View {
    @ObservedObject var model: PreferencesModel
    @ObservedObject var permission: PermissionWatcher
    let icon: NSImage
    let onStart: () -> Void
    let onLater: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
            header
            PreferencesView(model: model)
            AccessibilityCard(watcher: permission, isOnboarding: true)
            footer
        }
        .padding(Theme.windowPadding)
        .frame(width: Theme.windowWidth)
        .fixedSize(horizontal: false, vertical: true)
        .background(Theme.windowBackground)
        .environment(\.layoutDirection, .rightToLeft)
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 16) {
            Image(nsImage: icon)
                .resizable()
                .interpolation(.high)
                .frame(width: 64, height: 64)
            VStack(alignment: .leading, spacing: 6) {
                Text("أهلاً بيك في لخفنه")
                    .font(Theme.titleFont)
                Text("كتبت واللغة غلط؟ حدد الكلام ودوس الاختصار، وهو يقلبه للغة الصح.")
                    .font(Theme.bodyFont)
                    .foregroundColor(Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 6) {
                    Text("لو كتبت")
                        .font(Theme.smallFont)
                        .foregroundColor(Color.secondary)
                    Chip(text: "hgsghl ugd;l", style: .muted)
                    Text("←")
                        .foregroundColor(Color.secondary)
                    Chip(text: "السلام عليكم", style: .accent)
                }
                .padding(.top, 4)
            }
            Spacer(minLength: 0)
        }
    }

    private var footer: some View {
        HStack(spacing: 10) {
            Text(footnote)
                .font(Theme.smallFont)
                .foregroundColor(model.formError == nil ? Color.secondary : Theme.danger)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            Button("بعدين") { onLater() }
                .controlSize(.large)
            Button("يلا نبدأ") { onStart() }
                .buttonStyle(.borderedProminent)
                .tint(Theme.accent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
                .disabled(!permission.trusted)
        }
    }

    private var footnote: String {
        if let e = model.formError { return e }
        if permission.trusted { return "هتلاقيه مستنيك فوق جنب الساعة" }
        return "فعّل الصلاحية الأول، أو دوس «بعدين» وفعّلها وقت ما تحب"
    }
}

@MainActor
final class OnboardingWindowController: NSObject, NSWindowDelegate {
    /// Called after "يلا نبدأ" / "بعدين" saved the settings (firstRunDone = true). Bool = Accessibility granted.
    var onFinished: ((Bool) -> Void)?

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

    func show() {
        if let w = window, w.isVisible {
            model?.refreshLayouts()
            permission.refresh()
            WindowFactory.present(w)
            return
        }
        let m = PreferencesModel(settings: AppSettings.shared)
        model = m
        let root = OnboardingView(model: m,
                                  permission: permission,
                                  icon: WindowFactory.appIcon,
                                  onStart: { [weak self] in self?.finish() },
                                  onLater: { [weak self] in self?.finish() })
        let w = window ?? WindowFactory.makeWindow(title: "لخفنه")
        w.delegate = self
        window = w
        WindowFactory.setContent(root, of: w)
        permission.start()
        WindowFactory.present(w)
    }

    private func finish() {
        guard let m = model else { return }
        guard m.validate() else { return }
        let settings = AppSettings.shared
        m.write(to: settings)
        settings.firstRunDone = true
        let trusted = Permissions.isTrusted
        window?.close()
        onFinished?(trusted)
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
