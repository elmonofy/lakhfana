import AppKit

/// Small rounded HUD message under the menu bar (the Windows balloon tip). Never takes focus or mouse clicks,
/// fades out after 2.5 s. A new message replaces the one on screen.
@MainActor
final class Toast: NSObject {
    static let shared = Toast()

    /// Screen rect of our menu-bar icon, so the toast appears right under it (left side on RTL systems).
    var anchorProvider: (() -> NSRect?)?

    private var panel: NSPanel?
    private var label: NSTextField?
    private var iconView: NSImageView?
    private var hiding = false

    private let padH: CGFloat = 14
    private let padV: CGFloat = 11
    private let iconSize: CGFloat = 22
    private let gap: CGFloat = 10
    private let visibleSeconds: TimeInterval = 2.5

    func show(_ message: String) {
        let text = message.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        let p = panel ?? makePanel()
        guard let label = label, let iconView = iconView else { return }

        // ---- measure (RTL, wrapped, max width ~360pt) ----
        let font = NSFont.systemFont(ofSize: 13, weight: .medium)
        let para = NSMutableParagraphStyle()
        para.alignment = .right
        para.baseWritingDirection = .rightToLeft
        para.lineBreakMode = .byWordWrapping
        let attributed = NSAttributedString(string: text, attributes: [
            .font: font,
            .paragraphStyle: para,
            .foregroundColor: NSColor.labelColor,
        ])
        let maxText = Theme.toastMaxWidth - 2 * padH - iconSize - gap
        let measured = attributed.boundingRect(with: NSSize(width: maxText - 6, height: 2000),
                                               options: [.usesLineFragmentOrigin, .usesFontLeading])
        let textW = min(maxText, ceil(measured.width) + 6)
        let textH = ceil(measured.height) + 4
        let contentH = max(iconSize, textH)
        let width = 2 * padH + textW + gap + iconSize
        let height = 2 * padV + contentH

        // ---- lay out: icon on the start (right) side, text next to it ----
        label.attributedStringValue = attributed
        label.frame = NSRect(x: padH, y: padV + (contentH - textH) / 2, width: textW, height: textH)
        iconView.frame = NSRect(x: width - padH - iconSize, y: padV + (contentH - iconSize) / 2,
                                width: iconSize, height: iconSize)
        let size = NSSize(width: width, height: height)
        p.setFrame(NSRect(origin: origin(for: size), size: size), display: true)

        // ---- show ----
        NSObject.cancelPreviousPerformRequests(withTarget: self)
        hiding = false
        if !p.isVisible { p.alphaValue = 0 }
        p.orderFrontRegardless()
        p.invalidateShadow()
        let target = p
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.18
            target.animator().alphaValue = 1
        }, completionHandler: nil)
        perform(#selector(fadeOut), with: nil, afterDelay: visibleSeconds)
    }

    @objc private func fadeOut() {
        guard let p = panel else { return }
        hiding = true
        let target = p
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.3
            target.animator().alphaValue = 0
        }, completionHandler: nil)
        perform(#selector(hideNow), with: nil, afterDelay: 0.35)
    }

    @objc private func hideNow() {
        guard hiding, let p = panel else { return }
        hiding = false
        p.orderOut(nil)
    }

    // MARK: Position

    private func origin(for size: NSSize) -> NSPoint {
        let anchor: NSRect? = anchorProvider?()
        var screen: NSScreen?
        if let a = anchor {
            let mid = NSPoint(x: a.midX, y: a.midY)
            screen = NSScreen.screens.first(where: { $0.frame.contains(mid) })
        }
        if screen == nil {
            let mouse = NSEvent.mouseLocation
            screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) })
        }
        if screen == nil { screen = NSScreen.main ?? NSScreen.screens.first }
        guard let target = screen else { return NSPoint(x: 80, y: 80) }

        let vf = target.visibleFrame
        var x: CGFloat
        if let a = anchor, target.frame.contains(NSPoint(x: a.midX, y: a.midY)) {
            x = a.midX - size.width / 2
        } else if Toast.systemIsRightToLeft {
            x = vf.minX + 12
        } else {
            x = vf.maxX - size.width - 12
        }
        x = min(max(x, vf.minX + 8), vf.maxX - size.width - 8)
        let y = vf.maxY - size.height - 8
        return NSPoint(x: x, y: y)
    }

    /// On an Arabic/Hebrew system the menu bar is mirrored and status items sit on the left.
    private static var systemIsRightToLeft: Bool {
        guard let lang = Locale.preferredLanguages.first else { return false }
        return NSLocale.characterDirection(forLanguage: lang) == .rightToLeft
    }

    // MARK: Panel

    private func makePanel() -> NSPanel {
        let start = NSRect(x: 0, y: 0, width: 240, height: 44)
        let p = NSPanel(contentRect: start,
                        styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered,
                        defer: true)
        p.level = .statusBar
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.ignoresMouseEvents = true
        p.hidesOnDeactivate = false
        p.isReleasedWhenClosed = false
        p.becomesKeyOnlyIfNeeded = true
        p.isMovable = false
        p.animationBehavior = .none
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]

        let fx = NSVisualEffectView(frame: start)
        fx.material = .hudWindow
        fx.blendingMode = .behindWindow
        fx.state = .active
        fx.maskImage = ImageFactory.roundedMask(radius: 12)
        fx.autoresizingMask = [.width, .height]
        p.contentView = fx

        let tf = NSTextField(wrappingLabelWithString: "")
        tf.isEditable = false
        tf.isSelectable = false
        tf.isBezeled = false
        tf.drawsBackground = false
        tf.alignment = .right
        tf.baseWritingDirection = .rightToLeft
        tf.maximumNumberOfLines = 0
        fx.addSubview(tf)

        let iv = NSImageView(frame: NSRect(x: 0, y: 0, width: iconSize, height: iconSize))
        iv.image = NSApp.applicationIconImage
        iv.imageScaling = .scaleProportionallyUpOrDown
        fx.addSubview(iv)

        panel = p
        label = tf
        iconView = iv
        return p
    }
}
