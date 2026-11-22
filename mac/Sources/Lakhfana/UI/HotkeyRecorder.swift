import AppKit
import Carbon
import Combine
import SwiftUI

/// State of the shortcut recorder. While recording, a local key monitor swallows every key press in our app,
/// and the global hotkey is suspended so pressing the current shortcut records it instead of firing it.
@MainActor
final class HotkeyRecorderModel: ObservableObject {
    static let reservedMessage = "ده اختصار مهم في الماك، اختار غيره"
    static let takenMessage = "الاختصار ده مستخدم في برنامج تاني"
    static let recordingHint = "Esc للإلغاء، Delete للمسح"

    /// nil = cleared by the user (Delete).
    @Published private(set) var hotkey: Hotkey? = nil
    @Published private(set) var isRecording: Bool = false
    @Published private(set) var liveFlags: NSEvent.ModifierFlags = []
    @Published private(set) var status: StatusLine = StatusLine.hint("")

    private var monitor: Any?
    private var suspended = false
    private var rejection: String?

    init(hotkey: Hotkey?) {
        if let hk = hotkey, !hk.isEmpty {
            self.hotkey = hk
        }
        refreshStatus()
    }

    /// True when the current value can be saved.
    var isValid: Bool {
        return !isRecording && hotkey != nil && status.kind != .error
    }

    func toggle() {
        if isRecording { stop() } else { start() }
    }

    func start() {
        guard !isRecording else { return }
        rejection = nil
        liveFlags = []
        isRecording = true
        if !suspended {
            suspended = true
            HotkeyCenter.shared.suspend()
        }
        if monitor == nil {
            monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
                let consumed = MainActor.assumeIsolated { () -> Bool in
                    guard let model = self else { return false }
                    return model.handle(event)
                }
                return consumed ? nil : event
            }
        }
        refreshStatus()
    }

    /// Ends recording (keeps the last accepted value) and gives the global hotkey back.
    func stop() {
        if let m = monitor {
            NSEvent.removeMonitor(m)
            monitor = nil
        }
        if isRecording { isRecording = false }
        if !liveFlags.isEmpty { liveFlags = [] }
        rejection = nil
        if suspended {
            suspended = false
            HotkeyCenter.shared.resume()
        }
        refreshStatus()
    }

    /// Re-checks the current value (reserved / taken by another app) and updates the status line.
    func refreshStatus() {
        if isRecording {
            if let r = rejection {
                status = StatusLine.error(r)
            } else {
                status = StatusLine.hint(HotkeyRecorderModel.recordingHint)
            }
            return
        }
        guard let hk = hotkey, !hk.isEmpty else {
            status = StatusLine.error("اختار اختصار الأول")
            return
        }
        if HotkeyCenter.isReserved(hk) {
            status = StatusLine.error(HotkeyRecorderModel.reservedMessage)
        } else if !HotkeyCenter.isAvailable(hk) {
            status = StatusLine.error(HotkeyRecorderModel.takenMessage + "، جرّب غيره")
        } else {
            status = StatusLine.ok("تمام، الاختصار ده فاضي")
        }
    }

    // MARK: Key handling

    /// Returns true when the event was consumed.
    private func handle(_ event: NSEvent) -> Bool {
        guard isRecording else { return false }
        let mods = event.modifierFlags.intersection([.control, .option, .shift, .command])

        if event.type == .flagsChanged {
            if liveFlags != mods { liveFlags = mods }
            return false
        }
        guard event.type == .keyDown else { return false }
        if event.isARepeat { return true }

        let code = Int(event.keyCode)
        if Hotkey.isModifierKey(event.keyCode) { return true }

        if mods.isEmpty && code == kVK_Escape {
            stop()
            return true
        }
        if mods.isEmpty && (code == kVK_Delete || code == kVK_ForwardDelete) {
            hotkey = nil
            stop()
            return true
        }

        let strong = mods.intersection([.control, .option, .command])
        if strong.isEmpty && !Hotkey.isFunctionKeyAllowedAlone(UInt32(event.keyCode)) {
            reject(mods == .shift ? "Shift لوحده مش كفاية، زوّد ⌘ أو ⌃ أو ⌥" : "لازم معاه ⌘ أو ⌃ أو ⌥")
            return true
        }

        let hk = Hotkey(keyCode: event.keyCode, cocoaFlags: mods)
        if HotkeyCenter.isReserved(hk) {
            reject(HotkeyRecorderModel.reservedMessage)
            return true
        }
        if !HotkeyCenter.isAvailable(hk) {
            reject(HotkeyRecorderModel.takenMessage)
            return true
        }
        hotkey = hk
        stop()
        return true
    }

    private func reject(_ message: String) {
        rejection = message
        NSSound.beep()
        refreshStatus()
    }

    // MARK: Caps

    static func caps(for hk: Hotkey) -> [String] {
        return hk.modifierSymbols + [Hotkey.keyName(hk.keyCode)]
    }

    static func symbols(for flags: NSEvent.ModifierFlags) -> [String] {
        var out: [String] = []
        if flags.contains(.control) { out.append("⌃") }
        if flags.contains(.option) { out.append("⌥") }
        if flags.contains(.shift) { out.append("⇧") }
        if flags.contains(.command) { out.append("⌘") }
        return out
    }
}

/// The shortcut field: shows the key caps; click to record a new shortcut.
struct HotkeyRecorder: View {
    @ObservedObject var model: HotkeyRecorderModel

    var body: some View {
        Button(action: { model.toggle() }) {
            label
                .frame(minWidth: 150, minHeight: 32)
                .padding(.horizontal, 8)
                .background(model.isRecording ? Theme.accentSoft : Theme.fieldBackground,
                            in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: 8, style: .continuous)
                        .strokeBorder(model.isRecording ? Theme.accent : Theme.keyBorder,
                                      lineWidth: model.isRecording ? 1.5 : 1)
                )
                .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(.plain)
        .help(helpText)
        .accessibilityLabel(Text("اختصار التحويل"))
        .onDisappear { model.stop() }
    }

    private var helpText: String {
        if model.isRecording { return "دوس الاختصار الجديد، أو Esc للإلغاء" }
        return "دوس هنا عشان تغيّر الاختصار"
    }

    @ViewBuilder
    private var label: some View {
        if model.isRecording {
            if model.liveFlags.isEmpty {
                Text("دوس الاختصار…")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(Theme.accentText)
            } else {
                KeyCaps(keys: HotkeyRecorderModel.symbols(for: model.liveFlags) + ["…"], highlighted: true)
            }
        } else if let hk = model.hotkey {
            KeyCaps(keys: HotkeyRecorderModel.caps(for: hk))
        } else {
            Text("مفيش اختصار، دوس هنا واختار")
                .font(Theme.smallFont)
                .foregroundColor(Color.secondary)
        }
    }
}
