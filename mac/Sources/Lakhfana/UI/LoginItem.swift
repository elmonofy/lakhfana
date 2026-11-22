import Foundation
import ServiceManagement

/// "يشتغل لما الماك يفتح" through SMAppService (macOS 13+). Failures are logged and reported as an Arabic hint
/// (e.g. running the bare executable outside an .app bundle, or macOS waiting for the user's approval).
enum LoginItem {
    static let approvalMessage = "لازم توافق من إعدادات النظام (Login Items) عشان لخفنه يشتغل لما الماك يفتح."
    static let failedMessage = "مقدرناش نضيف لخفنه لوحدنا. ضيفه بنفسك من إعدادات النظام (Login Items)."

    /// Set after the first successful registration, so `sync` never re-adds an item the user removed later.
    private static let registeredOnceKey = "loginItemRegisteredOnce"

    private static var status: SMAppService.Status {
        return SMAppService.mainApp.status
    }

    /// What the settings toggle shows: on when registered, including "waiting for the user's approval".
    static var isOn: Bool {
        let s = status
        return s == .enabled || s == .requiresApproval
    }

    /// True when macOS will really start the app at login.
    static var isApproved: Bool {
        return status == .enabled
    }

    /// Hint for the current state, or nil when nothing needs the user's attention.
    static var currentHint: String? {
        return status == .requiresApproval ? approvalMessage : nil
    }

    /// Applies the user's choice (every settings save and the end of onboarding).
    /// Returns an Arabic hint when the item could not be enabled or still needs approval, otherwise nil.
    @discardableResult
    static func setEnabled(_ on: Bool) -> String? {
        let service = SMAppService.mainApp
        if on {
            let before = service.status
            if before == .enabled {
                markRegisteredOnce()
                return nil
            }
            if before == .requiresApproval {
                markRegisteredOnce()
                return approvalMessage
            }
            do {
                try service.register()
            } catch {
                NSLog("Lakhfana: login item register failed: \(error)")
                if service.status == .requiresApproval {
                    markRegisteredOnce()
                    return approvalMessage
                }
                return failedMessage
            }
            markRegisteredOnce()
            return service.status == .requiresApproval ? approvalMessage : nil
        }

        let s = service.status
        if s == .enabled || s == .requiresApproval {
            do {
                try service.unregister()
            } catch {
                NSLog("Lakhfana: login item unregister failed: \(error)")
            }
        }
        return nil
    }

    /// At launch: registers only the very first time (when wanted). After that the user's choice in
    /// System Settings wins; we never re-add an item they removed there.
    static func sync(wanted: Bool) {
        if UserDefaults.standard.bool(forKey: registeredOnceKey) { return }
        if isOn {
            markRegisteredOnce()
            return
        }
        if wanted {
            setEnabled(true)
        }
    }

    /// System Settings → General → Login Items.
    static func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }

    private static func markRegisteredOnce() {
        UserDefaults.standard.set(true, forKey: registeredOnceKey)
    }
}
