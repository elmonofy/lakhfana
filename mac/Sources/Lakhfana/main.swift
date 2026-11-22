import AppKit

// Menu-bar app: no Dock icon, no main window. LSUIElement in Info.plist does the same for the bundle;
// the explicit accessory policy also covers running the bare executable during development.
MainActor.assumeIsolated {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    _ = app.setActivationPolicy(.accessory)
    withExtendedLifetime(delegate) {
        app.run()
    }
}
