import AppKit
import SwiftUI

/// First-run window: welcome + live example, shortcut, languages, options, then the Accessibility step.
struct OnboardingView: View {
    @ObservedObject var model: PreferencesModel
    @ObservedObject var permission: PermissionWatcher
    let icon: NSImage
    let onStart: () -> Void
    let onLater: () -> Void
    /// nil: everything fits on screen. Otherwise the middle scrolls at this height (see WindowFactory.setContent).
    var scrollHeight: CGFloat? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
            header
            ScrollableMiddle(height: scrollHeight) {
                VStack(alignment: .leading, spacing: Theme.sectionSpacing) {
                    PreferencesView(model: model)
                    AccessibilityCard(watcher: permission, isOnboarding: true)
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
    /// Called once first run is finished (firstRunDone = true): after "يلا نبدأ", "بعدين" or the close button.
    /// Bool = Accessibility granted.
    var onFinished: ((Bool) -> Void)?

    private var window: NSWindow?
    private var model: PreferencesModel?
    private let permission: PermissionWatcher
    /// True once this window's draft was saved; guards the close-button path against running twice.
    private var finished = false

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
        finished = false
        let watcher = permission
        let icon = WindowFactory.appIcon
        let w = window ?? WindowFactory.makeWindow(title: "لخفنه")
        w.delegate = self
        window = w
        WindowFactory.setContent(of: w) { (scrollHeight: CGFloat?) -> OnboardingView in
            return OnboardingView(model: m,
                                  permission: watcher,
                                  icon: icon,
                                  onStart: { [weak self] in self?.start() },
                                  onLater: { [weak self] in self?.later() },
                                  scrollHeight: scrollHeight)
        }
        permission.start()
        WindowFactory.present(w)
    }

    /// "يلا نبدأ": full validation; stays open with inline errors until everything is right.
    private func start() {
        guard let m = model, !finished else { return }
        guard m.validate() else { return }
        m.write(to: AppSettings.shared)
        complete(closeWindow: true)
    }

    /// "بعدين": never blocks. Saves what validates, skips the rest.
    private func later() {
        guard let m = model, !finished else { return }
        m.writeValidParts(to: AppSettings.shared)
        complete(closeWindow: true)
    }

    /// Common end of first run: firstRunDone, hotkey re-registration, close, then onFinished (launch at login…).
    private func complete(closeWindow: Bool) {
        finished = true
        AppSettings.shared.firstRunDone = true
        HotkeyRefresh.applySaved()
        let trusted = Permissions.isTrusted
        if closeWindow {
            window?.close()
        }
        onFinished?(trusted)
    }

    /// The red close button behaves like "بعدين".
    private func finishFromClose() {
        guard let m = model, !finished else { return }
        m.writeValidParts(to: AppSettings.shared)
        complete(closeWindow: false)
    }

    // MARK: NSWindowDelegate

    func windowDidBecomeKey(_ notification: Notification) {
        model?.refreshLayouts()
        permission.refresh()
    }

    func windowDidResignKey(_ notification: Notification) {
        model?.stopRecording()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        finishFromClose()
        return true
    }

    func windowWillClose(_ notification: Notification) {
        model?.stopRecording()
        permission.stop()
        // Also covers close() calls that skip windowShouldClose.
        finishFromClose()
    }
}
