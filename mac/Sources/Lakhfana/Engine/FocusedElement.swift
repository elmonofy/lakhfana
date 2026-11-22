import AppKit
import ApplicationServices

/// What has keyboard focus right now, read through the Accessibility API (we already hold that permission).
struct FocusedElement {
    enum Kind {
        case editableText   // a text field / text area / editable web content
        case secureText     // a password field
        case other          // something that is clearly not text (a web page, a list, a terminal…)
        case unknown        // the app exposes nothing useful
    }

    let element: AXUIElement?
    let kind: Kind

    private static let textRoles: Set<String> = ["AXTextField", "AXTextArea", "AXComboBox", "AXSearchField"]

    /// Browsers and terminals bind ⌘← to Back / tab switching, so the line fallback is only safe there when
    /// the focus is known to be text.
    private static let riskyBundlePrefixes = [
        "com.google.Chrome", "com.apple.Safari", "org.mozilla.firefox", "com.microsoft.edgemac", "com.brave.Browser",
        "company.thebrowser.Browser", "com.operasoftware.Opera", "com.vivaldi.Vivaldi", "app.zen-browser",
        "com.apple.Terminal", "com.googlecode.iterm2", "dev.warp.Warp", "com.mitchellh.ghostty", "net.kovidgoyal.kitty",
        "io.alacritty", "com.github.wez.wezterm",
    ]

    static func isRiskyForLineSelect(_ app: NSRunningApplication?) -> Bool {
        guard let id = app?.bundleIdentifier else { return false }
        return riskyBundlePrefixes.contains(where: { id.hasPrefix($0) })
    }

    static func current(in app: NSRunningApplication?) -> FocusedElement {
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, 0.3)

        // Electron apps only expose their text fields after being asked to (harmless for everyone else).
        if let pid = app?.processIdentifier {
            let appElement = AXUIElementCreateApplication(pid)
            AXUIElementSetMessagingTimeout(appElement, 0.3)
            _ = AXUIElementSetAttributeValue(appElement, "AXManualAccessibility" as CFString, kCFBooleanTrue)
        }

        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &value) == .success,
              let v = value, CFGetTypeID(v) == AXUIElementGetTypeID() else {
            return FocusedElement(element: nil, kind: .unknown)
        }
        let element = v as! AXUIElement
        AXUIElementSetMessagingTimeout(element, 0.3)

        let role = string(element, kAXRoleAttribute as String) ?? ""
        let subrole = string(element, kAXSubroleAttribute as String) ?? ""
        if subrole == (kAXSecureTextFieldSubrole as String) || role == "AXSecureTextField" {
            return FocusedElement(element: element, kind: .secureText)
        }
        if textRoles.contains(role) {
            return FocusedElement(element: element, kind: .editableText)
        }
        // Editable web content / custom editors expose a settable selection range or AXEditable.
        var settable = DarwinBoolean(false)
        if AXUIElementIsAttributeSettable(element, kAXSelectedTextRangeAttribute as CFString, &settable) == .success,
           settable.boolValue {
            return FocusedElement(element: element, kind: .editableText)
        }
        if let editable = bool(element, "AXEditable"), editable {
            return FocusedElement(element: element, kind: .editableText)
        }
        if role.isEmpty { return FocusedElement(element: element, kind: .unknown) }
        return FocusedElement(element: element, kind: .other)
    }

    /// The caret / selection range of the focused text, if the app reports it.
    func selectedRange() -> CFRange? {
        guard let element = element else { return nil }
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &value) == .success,
              let v = value, CFGetTypeID(v) == AXValueGetTypeID() else { return nil }
        var range = CFRange(location: 0, length: 0)
        if AXValueGetValue(v as! AXValue, .cfRange, &range) { return range }
        return nil
    }

    private static func string(_ element: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private static func bool(_ element: AXUIElement, _ attribute: String) -> Bool? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        if let b = value as? Bool { return b }
        if let n = value as? NSNumber { return n.boolValue }
        return nil
    }
}
