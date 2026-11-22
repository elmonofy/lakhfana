import AppKit
import Carbon

/// The single global hotkey, through Carbon RegisterEventHotKey (needs no special permission). Main thread only.
final class HotkeyCenter {
    static let shared = HotkeyCenter()

    var onPressed: (() -> Void)?
    private(set) var current: Hotkey?
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?
    private var suspended = false

    private static let signature: OSType = 0x4C4B4846   // 'LKHF'
    private static let hotkeyID: UInt32 = 1

    var isRegistered: Bool { return ref != nil }

    private init() {
        installHandler()
    }

    private func installHandler() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(GetApplicationEventTarget(), { (_, event, _) -> OSStatus in
            var hkID = EventHotKeyID()
            let err = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                        nil, MemoryLayout<EventHotKeyID>.size, nil, &hkID)
            if err == noErr && hkID.signature == HotkeyCenter.signature {
                DispatchQueue.main.async {
                    HotkeyCenter.shared.onPressed?()
                }
            }
            return noErr
        }, 1, &spec, nil, &handler)
        if status != noErr {
            NSLog("Lakhfana: InstallEventHandler failed \(status)")
        }
    }

    private static func registerRaw(_ hk: Hotkey) -> EventHotKeyRef? {
        var newRef: EventHotKeyRef?
        let id = EventHotKeyID(signature: signature, id: hotkeyID)
        let status = RegisterEventHotKey(hk.keyCode, hk.modifiers, id, GetApplicationEventTarget(), 0, &newRef)
        return status == noErr ? newRef : nil
    }

    /// Registers `hk` (replacing the previous one). Returns false if another app owns it.
    @discardableResult
    func register(_ hk: Hotkey) -> Bool {
        unregisterRaw()
        current = hk
        suspended = false
        if hk.isEmpty { return false }
        ref = HotkeyCenter.registerRaw(hk)
        return ref != nil
    }

    func unregister() {
        unregisterRaw()
        current = nil
        suspended = false
    }

    private func unregisterRaw() {
        if let r = ref {
            UnregisterEventHotKey(r)
            ref = nil
        }
    }

    /// Temporarily releases the hotkey (while the user records a new one).
    func suspend() {
        guard !suspended else { return }
        suspended = true
        unregisterRaw()
    }

    func resume() {
        guard suspended else { return }
        suspended = false
        if let hk = current, !hk.isEmpty, !AppSettings.shared.paused {
            ref = HotkeyCenter.registerRaw(hk)
        }
    }

    /// True if `hk` can be registered (or is the one we currently hold).
    static func isAvailable(_ hk: Hotkey) -> Bool {
        if hk.isEmpty { return false }
        let me = HotkeyCenter.shared
        if me.isRegistered, let cur = me.current, cur == hk { return true }
        if let r = registerRaw(hk) {
            UnregisterEventHotKey(r)
            return true
        }
        return false
    }

    /// Shortcuts people rely on everywhere; never allowed as the global hotkey.
    static func isReserved(_ hk: Hotkey) -> Bool {
        if hk.isEmpty { return true }
        let k = Int(hk.keyCode)
        let cmd = UInt32(cmdKey), shift = UInt32(shiftKey), opt = UInt32(optionKey), ctrl = UInt32(controlKey)
        let m = hk.modifiers

        // No ⌘/⌃/⌥ at all: only F13–F19 are allowed alone (or with ⇧).
        if m & (cmd | opt | ctrl) == 0 {
            return !Hotkey.isFunctionKeyAllowedAlone(hk.keyCode)
        }
        if m == cmd {
            let reservedCmd = [kVK_ANSI_C, kVK_ANSI_V, kVK_ANSI_X, kVK_ANSI_A, kVK_ANSI_Z, kVK_ANSI_S, kVK_ANSI_Q,
                               kVK_ANSI_W, kVK_ANSI_F, kVK_ANSI_P, kVK_ANSI_N, kVK_ANSI_O, kVK_ANSI_T, kVK_ANSI_H,
                               kVK_ANSI_M, kVK_Tab, kVK_Space, kVK_ANSI_Grave, kVK_ANSI_Comma]
            if reservedCmd.contains(k) { return true }
        }
        if m == cmd | shift {
            if [kVK_ANSI_Z, kVK_ANSI_3, kVK_ANSI_4, kVK_ANSI_5, kVK_Tab].contains(k) { return true }
        }
        if m == cmd | opt && k == kVK_Escape { return true }
        if m == ctrl && k == kVK_Space { return true }               // previous input source
        if m == ctrl | opt && k == kVK_Space { return true }         // next input source
        if m == ctrl | cmd && k == kVK_Space { return true }         // emoji & symbols
        if m == ctrl | cmd && k == kVK_ANSI_Q { return true }        // lock screen
        if m & cmd != 0 && k == kVK_Space && m & opt != 0 { return true }   // Finder search
        return isSystemShortcut(hk)
    }

    /// Enabled macOS system shortcuts (Mission Control, Spaces, screenshots, input sources, the user's own
    /// customisations…). They win over Carbon hotkeys, so ours would never fire.
    static func isSystemShortcut(_ hk: Hotkey) -> Bool {
        var unmanaged: Unmanaged<CFArray>?
        guard CopySymbolicHotKeys(&unmanaged) == noErr, let cf = unmanaged?.takeRetainedValue() else { return false }
        let entries = (cf as NSArray).compactMap { $0 as? NSDictionary }
        for entry in entries {
            let enabled = (entry["kHISymbolicHotKeyEnabled"] as? NSNumber)?.boolValue ?? false
            guard enabled,
                  let code = (entry["kHISymbolicHotKeyCode"] as? NSNumber)?.uint32Value,
                  let mods = (entry["kHISymbolicHotKeyModifiers"] as? NSNumber)?.uint32Value else { continue }
            if code == hk.keyCode && (mods & Hotkey.carbonModifierMask) == hk.modifiers { return true }
        }
        return false
    }
}
