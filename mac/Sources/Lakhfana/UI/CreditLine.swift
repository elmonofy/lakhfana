import AppKit
import SwiftUI

/// Author credit, same as the Windows app.
enum Credit {
    static let author = "Abdelrahman Elmonofy"
    static let site = "fullstack.elmonofy.dev"
    static let url = URL(string: "https://fullstack.elmonofy.dev")!
}

/// "Created by Abdelrahman Elmonofy · fullstack.elmonofy.dev" — small, muted, centered; the site is a link.
struct CreditLine: View {
    var body: some View {
        HStack(spacing: 6) {
            Text("Created by " + Credit.author)
                .foregroundColor(.secondary)
            Text("·")
                .foregroundColor(.secondary)
            Link(Credit.site, destination: Credit.url)
                .foregroundColor(Theme.accentText)
        }
        .font(Theme.tinyFont)
        .environment(\.layoutDirection, .leftToRight)
        .frame(maxWidth: .infinity, alignment: .center)
    }
}
