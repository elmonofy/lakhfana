import AppKit
import SwiftUI

// Small building blocks shared by the onboarding and settings windows (the Windows UiChips / UiStatus / rows).

enum StatusKind {
    case hint
    case ok
    case error
}

struct StatusLine: Equatable {
    var kind: StatusKind
    var text: String

    static func hint(_ text: String) -> StatusLine { return StatusLine(kind: .hint, text: text) }
    static func ok(_ text: String) -> StatusLine { return StatusLine(kind: .ok, text: text) }
    static func error(_ text: String) -> StatusLine { return StatusLine(kind: .error, text: text) }
}

/// One coloured status line with an icon: green = ok, red = problem, grey = hint.
struct StatusText: View {
    let line: StatusLine

    var body: some View {
        if !line.text.isEmpty {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Image(systemName: symbolName)
                    .font(.system(size: 11.5, weight: .semibold))
                Text(line.text)
                    .font(Theme.smallFont)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
            .foregroundColor(color)
        }
    }

    private var symbolName: String {
        switch line.kind {
        case .ok: return "checkmark.circle.fill"
        case .error: return "exclamationmark.circle.fill"
        case .hint: return "keyboard"
        }
    }

    private var color: Color {
        switch line.kind {
        case .ok: return Theme.success
        case .error: return Theme.danger
        case .hint: return Color.secondary
        }
    }
}

/// Rounded pill with a short text, e.g. the example "hgsghl ugd;l ← السلام عليكم" or a language name.
struct Chip: View {
    enum Style {
        case neutral
        case accent
        case muted
    }

    let text: String
    var style: Style = .neutral

    var body: some View {
        Text(text)
            .font(.system(size: 12.5, weight: style == .accent ? .semibold : .regular))
            .lineLimit(1)
            .truncationMode(.tail)
            .foregroundColor(foreground)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(fill, in: Capsule())
            .overlay(Capsule().strokeBorder(border, lineWidth: 1))
    }

    private var foreground: Color {
        switch style {
        case .accent: return Theme.accentText
        case .muted: return Color.secondary
        case .neutral: return Color.primary
        }
    }

    private var fill: Color {
        switch style {
        case .accent: return Theme.accentSoft
        case .muted: return Theme.cardBackground
        case .neutral: return Theme.keyFill
        }
    }

    private var border: Color {
        switch style {
        case .accent: return Theme.accent.opacity(0.35)
        case .muted: return Theme.keyBorder
        case .neutral: return Theme.cardBorder
        }
    }
}

/// A settings row: title + description on the start (right) side, an accessory on the end (left) side.
struct SettingRow<Accessory: View>: View {
    let title: String
    let subtitle: String
    let subtitleColor: Color
    let accessory: Accessory

    init(title: String, subtitle: String, subtitleColor: Color = Color.secondary,
         @ViewBuilder accessory: () -> Accessory) {
        self.title = title
        self.subtitle = subtitle
        self.subtitleColor = subtitleColor
        self.accessory = accessory()
    }

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(Theme.headingFont)
                    .fixedSize(horizontal: false, vertical: true)
                Text(subtitle)
                    .font(Theme.smallFont)
                    .foregroundColor(subtitleColor)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 12)
            accessory
        }
        .padding(.vertical, Theme.rowPaddingV)
    }
}

/// A row with a switch on the end side.
struct ToggleRow: View {
    let title: String
    let subtitle: String
    @Binding var isOn: Bool

    var body: some View {
        SettingRow(title: title, subtitle: subtitle) {
            Toggle("", isOn: $isOn)
                .labelsHidden()
                .toggleStyle(.switch)
                .tint(Theme.accent)
                .accessibilityLabel(Text(title))
        }
    }
}

/// Key caps such as  ⌃  ⇧  Space . Always laid out left-to-right like the keyboard legends.
struct KeyCaps: View {
    let keys: [String]
    var highlighted: Bool = false

    var body: some View {
        HStack(spacing: 4) {
            ForEach(keys.indices, id: \.self) { i in
                Text(keys[i])
                    .font(.system(size: 12.5, weight: .semibold))
                    .foregroundColor(highlighted ? Theme.accentText : Color.primary)
                    .padding(.horizontal, 7)
                    .frame(minWidth: 24, minHeight: 22)
                    .background(Theme.keyFill, in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                    .overlay(
                        RoundedRectangle(cornerRadius: 5, style: .continuous)
                            .strokeBorder(Theme.keyBorder, lineWidth: 1)
                    )
            }
        }
        .environment(\.layoutDirection, .leftToRight)
    }
}

/// The middle of a window (between header and footer). `height == nil`: laid out plainly, the window fits its
/// content. Otherwise a vertical scroll area of exactly that height, so the footer buttons stay on screen.
struct ScrollableMiddle<Content: View>: View {
    let height: CGFloat?
    let content: Content

    init(height: CGFloat?, @ViewBuilder content: () -> Content) {
        self.height = height
        self.content = content()
    }

    var body: some View {
        if let h = height {
            ScrollView(.vertical, showsIndicators: true) {
                content
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(height: h)
        } else {
            content
        }
    }
}
