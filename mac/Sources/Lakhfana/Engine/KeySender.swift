import AppKit
import Carbon

/// Synthesized editing shortcuts sent to the focused app with CGEvent (needs Accessibility permission).
enum KeySender {
    /// Marks our own events (eventSourceUserData).
    private static let marker: Int64 = 0x4C4B4846

    /// Private state: the user's still-held keys don't leak into our events.
    private static let source: CGEventSource? = CGEventSource(stateID: .privateState)

    private static let modifierMask: CGEventFlags = [.maskCommand, .maskControl, .maskAlternate, .maskShift]

    private static func tap(_ key: Int, _ flags: CGEventFlags) {
        let code = CGKeyCode(key)
        guard let down = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: false) else { return }
        down.flags = flags
        up.flags = flags
        down.setIntegerValueField(.eventSourceUserData, value: marker)
        up.setIntegerValueField(.eventSourceUserData, value: marker)
        down.post(tap: .cghidEventTap)
        usleep(8_000)
        up.post(tap: .cghidEventTap)
        usleep(8_000)
    }

    static func copy() {
        tap(kVK_ANSI_C, .maskCommand)
    }

    static func paste() {
        tap(kVK_ANSI_V, .maskCommand)
    }

    /// ⌘← then ⌘⇧→: selects the line the caret is on.
    static func selectCurrentLine() {
        let arrowFlags: CGEventFlags = [.maskNumericPad, .maskSecondaryFn]
        tap(kVK_LeftArrow, arrowFlags.union(.maskCommand))
        usleep(15_000)
        tap(kVK_RightArrow, arrowFlags.union([.maskCommand, .maskShift]))
    }

    /// Waits until ⌘⌃⌥⇧ are physically released so our ⌘C isn't seen as ⌘⇧C. False on timeout.
    static func waitForModifiersReleased(timeout: TimeInterval) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            let flags = CGEventSource.flagsState(.combinedSessionState)
            if flags.intersection(modifierMask).isEmpty { return true }
            usleep(10_000)
        }
        return CGEventSource.flagsState(.combinedSessionState).intersection(modifierMask).isEmpty
    }
}
