import Foundation
import Carbon

/// What every printable key types in one keyboard layout (unshifted and shifted), read with UCKeyTranslate,
/// plus the reverse map. Works on Unicode scalars, never on Characters: Arabic diacritics would otherwise merge
/// with the previous letter into one grapheme.
/// A keystroke is encoded as `keyCode | shiftBit`.
public final class LayoutMap {
    public static let shiftBit = 0x100
    public static let maxOutputScalars = 3

    public let id: String
    /// keystroke → scalars it types (1...3).
    public private(set) var outputs: [Int: [Unicode.Scalar]] = [:]
    /// typed text (as String of scalars) → keystroke; unshifted wins when several keys type the same text.
    public private(set) var reverse: [String: Int] = [:]
    /// Every scalar that appears in any output.
    public private(set) var scalars = Set<Unicode.Scalar>()
    public private(set) var maxLength = 0
    /// True when most unshifted letters are ASCII Latin.
    public private(set) var isLatin = false

    /// Printable keys on ANSI/ISO keyboards (kVK_ANSI_A = 0 … kVK_ANSI_Grave = 50) without Return/Tab/Space.
    static let keyCodes: [UInt16] = (0...50).map { UInt16($0) }.filter { $0 != 36 && $0 != 48 && $0 != 49 }

    public init?(_ info: InputSourceInfo) {
        id = info.id
        guard let ptr = TISGetInputSourceProperty(info.source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let data = Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data
        if data.isEmpty { return nil }
        build(data)
        if outputs.isEmpty { return nil }
    }

    public func render(_ keystroke: Int) -> [Unicode.Scalar]? {
        return outputs[keystroke]
    }

    private func build(_ data: Data) {
        let kbdType = UInt32(LMGetKbdType())
        data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) in
            guard let base = raw.baseAddress else { return }
            let layout = base.assumingMemoryBound(to: UCKeyboardLayout.self)
            // Unshifted pass first so unshifted keystrokes win in the reverse map.
            for shifted in [false, true] {
                let modifierState = shifted ? UInt32((shiftKey >> 8) & 0xFF) : 0
                for code in LayoutMap.keyCodes {
                    guard let out = LayoutMap.translate(layout, code, modifierState, kbdType) else { continue }
                    let ks = Int(code) | (shifted ? LayoutMap.shiftBit : 0)
                    outputs[ks] = out
                    let key = LayoutMap.string(out)
                    if reverse[key] == nil { reverse[key] = ks }
                    for s in out { scalars.insert(s) }
                    maxLength = max(maxLength, out.count)
                }
            }
        }

        var letters = 0, latin = 0
        for (ks, out) in outputs where ks & LayoutMap.shiftBit == 0 && out.count == 1 {
            let s = out[0]
            guard s.properties.isAlphabetic else { continue }
            letters += 1
            if (s.value >= 0x61 && s.value <= 0x7A) || (s.value >= 0x41 && s.value <= 0x5A) { latin += 1 }
        }
        isLatin = letters > 0 && latin * 2 >= letters
    }

    private static func translate(_ layout: UnsafePointer<UCKeyboardLayout>, _ code: UInt16,
                                  _ modifierState: UInt32, _ kbdType: UInt32) -> [Unicode.Scalar]? {
        var deadKeyState: UInt32 = 0
        var length = 0
        var chars = [UniChar](repeating: 0, count: 8)
        let status = UCKeyTranslate(layout, code, UInt16(kUCKeyActionDown), modifierState, kbdType,
                                    OptionBits(0), &deadKeyState, chars.count, &length, &chars)
        if status != noErr { return nil }
        if deadKeyState != 0 { return nil }   // dead key: skip it
        if length <= 0 { return nil }
        let text = String(utf16CodeUnits: chars, count: length)
        let out = Array(text.unicodeScalars)
        if out.isEmpty || out.count > maxOutputScalars { return nil }
        for s in out {
            let v = s.value
            if v < 0x20 || v == 0x7F || (v >= 0x80 && v < 0xA0) { return nil }
            if v >= 0xF700 && v <= 0xF8FF { return nil }   // function-key private use area
        }
        if out.allSatisfy({ $0.properties.isWhitespace }) { return nil }
        return out
    }

    public static func string(_ scalars: [Unicode.Scalar]) -> String {
        var s = String.UnicodeScalarView()
        s.append(contentsOf: scalars)
        return String(s)
    }

    public static func string(_ scalars: ArraySlice<Unicode.Scalar>) -> String {
        var s = String.UnicodeScalarView()
        s.append(contentsOf: scalars)
        return String(s)
    }
}
