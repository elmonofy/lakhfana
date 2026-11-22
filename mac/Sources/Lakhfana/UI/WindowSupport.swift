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

    static func setContent<V: View>(_ root: V, of window: NSWindow) {
        let host = NSHostingView(rootView: root)
        window.contentView = host
        let size = host.fittingSize
        if size.width > 10 && size.height > 10 {
            window.setContentSize(size)
        }
        window.center()
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
