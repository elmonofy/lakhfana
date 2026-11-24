import AppKit

/// Detects a shortcut made of modifier keys only (e.g. ⌃⇧). Carbon hotkeys can't express it, so this watches
/// modifier changes with NSEvent monitors (needs the Accessibility permission the app already asks for).
/// It fires when the last modifier is released, provided exactly the target set was held, no other key, click or
/// scroll happened meanwhile, and the chord lasted at most 1.2 s. Only modifier state is looked at; nothing is recorded.
/// Main thread only.
final class ModifierChordMonitor {
    private static let maxChord: TimeInterval = 1.2
    private static let relevant: NSEvent.ModifierFlags = [.command, .option, .control, .shift]

    private let target: NSEvent.ModifierFlags
    private let onFire: () -> Void
    private var monitors: [Any] = []
    private var trustTimer: Timer?
    private var installedWhileTrusted = false

    private var held: NSEvent.ModifierFlags = []
    private var peak: NSEvent.ModifierFlags = []
    private var tainted = false
    private var startedAt = Date()

    init(target: NSEvent.ModifierFlags, onFire: @escaping () -> Void) {
        self.target = target.intersection(ModifierChordMonitor.relevant)
        self.onFire = onFire
    }

    func start() {
        install()
        // Global key monitors receive nothing until Accessibility is granted; reinstall once it is.
        trustTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if !self.installedWhileTrusted && Permissions.isTrusted { self.install() }
        }
    }

    func stop() {
        trustTimer?.invalidate()
        trustTimer = nil
        removeMonitors()
        reset()
    }

    deinit {
        trustTimer?.invalidate()
        for m in monitors { NSEvent.removeMonitor(m) }
    }

    private func install() {
        removeMonitors()
        reset()
        installedWhileTrusted = Permissions.isTrusted
        let mask: NSEvent.EventTypeMask = [.flagsChanged, .keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown,
                                           .scrollWheel, .systemDefined, .magnify, .swipe, .rotate, .gesture]
        if let g = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: { [weak self] event in
            self?.handle(event)
        }) {
            monitors.append(g)
        }
        // Our own windows (Settings) don't reach the global monitor.
        if let l = NSEvent.addLocalMonitorForEvents(matching: mask, handler: { [weak self] event in
            self?.handle(event)
            return event
        }) {
            monitors.append(l)
        }
    }

    private func removeMonitors() {
        for m in monitors { NSEvent.removeMonitor(m) }
        monitors.removeAll()
    }

    private func reset() {
        held = []
        peak = []
        tainted = false
    }

    /// True when a key, media key, click or scroll physically happened within the last `seconds` seconds
    /// (hardware-level: includes events the system or other apps swallowed).
    static func otherInputSince(_ seconds: TimeInterval) -> Bool {
        var types: [CGEventType] = [.keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel]
        if let systemDefined = CGEventType(rawValue: 14) { types.append(systemDefined) }   // NX_SYSDEFINED: media keys
        for t in types {
            if CGEventSource.secondsSinceLastEventType(.hidSystemState, eventType: t) < seconds { return true }
        }
        return false
    }

    private func handle(_ event: NSEvent) {
        if event.type != .flagsChanged {
            if !held.isEmpty { tainted = true }   // a real key / click inside the chord: a normal shortcut
            return
        }
        let mods = event.modifierFlags.intersection(ModifierChordMonitor.relevant)
        if held.isEmpty && !mods.isEmpty {
            peak = []
            tainted = false
            startedAt = Date()
        }
        held = mods
        peak.formUnion(mods)
        guard mods.isEmpty, !peak.isEmpty else { return }

        let elapsed = Date().timeIntervalSince(startedAt)
        // Keys taken by macOS or another app (⌘⇧4, ⌘⇧Tab, Raycast…) never reach our monitors, and nothing does
        // without Accessibility or under Secure Input: ask the HID layer whether anything else was pressed.
        let hit = !tainted && peak == target && elapsed <= ModifierChordMonitor.maxChord
            && Permissions.isTrusted && !ModifierChordMonitor.otherInputSince(elapsed)
        peak = []
        tainted = false
        if hit {
            let fire = onFire
            DispatchQueue.main.async { fire() }
        }
    }
}
