import AppKit
import Carbon

/// A global shortcut: Carbon key code + Carbon modifier flags (cmdKey | shiftKey | optionKey | controlKey).
struct Hotkey: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: UInt32

    static let carbonModifierMask = UInt32(cmdKey | shiftKey | optionKey | controlKey)

    /// ⌃⇧Space
    static let `default` = Hotkey(keyCode: UInt32(kVK_Space), modifiers: UInt32(controlKey | shiftKey))

    init(keyCode: UInt32, modifiers: UInt32) {
        self.keyCode = keyCode
        self.modifiers = modifiers & Hotkey.carbonModifierMask
    }

    /// From a recorder key event.
    init(keyCode: UInt16, cocoaFlags: NSEvent.ModifierFlags) {
        var m: UInt32 = 0
        if cocoaFlags.contains(.command) { m |= UInt32(cmdKey) }
        if cocoaFlags.contains(.shift) { m |= UInt32(shiftKey) }
        if cocoaFlags.contains(.option) { m |= UInt32(optionKey) }
        if cocoaFlags.contains(.control) { m |= UInt32(controlKey) }
        self.init(keyCode: UInt32(keyCode), modifiers: m)
    }

    var isEmpty: Bool { return keyCode == UInt32.max }

    static let empty = Hotkey(keyCode: UInt32.max, modifiers: 0)

    var hasCommand: Bool { return modifiers & UInt32(cmdKey) != 0 }
    var hasShift: Bool { return modifiers & UInt32(shiftKey) != 0 }
    var hasOption: Bool { return modifiers & UInt32(optionKey) != 0 }
    var hasControl: Bool { return modifiers & UInt32(controlKey) != 0 }

    /// Modifier symbols in Apple's order: ⌃⌥⇧⌘.
    var modifierSymbols: [String] {
        var parts: [String] = []
        if hasControl { parts.append("⌃") }
        if hasOption { parts.append("⌥") }
        if hasShift { parts.append("⇧") }
        if hasCommand { parts.append("⌘") }
        return parts
    }

    /// "⌃⇧Space", "⌘⇧K", "F13"
    var displayString: String {
        if isEmpty { return "" }
        return modifierSymbols.joined() + Hotkey.keyName(keyCode)
    }

    static func keyName(_ keyCode: UInt32) -> String {
        switch Int(keyCode) {
        case kVK_Space: return "Space"
        case kVK_Return: return "Return"
        case kVK_Tab: return "Tab"
        case kVK_Delete: return "Delete"
        case kVK_ForwardDelete: return "⌦"
        case kVK_Escape: return "Esc"
        case kVK_LeftArrow: return "←"
        case kVK_RightArrow: return "→"
        case kVK_UpArrow: return "↑"
        case kVK_DownArrow: return "↓"
        case kVK_Home: return "Home"
        case kVK_End: return "End"
        case kVK_PageUp: return "PgUp"
        case kVK_PageDown: return "PgDn"
        case kVK_F1: return "F1"
        case kVK_F2: return "F2"
        case kVK_F3: return "F3"
        case kVK_F4: return "F4"
        case kVK_F5: return "F5"
        case kVK_F6: return "F6"
        case kVK_F7: return "F7"
        case kVK_F8: return "F8"
        case kVK_F9: return "F9"
        case kVK_F10: return "F10"
        case kVK_F11: return "F11"
        case kVK_F12: return "F12"
        case kVK_F13: return "F13"
        case kVK_F14: return "F14"
        case kVK_F15: return "F15"
        case kVK_F16: return "F16"
        case kVK_F17: return "F17"
        case kVK_F18: return "F18"
        case kVK_F19: return "F19"
        case kVK_ANSI_A: return "A"
        case kVK_ANSI_B: return "B"
        case kVK_ANSI_C: return "C"
        case kVK_ANSI_D: return "D"
        case kVK_ANSI_E: return "E"
        case kVK_ANSI_F: return "F"
        case kVK_ANSI_G: return "G"
        case kVK_ANSI_H: return "H"
        case kVK_ANSI_I: return "I"
        case kVK_ANSI_J: return "J"
        case kVK_ANSI_K: return "K"
        case kVK_ANSI_L: return "L"
        case kVK_ANSI_M: return "M"
        case kVK_ANSI_N: return "N"
        case kVK_ANSI_O: return "O"
        case kVK_ANSI_P: return "P"
        case kVK_ANSI_Q: return "Q"
        case kVK_ANSI_R: return "R"
        case kVK_ANSI_S: return "S"
        case kVK_ANSI_T: return "T"
        case kVK_ANSI_U: return "U"
        case kVK_ANSI_V: return "V"
        case kVK_ANSI_W: return "W"
        case kVK_ANSI_X: return "X"
        case kVK_ANSI_Y: return "Y"
        case kVK_ANSI_Z: return "Z"
        case kVK_ANSI_0: return "0"
        case kVK_ANSI_1: return "1"
        case kVK_ANSI_2: return "2"
        case kVK_ANSI_3: return "3"
        case kVK_ANSI_4: return "4"
        case kVK_ANSI_5: return "5"
        case kVK_ANSI_6: return "6"
        case kVK_ANSI_7: return "7"
        case kVK_ANSI_8: return "8"
        case kVK_ANSI_9: return "9"
        case kVK_ANSI_Minus: return "-"
        case kVK_ANSI_Equal: return "="
        case kVK_ANSI_LeftBracket: return "["
        case kVK_ANSI_RightBracket: return "]"
        case kVK_ANSI_Backslash: return "\\"
        case kVK_ANSI_Semicolon: return ";"
        case kVK_ANSI_Quote: return "'"
        case kVK_ANSI_Comma: return ","
        case kVK_ANSI_Period: return "."
        case kVK_ANSI_Slash: return "/"
        case kVK_ANSI_Grave: return "`"
        default: return String(format: "0x%02X", keyCode)
        }
    }

    /// True for keys that are modifiers themselves (never a hotkey's main key).
    static func isModifierKey(_ keyCode: UInt16) -> Bool {
        switch Int(keyCode) {
        case kVK_Command, kVK_RightCommand, kVK_Shift, kVK_RightShift, kVK_Option, kVK_RightOption,
             kVK_Control, kVK_RightControl, kVK_CapsLock, kVK_Function:
            return true
        default:
            return false
        }
    }

    static func isFunctionKeyAllowedAlone(_ keyCode: UInt32) -> Bool {
        let k = Int(keyCode)
        return k == kVK_F13 || k == kVK_F14 || k == kVK_F15 || k == kVK_F16 || k == kVK_F17 || k == kVK_F18 || k == kVK_F19
    }
}
