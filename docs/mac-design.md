# لخفنه for macOS — Design & Contract

Same product as the Windows app (see docs/design.md): user typed in the wrong keyboard language, selects the text
(or selects nothing → current line), presses a global hotkey, the text is re-typed as if the same keys had been
pressed in the other input source. Arabic-only UI, friendly Egyptian colloquial, RTL. Distributed publicly as a
`.dmg` (drag to Applications). Not signed with a Developer ID yet → ad-hoc signature; Gatekeeper "Open Anyway" step
must be explained to users. Not App Store (sandbox forbids posting key events to other apps).

## Tech (hard constraints)
- Swift Package in `mac/`, `swift-tools-version:5.9`, Swift 5 language mode, platform `.macOS(.v13)`.
  AppKit + SwiftUI (NSHostingView/NSHostingController). No Xcode project. No third-party packages.
- Nobody on the team can compile locally (dev machine is Windows). CI on GitHub `macos-15` runner is the compiler:
  write conservative, well-known APIs only; no Swift 6-only syntax; no macOS 14+ APIs without `#available`.
- Bundle id `com.lakhfana.app`, executable `Lakhfana`, bundle `Lakhfana.app`, display name `لخفنه`,
  `LSUIElement = true` (menu-bar app), `CFBundleDevelopmentRegion = ar`, `CFBundleLocalizations = [ar]`, plus an
  `ar.lproj/InfoPlist.strings` so AppKit lays out menus/windows RTL.
- Universal binary (arm64 + x86_64).

## Layout
```
mac/Package.swift                                   [core]
mac/Sources/LakhfanaCore/InputSources.swift         [core]  TIS input sources
mac/Sources/LakhfanaCore/LayoutMap.swift            [core]  UCKeyTranslate key maps
mac/Sources/LakhfanaCore/Converter.swift            [core]
mac/Sources/LakhfanaCore/EnglishScorer.swift        [core]
mac/Sources/Lakhfana/Model/Hotkey.swift             [core]
mac/Sources/Lakhfana/Model/AppSettings.swift        [core]
mac/Sources/Lakhfana/Engine/HotkeyCenter.swift      [core]  Carbon RegisterEventHotKey
mac/Sources/Lakhfana/Engine/KeySender.swift         [core]  CGEvent ⌘C / ⌘V / line select
mac/Sources/Lakhfana/Engine/PasteboardHelper.swift  [core]
mac/Sources/Lakhfana/Engine/Permissions.swift       [core]  Accessibility (AXIsProcessTrusted)
mac/Sources/Lakhfana/Engine/SwitchEngine.swift      [core]
mac/Sources/Lakhfana/main.swift                     [ui]    NSApplication bootstrap (accessory policy)
mac/Sources/Lakhfana/UI/*.swift                     [ui]    AppDelegate, MenuBarController, Toast, Theme,
                                                            HotkeyRecorder, OnboardingWindow, SettingsWindow, LoginItem
mac/Tests/LakhfanaCoreTests/ConverterTests.swift    [core]
mac/Resources/Info.plist, ar.lproj/InfoPlist.strings, AppIcon-1024.png   [pkg]
mac/scripts/bundle.sh                               [pkg]   build universal → .app → ad-hoc sign → .dmg
.github/workflows/mac.yml                           [pkg]   swift test, bundle, smoke-launch, upload dmg artifact
```

## Contract

### LakhfanaCore (module `LakhfanaCore`, all `public`)
```swift
import Carbon
public struct InputSourceInfo {
    public let id: String            // kTISPropertyInputSourceID, e.g. "com.apple.keylayout.Arabic-PC"
    public let name: String          // localized name, e.g. "Arabic - PC"
    public let languages: [String]   // e.g. ["ar"]
    public let source: TISInputSource
}
public enum InputSources {
    public static func enabledKeyboardLayouts() -> [InputSourceInfo]      // enabled+selectable, with layout data
    public static func installed(id: String) -> InputSourceInfo?          // includeAllInstalled (tests)
    public static func current() -> InputSourceInfo?                      // TISCopyCurrentKeyboardLayoutInputSource
    public static func pickPair(_ all: [InputSourceInfo], savedA: String?, savedB: String?) -> (InputSourceInfo, InputSourceInfo)?
        // saved ids if enabled; else first Arabic + first English; else first two; nil if < 2 different layouts
    public static func select(_ id: String)                              // TISSelectInputSource
}
public final class Converter {
    public init?(a: InputSourceInfo, b: InputSourceInfo)
    public var idA: String { get }
    public var idB: String { get }
    public func convert(_ text: String, fallbackSourceID: String?) -> (text: String, targetID: String)
}
```

### App target (module `Lakhfana`, internal)
```swift
struct Hotkey: Codable, Equatable {
    var keyCode: UInt32          // Carbon virtual key code (kVK_*)
    var modifiers: UInt32        // Carbon flags: cmdKey | shiftKey | optionKey | controlKey
    static let `default`: Hotkey // ⌃⇧Space  (keyCode 49, controlKey|shiftKey)
    var isEmpty: Bool
    var displayString: String    // "⌃⌥⇧⌘" order then key name: "⌃⇧Space", "⌘⇧K", "F5"
    static func keyName(_ keyCode: UInt32) -> String   // US-style names: "A", "1", "Space", "Return", "←", "F13"...
    init(keyCode: UInt32, modifiers: UInt32)
    init(keyCode: UInt16, cocoaFlags: NSEvent.ModifierFlags)  // for the recorder
}
final class AppSettings: ObservableObject {       // persisted in UserDefaults on every change
    static let shared: AppSettings
    @Published var hotkey: Hotkey                 // default .default
    @Published var layoutA: String?               // input source id; nil = auto
    @Published var layoutB: String?
    @Published var switchLayoutAfter: Bool        // default true
    @Published var selectLineIfEmpty: Bool        // default true
    @Published var launchAtLogin: Bool            // default true (UI applies it via LoginItem)
    @Published var firstRunDone: Bool             // default false
    @Published var paused: Bool                   // default false
}
final class HotkeyCenter {                        // main thread only
    static let shared: HotkeyCenter
    var onPressed: (() -> Void)?
    private(set) var current: Hotkey?
    var isRegistered: Bool { get }
    @discardableResult func register(_ hk: Hotkey) -> Bool
    func unregister()
    func suspend()                                // while the recorder is recording
    func resume()
    static func isAvailable(_ hk: Hotkey) -> Bool // try-register then release; true for the one we hold
    static func isReserved(_ hk: Hotkey) -> Bool  // ⌘C ⌘V ⌘X ⌘A ⌘Z ⌘⇧Z ⌘S ⌘Q ⌘W ⌘Tab ⌘Space ⌃Space ⌃⌥Space ⌘⇧3/4/5, plain keys without modifiers except F13–F19
}
enum Permissions {
    static var isTrusted: Bool { get }            // AXIsProcessTrusted()
    static func promptIfNeeded()                  // AXIsProcessTrustedWithOptions(prompt: true)
    static func openAccessibilitySettings()       // x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility
}
final class SwitchEngine {
    static let shared: SwitchEngine
    var onNotify: ((String) -> Void)?             // Arabic message; always called on the main thread
    func trigger()                                // call on main thread; ignored while a run is in progress
    func reloadLayouts()
}
```

## Engine behaviour (macOS specifics)
1. Hotkey via Carbon `RegisterEventHotKey` (needs no permission). Posting key events needs Accessibility; if not
   trusted → onNotify("لخفنه محتاج صلاحية Accessibility…") and the UI opens the permission step.
2. Wait until ⌘⌃⌥⇧ are physically released (`CGEventSource.flagsState(.combinedSessionState)`, ≤1.5 s).
3. Frontmost app = `NSWorkspace.shared.frontmostApplication`; ignore our own process.
4. Back up the general pasteboard (every item, every type's data). Remember `changeCount`, post ⌘C (CGEvent,
   keycode 8, flags exactly `.maskCommand`, private event source), poll ≤ 400 ms for change.
   No change / empty string / editor empty-selection line copy (VS Code: `"isFromEmptySelection":true` inside any
   pasteboard type data, e.g. `vscode-editor-data` or `org.chromium.web-custom-data`) → no selection.
5. No selection: if selectLineIfEmpty → ⌘← then ⌘⇧→, copy again; else notify "حدد الكلام الأول".
6. Convert; unchanged → restore + notify. Pressing again on the exact text we just pasted returns the original.
7. Write result as plain string + `org.nspasteboard.TransientType` and `org.nspasteboard.ConcealedType` markers
   (clipboard managers skip it), post ⌘V, wait ~350 ms, restore backup only if `changeCount` is still ours.
8. If switchLayoutAfter → `InputSources.select(targetID)`.

## Converter (port of the Windows algorithm)
Key maps from `UCKeyTranslate` on `kTISPropertyUnicodeKeyLayoutData` for key codes 0…50 (and other printable ANSI
codes), modifier states none and shift (`(shiftKey >> 8) & 0xFF`), `kUCKeyTranslateNoDeadKeysBit`; skip dead keys,
control chars, empty output; outputs may be multi-char. Per-word direction by chars unique to each layout; global
fallback; English-bigram disambiguation (same table and case-flip penalty as Windows `EnglishScorer.cs`) when the
target is Latin and a segmentation is ambiguous; pass-through for unknown chars; whitespace preserved exactly.
Tests (CI): with `com.apple.keylayout.Arabic-PC` + `com.apple.keylayout.US` (loaded via `installed(id:)`):
"hgsghl ugd;l" → "السلام عليكم", "شلاخعف" → "about", "مهلاف" → "light", "لامشؤن" → "black", whitespace,
mixed words, emoji pass-through, random round-trip; with `com.apple.keylayout.Arabic` (Mac default Arabic): round-trip.

## UI
Menu-bar icon (template image, monochrome ⇄-style glyph). Menu: الإعدادات… / إيقاف مؤقت ⇄ تشغيل / — / خروج من لخفنه.
Onboarding window (first run): welcome + example chips, hotkey recorder, language pair (chips if exactly two,
pickers if more, red hint + "افتح إعدادات الكيبورد" if fewer than two), toggles (يشتغل لما الماك يفتح / يغيّر لغة
الكيبورد بعد التحويل / لو مفيش كلام متحدد، يحدد السطر لوحده), then an Accessibility step: explanation, button
"افتح الإعدادات", live status (polls `Permissions.isTrusted` every 1 s, turns green "تمام، الصلاحية اتفعّلت"), and
"يلا نبدأ" (enabled when trusted; secondary "بعدين" allowed). Settings window: same controls + permission status +
version + "خروج". Toasts: small rounded HUD panel under the menu bar, auto-hide 2.5 s. All RTL
(`.environment(\.layoutDirection, .rightToLeft)`), system font, one accent colour (indigo #4F46E5) matching Windows.

## Known limits
- Ad-hoc signature: first launch needs System Settings → Privacy & Security → "Open Anyway"; each new build may
  require re-granting Accessibility. Fixed by a Developer ID + notarization later (no code changes).
