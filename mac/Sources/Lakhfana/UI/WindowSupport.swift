import AppKit
import SwiftUI

/// Creates and shows the app's (few) windows. The SwiftUI root sizes itself; the window follows it.
@MainActor
enum WindowFactory {
    static func makeWindow(title: String) -> NSWindow {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: Theme.windowWidth, height: 600),
                         styleMask: [.titled, .closable],
                         backing: .buffered,
                         defer: false)
        w.title = title
        w.isReleasedWhenClosed = false
        w.tabbingMode = .disallowed
        w.collectionBehavior = [.moveToActiveSpace]
        return w
    }

    /// `build(nil)` is the plain layout; `build(h)` puts the middle in a scroll area of height `h`.
    /// The plain layout is used when it fits the screen; otherwise the window is capped to the screen's
    /// visible height (minus a margin) and the middle scrolls, keeping header and footer visible.
    static func setContent<V: View>(of window: NSWindow, build: (CGFloat?) -> V) {
        let natural = NSHostingView(rootView: build(nil)).fittingSize
        let cap = maxContentHeight(for: window)
        var root = build(nil)
        if natural.height > cap {
            // Header + footer + paddings: the same layout with an empty scroll area.
            let chrome = NSHostingView(rootView: build(0)).fittingSize.height
            root = build(max(160, cap - chrome))
        }
        let host = NSHostingView(rootView: root)
        window.contentView = host
        let size = host.fittingSize
        if size.width > 10 && size.height > 10 {
            window.setContentSize(size)
        }
        window.center()
    }

    /// Tallest content area that still fits on the window's screen (title bar and a margin left over).
    static func maxContentHeight(for window: NSWindow) -> CGFloat {
        guard let screen = window.screen ?? NSScreen.main else { return 10_000 }
        let probe = NSRect(x: 0, y: 0, width: 100, height: 100)
        let titleBar = window.frameRect(forContentRect: probe).height - probe.height
        return screen.visibleFrame.height - titleBar - 40
    }

    /// Menu-bar apps are never active on their own: bring the window (and us) to the front.
    static func present(_ window: NSWindow) {
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    static var appIcon: NSImage {
        return NSApp.applicationIconImage ?? NSImage(size: NSSize(width: 64, height: 64))
    }

    static var appVersion: String {
        let v = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        if let v = v, !v.isEmpty { return v }
        return "—"
    }
}

/// After a window saved the settings: makes sure the saved shortcut is the one we hold.
@MainActor
enum HotkeyRefresh {
    /// (Re)registers `AppSettings.shared.hotkey` unless paused, when it changed or isn't registered (e.g. it was
    /// taken by another app at launch and is free now). Returns false when another app still owns it.
    @discardableResult
    static func applySaved() -> Bool {
        let settings = AppSettings.shared
        if settings.paused { return true }
        let hk = settings.hotkey
        let center = HotkeyCenter.shared
        if center.isRegistered, let current = center.current, current == hk { return true }
        return center.register(hk)
    }
}
