import Foundation
import ServiceManagement

/// "يشتغل لما الماك يفتح" through SMAppService (macOS 13+). Failures are logged and otherwise ignored
/// (e.g. running the bare executable outside an .app bundle).
enum LoginItem {
    static var isEnabled: Bool {
        return SMAppService.mainApp.status == .enabled
    }

    /// Applies the user's choice right away (called when the toggle changes or onboarding finishes).
    static func setEnabled(_ on: Bool) {
        let service = SMAppService.mainApp
        do {
            if on {
                if service.status != .enabled { try service.register() }
            } else {
                if service.status == .enabled || service.status == .requiresApproval { try service.unregister() }
            }
        } catch {
            let action: String = on ? "register" : "unregister"
            NSLog("Lakhfana: login item \(action) failed: \(error)")
        }
    }

    /// At launch: register once if wanted and never registered; don't fight a choice made in System Settings.
    static func sync(wanted: Bool) {
        let status = SMAppService.mainApp.status
        if wanted && status == .notRegistered {
            setEnabled(true)
        } else if !wanted && status == .enabled {
            setEnabled(false)
        }
    }
}
