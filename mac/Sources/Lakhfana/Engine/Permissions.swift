import AppKit
import ApplicationServices

/// Accessibility permission: required to post ⌘C / ⌘V to other apps.
enum Permissions {
    static var isTrusted: Bool {
        return AXIsProcessTrusted()
    }

    /// Shows the system prompt (once per app identity) that adds us to the Accessibility list.
    static func promptIfNeeded() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    static func openAccessibilitySettings() {
        let urls = [
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
        ]
        for s in urls {
            if let url = URL(string: s), NSWorkspace.shared.open(url) { return }
        }
    }
}
