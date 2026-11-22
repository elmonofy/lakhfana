import AppKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var menuBar: MenuBarController?
    private var onboarding: OnboardingWindowController?
    private var settingsWindow: SettingsWindowController?
    private var cancellables = Set<AnyCancellable>()

    private var settings: AppSettings { return AppSettings.shared }

    // MARK: Launch

    func applicationDidFinishLaunching(_ notification: Notification) {
        let bar = MenuBarController()
        bar.onOpenSettings = { [weak self] in self?.showSettings() }
        bar.update(paused: settings.paused, hotkey: settings.hotkey)
        menuBar = bar
        Toast.shared.anchorProvider = { [weak bar] in bar?.anchorRect() }

        HotkeyCenter.shared.onPressed = {
            SwitchEngine.shared.trigger()
        }
        SwitchEngine.shared.onNotify = { [weak self] message in
            self?.engineSaid(message)
        }

        let ob = OnboardingWindowController()
        ob.onFinished = { [weak self] trusted in self?.onboardingFinished(trusted: trusted) }
        onboarding = ob
        settingsWindow = SettingsWindowController()

        let hotkeyOK = registerHotkey()
        observeSettings()
        // First launch registers the login item once (if wanted); later launches never re-add a removed one.
        LoginItem.sync(wanted: settings.launchAtLogin)

        if !settings.firstRunDone {
            ob.show()
            return
        }

        if !hotkeyOK {
            Toast.shared.show(hotkeyTakenMessage(settings.hotkey))
            showSettings()
        } else if !Permissions.isTrusted {
            Toast.shared.show("لخفنه محتاج صلاحية Accessibility عشان يقدر يحوّل الكلام. فعّلها من الإعدادات.")
            showSettings()
        }
    }

    /// Opening the app again (Finder / Spotlight) while it runs → settings.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return false
    }

    func applicationWillTerminate(_ notification: Notification) {
        HotkeyCenter.shared.unregister()
    }

    // MARK: Windows

    private func showSettings() {
        if let ob = onboarding, ob.isVisible {
            ob.show()
            return
        }
        if settingsWindow == nil { settingsWindow = SettingsWindowController() }
        settingsWindow?.show()
    }

    /// The permission step lives in the onboarding window (first run) and in settings.
    private func showPermissionStep() {
        showSettings()
    }

    /// End of first run ("يلا نبدأ", "بعدين" or the close button). The onboarding window already saved the
    /// settings and re-registered the hotkey.
    private func onboardingFinished(trusted: Bool) {
        LoginItem.setEnabled(settings.launchAtLogin)
        SwitchEngine.shared.reloadLayouts()
        let hk = settings.hotkey
        if !settings.paused && !HotkeyCenter.shared.isRegistered {
            Toast.shared.show(hotkeyTakenMessage(hk))
        } else if trusted {
            Toast.shared.show("جاهزين! حدد الكلام ودوس " + hk.isolatedDisplayString)
        } else {
            Toast.shared.show("تمام. من غير صلاحية Accessibility مش هقدر أحوّل، فعّلها من الإعدادات وقت ما تحب.")
        }
    }

    // MARK: Engine

    private func engineSaid(_ message: String) {
        Toast.shared.show(message)
        if !Permissions.isTrusted {
            showPermissionStep()
        }
    }

    // MARK: Hotkey

    /// Registers the saved hotkey unless paused. Returns false when another app owns it.
    @discardableResult
    private func registerHotkey() -> Bool {
        if settings.paused {
            HotkeyCenter.shared.unregister()
            return true
        }
        let hk = settings.hotkey
        if hk.isEmpty {
            HotkeyCenter.shared.unregister()
            return false
        }
        return HotkeyCenter.shared.register(hk)
    }

    private func hotkeyTakenMessage(_ hk: Hotkey) -> String {
        return "الاختصار " + hk.isolatedDisplayString + " مستخدم في برنامج تاني. اختار غيره من الإعدادات."
    }

    // MARK: Settings observation

    private func observeSettings() {
        // @Published emits in willSet: hop to the next main-loop turn so AppSettings already holds the new values.
        settings.$hotkey
            .dropFirst()
            .removeDuplicates()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.hotkeyChanged() }
            .store(in: &cancellables)

        settings.$paused
            .dropFirst()
            .removeDuplicates()
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.pausedChanged() }
            .store(in: &cancellables)

        settings.$layoutA
            .dropFirst()
            .map { _ in () }
            .merge(with: settings.$layoutB.dropFirst().map { _ in () })
            .debounce(for: .milliseconds(100), scheduler: DispatchQueue.main)
            .sink { _ in SwitchEngine.shared.reloadLayouts() }
            .store(in: &cancellables)
        // launchAtLogin is applied by the windows that save it (every settings save, end of onboarding),
        // so an unchanged value still re-applies and the window can show a hint when macOS refuses.
    }

    private func hotkeyChanged() {
        let hk = settings.hotkey
        menuBar?.update(paused: settings.paused, hotkey: hk)
        if settings.paused { return }
        // A window that saved the change may have registered it already (HotkeyRefresh).
        let center = HotkeyCenter.shared
        if center.isRegistered, let current = center.current, current == hk { return }
        if !registerHotkey() {
            Toast.shared.show(hotkeyTakenMessage(hk))
        }
    }

    private func pausedChanged() {
        let hk = settings.hotkey
        menuBar?.update(paused: settings.paused, hotkey: hk)
        if settings.paused {
            HotkeyCenter.shared.unregister()
            Toast.shared.show("وقفنا مؤقتاً. دوس «تشغيل» من القائمة لما تحتاجنا.")
        } else if registerHotkey() {
            Toast.shared.show("رجعنا تاني! " + hk.isolatedDisplayString)
        } else {
            Toast.shared.show("رجعنا، بس الاختصار " + hk.isolatedDisplayString + " مستخدم في برنامج تاني. اختار غيره من الإعدادات.")
        }
    }
}
