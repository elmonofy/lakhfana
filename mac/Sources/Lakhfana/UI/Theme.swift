import AppKit
import SwiftUI

/// Visual constants shared by every window. Same palette as the Windows app (src/UI/Theme.cs):
/// one indigo accent (#4F46E5), white cards on a soft background, green/red status lines.
enum Theme {
    // MARK: Layout

    static let windowWidth: CGFloat = 540
    static let windowPadding: CGFloat = 24
    static let sectionSpacing: CGFloat = 18
    static let cardSpacing: CGFloat = 12
    static let cardPaddingH: CGFloat = 18
    static let rowPaddingV: CGFloat = 12
    static let cardRadius: CGFloat = 12
    static let toastMaxWidth: CGFloat = 360

    // MARK: Fonts

    static let titleFont = Font.system(size: 22, weight: .bold)
    static let headingFont = Font.system(size: 13, weight: .semibold)
    static let bodyFont = Font.system(size: 13)
    static let smallFont = Font.system(size: 11.5)
    static let tinyFont = Font.system(size: 11)

    // MARK: Colours

    static let accent = Color(red: 79.0 / 255.0, green: 70.0 / 255.0, blue: 229.0 / 255.0)

    static var accentNS: NSColor { return rgb(79, 70, 229) }
    /// Accent for text and small glyphs: lighter in dark mode so it stays readable.
    static var accentText: Color { return Color(nsColor: dynamic(light: rgb(79, 70, 229), dark: rgb(165, 180, 252))) }
    static var accentSoft: Color { return accent.opacity(0.12) }
    static var success: Color { return Color(nsColor: dynamic(light: rgb(15, 123, 69), dark: rgb(74, 222, 128))) }
    static var danger: Color { return Color(nsColor: dynamic(light: rgb(196, 43, 28), dark: rgb(248, 113, 113))) }

    static var windowBackground: Color { return Color(nsColor: .windowBackgroundColor) }
    static var cardBackground: Color { return Color(nsColor: .controlBackgroundColor) }
    static var fieldBackground: Color { return Color(nsColor: .textBackgroundColor) }
    static var cardBorder: Color { return Color.primary.opacity(0.09) }
    static var keyFill: Color { return Color.primary.opacity(0.05) }
    static var keyBorder: Color { return Color.primary.opacity(0.2) }

    static func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat) -> NSColor {
        return NSColor(srgbRed: r / 255.0, green: g / 255.0, blue: b / 255.0, alpha: 1)
    }

    static func dynamic(light: NSColor, dark: NSColor) -> NSColor {
        return NSColor(name: nil) { appearance in
            let match = appearance.bestMatch(from: [NSAppearance.Name.darkAqua, NSAppearance.Name.aqua])
            return match == NSAppearance.Name.darkAqua ? dark : light
        }
    }
}

// MARK: - Card

struct CardModifier: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(.horizontal, Theme.cardPaddingH)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.cardBackground, in: RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: Theme.cardRadius, style: .continuous)
                    .strokeBorder(Theme.cardBorder, lineWidth: 1)
            )
    }
}

extension View {
    /// White rounded card with a hairline border (the Windows UiCard).
    func card() -> some View {
        return modifier(CardModifier())
    }
}
