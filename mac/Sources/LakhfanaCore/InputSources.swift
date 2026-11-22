import Foundation
import Carbon

/// One keyboard layout input source (e.g. "com.apple.keylayout.Arabic-PC").
public struct InputSourceInfo {
    public let id: String
    public let name: String
    public let languages: [String]
    public let source: TISInputSource

    /// Primary language code ("ar", "en", ...), lowercased.
    public var primaryLanguage: String {
        guard let first = languages.first else { return "" }
        return String(first.lowercased().prefix(while: { $0 != "-" && $0 != "_" }))
    }
}

public enum InputSources {

    private static func string(_ source: TISInputSource, _ key: CFString) -> String? {
        guard let ptr = TISGetInputSourceProperty(source, key) else { return nil }
        return Unmanaged<CFString>.fromOpaque(ptr).takeUnretainedValue() as String
    }

    private static func bool(_ source: TISInputSource, _ key: CFString) -> Bool {
        guard let ptr = TISGetInputSourceProperty(source, key) else { return false }
        return CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque(ptr).takeUnretainedValue())
    }

    private static func languages(_ source: TISInputSource) -> [String] {
        guard let ptr = TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages) else { return [] }
        let arr = Unmanaged<CFArray>.fromOpaque(ptr).takeUnretainedValue() as NSArray
        return arr.compactMap { $0 as? String }
    }

    static func hasLayoutData(_ source: TISInputSource) -> Bool {
        return TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) != nil
    }

    static func info(_ source: TISInputSource) -> InputSourceInfo? {
        guard let id = string(source, kTISPropertyInputSourceID) else { return nil }
        let name = string(source, kTISPropertyLocalizedName) ?? id
        return InputSourceInfo(id: id, name: name, languages: languages(source), source: source)
    }

    private static func list(includeAllInstalled: Bool) -> [TISInputSource] {
        let filter: [String: Any] = [
            kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String,
        ]
        guard let cf = TISCreateInputSourceList(filter as CFDictionary, includeAllInstalled)?.takeRetainedValue() else {
            return []
        }
        let count = CFArrayGetCount(cf)
        var result: [TISInputSource] = []
        result.reserveCapacity(count)
        for i in 0..<count {
            guard let raw = CFArrayGetValueAtIndex(cf, i) else { continue }
            let src = Unmanaged<TISInputSource>.fromOpaque(raw).takeUnretainedValue()
            result.append(src)
        }
        return result
    }

    /// Enabled, selectable keyboard layouts that carry 'uchr' layout data.
    public static func enabledKeyboardLayouts() -> [InputSourceInfo] {
        var seen = Set<String>()
        var result: [InputSourceInfo] = []
        for src in list(includeAllInstalled: false) {
            guard bool(src, kTISPropertyInputSourceIsEnabled),
                  bool(src, kTISPropertyInputSourceIsSelectCapable),
                  hasLayoutData(src),
                  let inf = info(src),
                  !seen.contains(inf.id) else { continue }
            seen.insert(inf.id)
            result.append(inf)
        }
        return result
    }

    /// Any installed layout by id, enabled or not (used by tests).
    public static func installed(id: String) -> InputSourceInfo? {
        let filter: [String: Any] = [kTISPropertyInputSourceID as String: id]
        if let cf = TISCreateInputSourceList(filter as CFDictionary, true)?.takeRetainedValue(),
           CFArrayGetCount(cf) > 0,
           let raw = CFArrayGetValueAtIndex(cf, 0) {
            let src = Unmanaged<TISInputSource>.fromOpaque(raw).takeUnretainedValue()
            if hasLayoutData(src) { return info(src) }
        }
        return nil
    }

    /// The layout currently used for typing.
    public static func current() -> InputSourceInfo? {
        guard let src = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue() else { return nil }
        return info(src)
    }

    /// Saved pair if both are enabled; else first Arabic + first English; else first two different layouts.
    public static func pickPair(_ all: [InputSourceInfo], savedA: String?, savedB: String?) -> (InputSourceInfo, InputSourceInfo)? {
        func find(_ id: String?) -> InputSourceInfo? {
            guard let id = id, !id.isEmpty else { return nil }
            return all.first(where: { $0.id == id })
        }
        if let a = find(savedA), let b = find(savedB), a.id != b.id { return (a, b) }

        func firstLang(_ lang: String, except: String?) -> InputSourceInfo? {
            return all.first(where: { $0.primaryLanguage == lang && $0.id != except })
        }
        func firstOther(_ except: String) -> InputSourceInfo? {
            return all.first(where: { $0.id != except })
        }

        if let keep = find(savedA) ?? find(savedB) {
            var partner: InputSourceInfo? = nil
            if keep.primaryLanguage == "ar" { partner = firstLang("en", except: keep.id) }
            else if keep.primaryLanguage == "en" { partner = firstLang("ar", except: keep.id) }
            if partner == nil { partner = firstOther(keep.id) }
            guard let p = partner else { return nil }
            return find(savedA) != nil ? (keep, p) : (p, keep)
        }

        if let ar = firstLang("ar", except: nil), let en = firstLang("en", except: nil) { return (ar, en) }
        if let first = all.first, let second = firstOther(first.id) { return (first, second) }
        return nil
    }

    /// Switches the system to the given layout.
    public static func select(_ id: String) {
        let filter: [String: Any] = [kTISPropertyInputSourceID as String: id]
        guard let cf = TISCreateInputSourceList(filter as CFDictionary, false)?.takeRetainedValue(),
              CFArrayGetCount(cf) > 0,
              let raw = CFArrayGetValueAtIndex(cf, 0) else { return }
        let src = Unmanaged<TISInputSource>.fromOpaque(raw).takeUnretainedValue()
        TISSelectInputSource(src)
    }
}
