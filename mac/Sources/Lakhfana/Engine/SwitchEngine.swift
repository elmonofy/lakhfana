import AppKit
import Carbon
import LakhfanaCore

/// One hotkey press: copy the selection (or the current line), convert it to the other layout, paste it back,
/// restore the user's clipboard, and optionally switch the keyboard language.
/// Waiting happens on a background queue; pasteboard and input-source calls run on the main thread.
final class SwitchEngine {
    static let shared = SwitchEngine()

    var onNotify: ((String) -> Void)?

    private let queue = DispatchQueue(label: "com.lakhfana.switch")
    private var busy = false                      // main thread only
    private var converter: Converter?             // main thread only
    private var converterKey: String?
    // Last conversion, so pressing the hotkey again on the same text undoes it exactly.
    private var lastOriginal: String?
    private var lastConverted: String?
    private var lastOriginalID: String?

    private static let copyTimeout: TimeInterval = 0.4
    private static let pasteSettle: TimeInterval = 0.35

    private init() {}

    func reloadLayouts() {
        converter = nil
        converterKey = nil
    }

    func trigger() {
        guard !busy else { return }
        if !Permissions.isTrusted {
            say("لخفنه محتاج صلاحية Accessibility عشان يقدر ينسخ ويلزق. فعّلها من الإعدادات وجرّب تاني.")
            Permissions.promptIfNeeded()
            return
        }
        if IsSecureEventInputEnabled() {
            say("مش هينفع هنا: الخانة دي محمية (زي خانة الباسورد).")
            return
        }
        guard let conv = currentConverter() else {
            say("لازم يبقى عندك لغتين كيبورد على الأقل في الماك عشان لخفنه يشتغل.")
            return
        }
        let front = NSWorkspace.shared.frontmostApplication
        if front?.processIdentifier == ProcessInfo.processInfo.processIdentifier { return }

        let settings = AppSettings.shared
        let selectLine = settings.selectLineIfEmpty
        let switchAfter = settings.switchLayoutAfter
        let sourceID = InputSources.current()?.id

        busy = true
        queue.async { [weak self] in
            guard let self = self else { return }
            self.run(conv, front: front, sourceID: sourceID, selectLine: selectLine, switchAfter: switchAfter)
            DispatchQueue.main.async { self.busy = false }
        }
    }

    // MARK: - helpers

    private func say(_ message: String) {
        if Thread.isMainThread {
            onNotify?(message)
        } else {
            DispatchQueue.main.async { [weak self] in self?.onNotify?(message) }
        }
    }

    private func onMain<T>(_ work: () -> T) -> T {
        if Thread.isMainThread { return work() }
        return DispatchQueue.main.sync(execute: work)
    }

    private func currentConverter() -> Converter? {
        let settings = AppSettings.shared
        guard let pair = InputSources.pickPair(InputSources.enabledKeyboardLayouts(),
                                               savedA: settings.layoutA, savedB: settings.layoutB) else { return nil }
        let a = pair.0, b = pair.1
        let key = a.id + "|" + b.id
        if converter == nil || converterKey != key {
            converter = Converter(a: a, b: b)
            converterKey = key
        }
        return converter
    }

    /// ⌘C, then wait for the pasteboard to change. Nil when nothing is selected.
    private func copySelection() -> String? {
        let before = onMain { PasteboardHelper.changeCount }
        KeySender.copy()
        let deadline = Date().addingTimeInterval(SwitchEngine.copyTimeout)
        var changed = false
        while Date() < deadline {
            if onMain({ PasteboardHelper.changeCount }) != before { changed = true; break }
            usleep(10_000)
        }
        if !changed { return nil }
        usleep(20_000)   // let the source app finish writing all its types
        return onMain { () -> String? in
            if PasteboardHelper.isEmptySelectionLineCopy() { return nil }
            guard let s = PasteboardHelper.readString(), !s.isEmpty else { return nil }
            return s
        }
    }

    private func run(_ conv: Converter, front: NSRunningApplication?, sourceID: String?,
                     selectLine: Bool, switchAfter: Bool) {
        if !KeySender.waitForModifiersReleased(timeout: 1.5) {
            say("سيب الزراير وجرّب تاني.")
            return
        }

        let backup = onMain { PasteboardHelper.backup() }
        var restored = false
        defer {
            if !restored { onMain { PasteboardHelper.restore(backup) } }
        }

        var text = copySelection()
        if text == nil && selectLine {
            KeySender.selectCurrentLine()
            usleep(40_000)
            text = copySelection()
        }
        guard let original = text else {
            if !selectLine { say("حدد الكلام الأول وبعدين دوس الاختصار.") }
            return
        }

        let result: (text: String, targetID: String) = onMain {
            if let last = self.lastConverted, original == last, let orig = self.lastOriginal, let origID = self.lastOriginalID {
                return (text: orig, targetID: origID)
            }
            return conv.convert(original, fallbackSourceID: sourceID)
        }
        if result.text == original {
            say("مفيش حاجة تتحوّل في الكلام ده.")
            return
        }

        let ok = onMain { PasteboardHelper.setString(result.text) }
        if !ok {
            say("الحافظة مشغولة دلوقتي، جرّب تاني.")
            return
        }
        let ours = onMain { PasteboardHelper.changeCount }

        // Don't paste into a different app if the user switched away meanwhile.
        let stillFront = onMain { NSWorkspace.shared.frontmostApplication?.processIdentifier == front?.processIdentifier }
        if !stillFront { return }

        KeySender.paste()
        usleep(UInt32(SwitchEngine.pasteSettle * 1_000_000))

        onMain {
            if PasteboardHelper.changeCount == ours {
                PasteboardHelper.restore(backup)
            }
            self.lastOriginal = original
            self.lastConverted = result.text
            self.lastOriginalID = result.targetID == conv.idA ? conv.idB : conv.idA
            if switchAfter { InputSources.select(result.targetID) }
        }
        restored = true
    }
}
