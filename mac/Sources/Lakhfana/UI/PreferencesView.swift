import AppKit
import Combine
import LakhfanaCore
import SwiftUI

// The shared body of the onboarding and settings windows: shortcut card, languages card, options card.
// Edits are kept in a draft (PreferencesModel) and written to AppSettings only on "يلا نبدأ" / "بعدين" / "حفظ".

@MainActor
final class PreferencesModel: ObservableObject {
    enum LanguageMode {
        case pair       // exactly two keyboard layouts: read-only chips
        case pickers    // three or more: two pickers
        case missing    // fewer than two usable layouts
    }

    static let layoutHint = "اختار اللغتين اللي بتكتب بيهم"

    let recorder: HotkeyRecorderModel

    @Published private(set) var layouts: [InputSourceInfo] = []
    @Published private(set) var mode: LanguageMode = .missing
    @Published var layoutA: String = "" {
        didSet { if layoutA != oldValue && layoutError != nil { layoutError = nil } }
    }
    @Published var layoutB: String = "" {
        didSet { if layoutB != oldValue && layoutError != nil { layoutError = nil } }
    }
    @Published var layoutError: String? = nil
    @Published var formError: String? = nil
    @Published var launchAtLogin: Bool = true
    @Published var switchLayoutAfter: Bool = true
    @Published var selectLineIfEmpty: Bool = true

    private let savedA: String?
    private let savedB: String?

    init(settings: AppSettings) {
        let hk = settings.hotkey
        recorder = HotkeyRecorderModel(hotkey: hk.isEmpty ? Hotkey.default : hk)
        savedA = settings.layoutA
        savedB = settings.layoutB
        launchAtLogin = settings.launchAtLogin
        switchLayoutAfter = settings.switchLayoutAfter
        selectLineIfEmpty = settings.selectLineIfEmpty
        reloadLayouts(force: true)
    }

    // MARK: Languages

    /// Re-reads the enabled keyboard layouts (the user may have just added one in System Settings).
    func refreshLayouts() {
        reloadLayouts(force: false)
    }

    private func reloadLayouts(force: Bool) {
        let now = InputSources.enabledKeyboardLayouts()
        let newIDs = now.map { $0.id }
        let oldIDs = layouts.map { $0.id }
        if !force && newIDs == oldIDs { return }

        let wantA: String? = layoutA.isEmpty ? savedA : layoutA
        let wantB: String? = layoutB.isEmpty ? savedB : layoutB
        layouts = now
        if let pair = InputSources.pickPair(now, savedA: wantA, savedB: wantB) {
            layoutA = pair.0.id
            layoutB = pair.1.id
            mode = now.count > 2 ? .pickers : .pair
        } else {
            layoutA = now.first?.id ?? ""
            layoutB = ""
            mode = .missing
        }
        layoutError = nil
    }

    func name(of id: String) -> String {
        if let info = layouts.first(where: { $0.id == id }) {
            return info.name.isEmpty ? info.id : info.name
        }
        return id
    }

    func openKeyboardSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.Keyboard-Settings.extension"),
           NSWorkspace.shared.open(url) {
            return
        }
        _ = NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/System Settings.app"))
    }

    // MARK: Validation / output

    /// Checks everything and shows inline errors. Stops a running recording first.
    func validate() -> Bool {
        formError = nil
        recorder.stop()
        refreshLayouts()

        var ok = recorder.isValid
        if mode == .missing || layoutA.isEmpty || layoutB.isEmpty {
            layoutError = "لازم يبقى عندك لغتين على الأقل"
            ok = false
        } else if layoutA == layoutB {
            layoutError = "اختار لغتين مختلفين"
            ok = false
        }
        if !ok { formError = "في حاجة محتاجة تتظبط فوق" }
        return ok
    }

    /// Writes the draft to the settings (only the values that changed, so observers fire only when needed).
    func write(to s: AppSettings) {
        if let hk = recorder.hotkey, hk != s.hotkey { s.hotkey = hk }
        if !layoutA.isEmpty && s.layoutA != layoutA { s.layoutA = layoutA }
        if !layoutB.isEmpty && s.layoutB != layoutB { s.layoutB = layoutB }
        if s.switchLayoutAfter != switchLayoutAfter { s.switchLayoutAfter = switchLayoutAfter }
        if s.selectLineIfEmpty != selectLineIfEmpty { s.selectLineIfEmpty = selectLineIfEmpty }
        if s.launchAtLogin != launchAtLogin { s.launchAtLogin = launchAtLogin }
    }

    /// Call when the window goes away or loses focus.
    func stopRecording() {
        recorder.stop()
    }
}

/// Live Accessibility status, polled every second while a window that shows it is open.
@MainActor
final class PermissionWatcher: ObservableObject {
    @Published private(set) var trusted: Bool = false
    private var timer: AnyCancellable?

    init() {
        trusted = Permissions.isTrusted
    }

    func start() {
        refresh()
        guard timer == nil else { return }
        timer = Timer.publish(every: 1, on: .main, in: .common)
            .autoconnect()
            .sink { [weak self] _ in self?.refresh() }
    }

    func stop() {
        timer?.cancel()
        timer = nil
    }

    func refresh() {
        let now = Permissions.isTrusted
        if now != trusted { trusted = now }
    }

    func openSystemSettings() {
        Permissions.promptIfNeeded()
        Permissions.openAccessibilitySettings()
    }
}

// MARK: - Views

struct PreferencesView: View {
    @ObservedObject var model: PreferencesModel

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.cardSpacing) {
            HotkeyCard(recorder: model.recorder)
            LanguagesCard(model: model)
            OptionsCard(model: model)
        }
    }
}

struct HotkeyCard: View {
    @ObservedObject var recorder: HotkeyRecorderModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            SettingRow(title: "اختصار التحويل", subtitle: "دوس الاختصار اللي يريحك") {
                HotkeyRecorder(model: recorder)
            }
            StatusText(line: recorder.status)
                .padding(.top, -4)
                .padding(.bottom, 12)
        }
        .card()
    }
}

struct LanguagesCard: View {
    @ObservedObject var model: PreferencesModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            switch model.mode {
            case .pair:
                pairRow
            case .pickers:
                pickersRow
            case .missing:
                missingRow
            }
        }
        .card()
    }

    private var pairRow: some View {
        let a = model.name(of: model.layoutA)
        let b = model.name(of: model.layoutB)
        return SettingRow(title: "اللغتين", subtitle: "هيقلب الكلام بين اللغتين دول") {
            HStack(spacing: 6) {
                Chip(text: a)
                Text("⇄").foregroundColor(Color.secondary)
                Chip(text: b)
            }
            .frame(maxWidth: 260)
            .help(a + "  ⇄  " + b)
        }
    }

    private var pickersRow: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text("اللغتين")
                    .font(Theme.headingFont)
                Text(model.layoutError ?? PreferencesModel.layoutHint)
                    .font(Theme.smallFont)
                    .foregroundColor(model.layoutError == nil ? Color.secondary : Theme.danger)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 10) {
                layoutPicker(selection: $model.layoutA)
                Text("⇄").foregroundColor(Color.secondary)
                layoutPicker(selection: $model.layoutB)
            }
        }
        .padding(.vertical, Theme.rowPaddingV)
    }

    private func layoutPicker(selection: Binding<String>) -> some View {
        Picker("", selection: selection) {
            ForEach(model.layouts, id: \.id) { info in
                Text(info.name.isEmpty ? info.id : info.name).tag(info.id)
            }
        }
        .labelsHidden()
        .pickerStyle(.menu)
        .frame(maxWidth: .infinity)
    }

    private var missingRow: some View {
        VStack(alignment: .leading, spacing: 0) {
            SettingRow(title: "اللغتين", subtitle: "لازم يبقى عندك لغتين في الكيبورد، زي العربي والإنجليزي") {
                Button("افتح إعدادات الكيبورد") { model.openKeyboardSettings() }
            }
            StatusText(line: StatusLine.error(model.layoutError ?? missingHint))
                .padding(.top, -4)
                .padding(.bottom, 12)
        }
    }

    private var missingHint: String {
        if model.layouts.count >= 2 {
            return "اللغتين اللي عندك ليهم نفس الكيبورد، ضيف لغة تانية من إعدادات الكيبورد"
        }
        return "ضيف لغة تانية من إعدادات الكيبورد (Input Sources) وبعدين ارجع هنا"
    }
}

struct OptionsCard: View {
    @ObservedObject var model: PreferencesModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ToggleRow(title: "يشتغل لما الماك يفتح",
                      subtitle: "يفتح لوحده أول ما الجهاز يشتغل",
                      isOn: $model.launchAtLogin)
            Divider()
            ToggleRow(title: "يغيّر لغة الكيبورد بعد التحويل",
                      subtitle: "عشان تكمّل كتابة باللغة الصح على طول",
                      isOn: $model.switchLayoutAfter)
            Divider()
            ToggleRow(title: "لو مفيش كلام متحدد، يحدد السطر لوحده",
                      subtitle: "ويحوّل السطر اللي انت واقف فيه",
                      isOn: $model.selectLineIfEmpty)
        }
        .card()
    }
}

/// The Accessibility step: explanation, "افتح الإعدادات", live status.
struct AccessibilityCard: View {
    @ObservedObject var watcher: PermissionWatcher
    let isOnboarding: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            SettingRow(title: title, subtitle: subtitle) {
                if !watcher.trusted {
                    Button("افتح الإعدادات") { watcher.openSystemSettings() }
                        .buttonStyle(.borderedProminent)
                        .tint(Theme.accent)
                }
            }
            StatusText(line: watcher.trusted
                       ? StatusLine.ok("تمام، الصلاحية اتفعّلت")
                       : StatusLine.error("لسه الصلاحية مش متفعّلة"))
                .padding(.top, -4)
                .padding(.bottom, watcher.trusted ? 12 : 8)
            if !watcher.trusted {
                Text("لو نزّلت نسخة جديدة ولقيت لخفنه متفعّل في القائمة ومش شغال، اقفله وافتحه تاني من نفس القائمة (أو شيله بعلامة − وضيفه من جديد).")
                    .font(Theme.tinyFont)
                    .foregroundColor(Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.bottom, 12)
            }
        }
        .card()
    }

    private var title: String {
        return isOnboarding ? "آخر خطوة: صلاحية Accessibility" : "صلاحية Accessibility"
    }

    private var subtitle: String {
        if watcher.trusted {
            return "لخفنه يقدر ينسخ الكلام اللي انت محدده ويكتبه مكانه."
        }
        return "عشان لخفنه يقدر ينسخ الكلام اللي انت محدده ويكتبه صح، لازم تسمحله: دوس «افتح الإعدادات»، وفي Privacy & Security افتح Accessibility وفعّل لخفنه."
    }
}
