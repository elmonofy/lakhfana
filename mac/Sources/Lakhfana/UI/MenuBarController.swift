import AppKit

/// The menu-bar icon and its menu: الإعدادات… / إيقاف مؤقت ⇄ تشغيل / — / خروج من لخفنه.
@MainActor
final class MenuBarController: NSObject {
    var onOpenSettings: (() -> Void)?

    private let statusItem: NSStatusItem
    private let pauseItem: NSMenuItem

    override init() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        pauseItem = NSMenuItem(title: "إيقاف مؤقت", action: nil, keyEquivalent: "")
        super.init()

        if let button = statusItem.button {
            button.image = ImageFactory.statusIcon()
            button.imagePosition = .imageOnly
            button.toolTip = "لخفنه"
        }

        let menu = NSMenu()
        menu.userInterfaceLayoutDirection = .rightToLeft
        menu.autoenablesItems = false

        let settingsItem = NSMenuItem(title: "الإعدادات…", action: #selector(openSettingsClicked), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)

        pauseItem.action = #selector(togglePauseClicked)
        pauseItem.target = self
        menu.addItem(pauseItem)

        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: "خروج من لخفنه", action: #selector(quitClicked), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        statusItem.menu = menu
    }

    func update(paused: Bool, hotkey: Hotkey) {
        pauseItem.title = paused ? "تشغيل" : "إيقاف مؤقت"
        guard let button = statusItem.button else { return }
        button.appearsDisabled = paused
        if paused {
            button.toolTip = "لخفنه — متوقف مؤقتاً"
        } else if hotkey.isEmpty {
            button.toolTip = "لخفنه"
        } else {
            button.toolTip = "لخفنه — " + hotkey.displayString
        }
    }

    /// Screen frame of the menu-bar icon (used to place toasts under it).
    func anchorRect() -> NSRect? {
        return statusItem.button?.window?.frame
    }

    @objc private func openSettingsClicked() {
        onOpenSettings?()
    }

    @objc private func togglePauseClicked() {
        AppSettings.shared.paused.toggle()
    }

    @objc private func quitClicked() {
        NSApp.terminate(nil)
    }
}

/// Images drawn in code (no asset catalog in a SwiftPM app). Not actor-bound: drawing handlers may run anywhere.
enum ImageFactory {
    /// 18 pt monochrome ⇄ template image; macOS tints it for light/dark menu bars.
    static func statusIcon() -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            NSColor.black.setStroke()

            // top arrow →
            let top = NSBezierPath()
            top.lineWidth = 1.7
            top.lineCapStyle = .round
            top.lineJoinStyle = .round
            top.move(to: NSPoint(x: 2.5, y: 12.5))
            top.line(to: NSPoint(x: 15.5, y: 12.5))
            top.move(to: NSPoint(x: 12.2, y: 15.6))
            top.line(to: NSPoint(x: 15.5, y: 12.5))
            top.line(to: NSPoint(x: 12.2, y: 9.4))
            top.stroke()

            // bottom arrow ←
            let bottom = NSBezierPath()
            bottom.lineWidth = 1.7
            bottom.lineCapStyle = .round
            bottom.lineJoinStyle = .round
            bottom.move(to: NSPoint(x: 15.5, y: 5.5))
            bottom.line(to: NSPoint(x: 2.5, y: 5.5))
            bottom.move(to: NSPoint(x: 5.8, y: 8.6))
            bottom.line(to: NSPoint(x: 2.5, y: 5.5))
            bottom.line(to: NSPoint(x: 5.8, y: 2.4))
            bottom.stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "لخفنه"
        return image
    }

    /// Stretchable rounded-rect mask: gives an NSVisualEffectView round corners and a matching shadow.
    static func roundedMask(radius: CGFloat) -> NSImage {
        let edge = radius * 2 + 1
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }
}
