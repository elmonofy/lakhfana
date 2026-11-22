import Foundation
import Combine

/// User preferences, persisted in UserDefaults on every change.
final class AppSettings: ObservableObject {
    static let shared = AppSettings()

    private enum Key {
        static let hotkey = "hotkey"
        static let layoutA = "layoutA"
        static let layoutB = "layoutB"
        static let switchLayoutAfter = "switchLayoutAfter"
        static let selectLineIfEmpty = "selectLineIfEmpty"
        static let launchAtLogin = "launchAtLogin"
        static let firstRunDone = "firstRunDone"
        static let paused = "paused"
    }

    private let defaults: UserDefaults

    @Published var hotkey: Hotkey { didSet { saveHotkey() } }
    @Published var layoutA: String? { didSet { defaults.set(layoutA, forKey: Key.layoutA) } }
    @Published var layoutB: String? { didSet { defaults.set(layoutB, forKey: Key.layoutB) } }
    @Published var switchLayoutAfter: Bool { didSet { defaults.set(switchLayoutAfter, forKey: Key.switchLayoutAfter) } }
    @Published var selectLineIfEmpty: Bool { didSet { defaults.set(selectLineIfEmpty, forKey: Key.selectLineIfEmpty) } }
    @Published var launchAtLogin: Bool { didSet { defaults.set(launchAtLogin, forKey: Key.launchAtLogin) } }
    @Published var firstRunDone: Bool { didSet { defaults.set(firstRunDone, forKey: Key.firstRunDone) } }
    @Published var paused: Bool { didSet { defaults.set(paused, forKey: Key.paused) } }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        defaults.register(defaults: [
            Key.switchLayoutAfter: true,
            Key.selectLineIfEmpty: true,
            Key.launchAtLogin: true,
            Key.firstRunDone: false,
            Key.paused: false,
        ])

        var hk = Hotkey.default
        if let data = defaults.data(forKey: Key.hotkey),
           let decoded = try? JSONDecoder().decode(Hotkey.self, from: data),
           !decoded.isEmpty {
            hk = decoded
        }
        hotkey = hk
        layoutA = AppSettings.nonEmpty(defaults.string(forKey: Key.layoutA))
        layoutB = AppSettings.nonEmpty(defaults.string(forKey: Key.layoutB))
        switchLayoutAfter = defaults.bool(forKey: Key.switchLayoutAfter)
        selectLineIfEmpty = defaults.bool(forKey: Key.selectLineIfEmpty)
        launchAtLogin = defaults.bool(forKey: Key.launchAtLogin)
        firstRunDone = defaults.bool(forKey: Key.firstRunDone)
        paused = defaults.bool(forKey: Key.paused)
    }

    private static func nonEmpty(_ s: String?) -> String? {
        guard let s = s, !s.isEmpty else { return nil }
        return s
    }

    private func saveHotkey() {
        if let data = try? JSONEncoder().encode(hotkey) {
            defaults.set(data, forKey: Key.hotkey)
        }
    }
}
