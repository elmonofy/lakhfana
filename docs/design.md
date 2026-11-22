# لخفنه (Lakhfana) — Design & Interface Contract

## What it is
A Windows tray app. User typed text in the wrong keyboard layout (e.g. `hgsghl ugd;l` instead of `السلام عليكم`).
They select it (Ctrl+A or mouse) and press a global hotkey; the app replaces the selection with the same keystrokes
rendered in the other installed layout. If nothing is selected, it selects the current line and converts it.
It is meant for public distribution, so it must be polished, self-installing, and robust.

## Decisions (made with the user)
- Distributed to the public → polished UI, icon, install/uninstall, start with Windows.
- No selection → auto-select current line (Home, Shift+End) and convert.
- UI language: **Arabic only**, RTL, friendly Egyptian-colloquial tone (the brand name is colloquial). Short sentences.
- Hotkey chosen in a first-run (onboarding) window; changeable in Settings. Default suggestion: **Ctrl + Shift + Space**.

## Tech constraints (hard)
- No .NET SDK on the machine. Build with the in-box compiler:
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` → **C# 5 ONLY**, .NET Framework 4.8, WinForms.
  Forbidden syntax: `$"..."` interpolation, `?.`, `?[`, `nameof`, expression-bodied members (`=>` on members),
  auto-property initializers, `out var`, pattern matching, tuples, `using static`, local functions, `catch when`, `default` literal.
  Lambdas and LINQ are fine. Public fields are fine.
- Always compile with `/codepage:65001` (sources are UTF-8, contain Arabic strings).
- Output: single file `dist\Lakhfana.exe`, zero dependencies (works on any Win10/11).
- Namespace: `Lakhfana`. Code/identifiers in English; all user-visible strings Arabic.
- Each file declares its own P/Invoke signatures privately (e.g. `static class XNative` nested/internal in that file)
  — EXCEPT the core `Native` class, which core files share. Never declare the same top-level type name twice.
- Installed layouts on dev machine: `00000409` (en-US), `00000401` (Arabic 101).

## Project layout & ownership
```
build.ps1                 [shell]  compiles everything → dist\Lakhfana.exe ; also builds tests → dist\tests\*.exe
app.manifest              [shell]  asInvoker, Win10 supportedOS, dpiAware true (system), Common-Controls v6
src/Program.cs            [shell]
src/AssemblyInfo.cs       [shell]  title "لخفنه", version 1.0.0.0
src/App/Settings.cs       [shell]
src/App/Hotkey.cs         [shell]
src/App/HotkeyManager.cs  [shell]
src/App/Installer.cs      [shell]
src/App/SingleInstance.cs [shell]
src/App/TrayApp.cs        [shell]
src/Core/Native.cs        [core]
src/Core/KeyboardLayouts.cs [core]
src/Core/LayoutMap.cs     [core]
src/Core/Converter.cs     [core]
src/Core/EnglishScorer.cs [core]
src/Core/ClipboardHelper.cs [core]
src/Core/InputSim.cs      [core]
src/Core/SwitchEngine.cs  [core]
tests/ConverterTests.cs   [core]   console exe, exits non-zero on failure
src/UI/Theme.cs           [ui]
src/UI/HotkeyBox.cs       [ui]
src/UI/OnboardingForm.cs  [ui]
src/UI/SettingsForm.cs    [ui]
tools/make-icon.ps1       [ui]     generates assets/app.ico (16,20,24,32,40,48,64,256)
tests/UiSnapshot.cs       [ui]     console exe: builds each form, DrawToBitmap → dist\snapshots\*.png
```

## Public contract (exact signatures; all in namespace Lakhfana)

### Shell-owned
```csharp
public struct Hotkey {
    public const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8;
    public int Modifiers;   // MOD_* flags
    public int Vk;          // virtual-key code
    public Hotkey(int modifiers, int vk);
    public bool IsEmpty { get; }             // Vk == 0
    public string ToDisplayString();          // "Ctrl + Shift + Space" (Latin key names; order Ctrl, Alt, Shift, Win, key)
    public string Serialize();                // "6:32"
    public static Hotkey Parse(string s);     // inverse; returns default(Hotkey) on bad input
    public static Hotkey Default { get; }     // Ctrl+Shift+Space
}

public sealed class Settings {
    public Hotkey Hotkey;               // default Hotkey.Default
    public string LayoutA;              // HKL as hex string e.g. "04090409"; null/"" = auto-pick
    public string LayoutB;
    public bool SwitchLayoutAfter = true;  // switch the target window's keyboard to the output language
    public bool SelectLineIfEmpty = true;
    public bool StartWithWindows = true;
    public bool FirstRunDone;
    public bool Paused;
    public static string Folder { get; }   // %APPDATA%\Lakhfana
    public static Settings Load();          // never throws; defaults on missing/corrupt
    public void Save();                     // key=value UTF-8 file settings.ini, atomic write (temp + replace)
}

public sealed class HotkeyManager : IDisposable {   // NativeWindow-based; events raised on UI thread
    public event EventHandler Pressed;
    public Hotkey Current { get; }
    public bool Register(Hotkey hk);        // unregisters previous; MOD_NOREPEAT; false if taken
    public void Unregister();
    public void Suspend();                  // temporarily unregister (while user records a new hotkey)
    public void Resume();                   // re-register Current if not paused
    public static bool IsAvailable(Hotkey hk); // try-register on a temp window, then release
    public void Dispose();
}

public static class Installer {
    public static string InstallDir { get; }      // %LOCALAPPDATA%\Programs\Lakhfana
    public static string InstalledExe { get; }    // InstallDir\Lakhfana.exe
    public static string Version { get; }         // "1.0.0"
    public static bool IsRunningFromInstallDir();
    public static bool IsInstalled();              // HKCU uninstall key exists AND exe exists
    public static void Install(bool startWithWindows); // copy self → InstalledExe (overwrite), Start-Menu shortcut "لخفنه.lnk"
                                                       // (IShellLinkW COM interop), HKCU Uninstall entry, Run key per flag
    public static void SetStartWithWindows(bool on);   // HKCU\...\Run "Lakhfana" = "\"InstalledExe\""
    public static void Uninstall();                    // removes all of the above + settings folder; schedules self-delete
}
```
Program flow (Program.cs): `/uninstall` → Arabic confirm → close running instance → Installer.Uninstall → done message.
`/portable` → skip all install logic (dev/testing). Single instance (mutex `Local\Lakhfana_SingleInstance`; named events
`Local\Lakhfana_ShowSettings`, `Local\Lakhfana_Exit`). Second launch of the installed exe → signal ShowSettings and exit.
Launch from elsewhere (downloaded exe): if not installed → Onboarding (install mode) → Install → start installed exe → exit;
if already installed → signal Exit to the running copy, wait ≤3 s, Install (update, keep settings) → start installed exe → exit.
Running from install dir with `FirstRunDone == false` → Onboarding, then tray.

TrayApp (ApplicationContext): NotifyIcon (icon = `Icon.ExtractAssociatedIcon(Application.ExecutablePath)`), tooltip
`"لخفنه — " + hotkey display`, RTL ContextMenuStrip: الإعدادات / إيقاف مؤقت (toggle ⇄ تشغيل) / — / خروج. Double-click → settings.
Hotkey Pressed → `engine.Trigger()`. Engine.Notify → marshal to UI thread → balloon tip. Applies settings after
Onboarding/Settings close: re-register hotkey, Installer.SetStartWithWindows (only when installed), engine.ReloadLayouts().
If the hotkey can't be registered at startup → balloon telling the user to choose another one, open Settings.

### Core-owned
```csharp
public sealed class LayoutInfo {
    public IntPtr Hkl;
    public string Key;          // hex HKL string used in Settings, e.g. "04010401" (IntPtr.ToInt64().ToString("X8"))
    public string DisplayName;  // CultureInfo(langId).NativeName, e.g. "العربية (مصر)", "English (United States)"
    public int LangId;          // low word of HKL
}
public static class KeyboardLayouts {
    public static List<LayoutInfo> GetInstalled();              // GetKeyboardLayoutList
    public static LayoutInfo Find(List<LayoutInfo> all, string key); // null if absent
    public static void PickPair(List<LayoutInfo> all, string keyA, string keyB, out LayoutInfo a, out LayoutInfo b);
        // use saved keys if installed; else prefer Arabic(lang 0x01) + English(lang 0x09); else first two; b may be null if <2 layouts
}
public sealed class Converter {
    public Converter(IntPtr hklA, IntPtr hklB);
    public string Convert(string text, IntPtr fallbackSourceHkl, out IntPtr targetHkl);
}
public sealed class SwitchEngine {
    public SwitchEngine(Settings settings);   // keeps the reference; reads it at each Trigger
    public event Action<string> Notify;        // Arabic user message; raised on a worker thread
    public void Trigger();                     // call on UI thread; returns immediately; ignored if a run is in progress
    public void ReloadLayouts();
}
```

### UI-owned
```csharp
public static class Theme { /* colors, fonts, public static int S(int px) DPI scaling helper */ }
public sealed class HotkeyBox : Control {
    public Hotkey Value { get; set; }
    public event EventHandler ValueChanged;
    public event EventHandler RecordingStarted;  // owner calls HotkeyManager.Suspend()
    public event EventHandler RecordingEnded;    // owner calls HotkeyManager.Resume()
}
public sealed class OnboardingForm : Form {
    public OnboardingForm(Settings settings, HotkeyManager hotkeys /* may be null */, bool installMode);
    // "يلا نبدأ": validate (hotkey not empty & HotkeyManager.IsAvailable, two different layouts), write values into
    // settings, settings.FirstRunDone = true, settings.Save(), DialogResult = OK. Closing with X → DialogResult.Cancel.
}
public sealed class SettingsForm : Form {
    public SettingsForm(Settings settings, HotkeyManager hotkeys);
    public event EventHandler Applied;            // after Save() — TrayApp re-applies
    public event EventHandler UninstallRequested; // TrayApp runs the uninstall flow
}
```

## Core behaviour (SwitchEngine.Trigger)
1. If Alt is physically down, immediately tap unassigned VK 0xE8 (prevents Alt-release from opening the target's menu bar).
2. Worker STA thread: wait until Ctrl/Shift/Alt/Win are physically released (GetAsyncKeyState, ≤1500 ms; after that inject key-ups).
3. Capture foreground hwnd + its thread's HKL. If target process is elevated and we are not → Notify (UIPI blocks input) and stop.
4. Back up the clipboard (every format that can be read; best effort; retries on busy clipboard).
5. Remember GetClipboardSequenceNumber, SendInput Ctrl+C, poll ≤ 400 ms for change. Treat as "no selection" when nothing
   changed, text empty, or the copy is an editor's empty-selection line copy (VS Code format `vscode-editor-data` with
   `"isFromEmptySelection":true`, Visual Studio `MSDEVLineSelect` / `VisualStudioEditorOperationsLineCutCopyClipboardTag`).
6. No selection: if SelectLineIfEmpty → SendInput Home, Shift+End, retry copy once; else Notify "حدد الكلام الأول" and restore.
7. Convert. If result == original → restore clipboard, Notify nothing to convert, stop.
8. Put result on the clipboard as Unicode text plus `ExcludeClipboardContentFromMonitorProcessing` and
   `CanIncludeInClipboardHistory`=0 (keeps Win+V history clean), SendInput Ctrl+V, wait ~300 ms, restore backup.
9. If SwitchLayoutAfter → PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, 0, targetHkl).
All SendInput keystrokes use proper scan codes and extended flags (Home/End are extended keys).

## Converter algorithm
- For each layout build key map via ToUnicodeEx (wFlags = 4, scan code from MapVirtualKeyEx) for VK 0x20..0xFE ×
  shift states {none, Shift}; skip dead keys (flush with a second call), control chars, and results that are empty.
  Output may be multi-char (Arabic 101: B → "لا", Shift+B → "لآ", Shift+G → "لأ", Shift+T → "لإ").
- Reverse map per layout: text (1–3 chars) → keystroke; prefer the unshifted keystroke when duplicated.
- Split text into words on whitespace (whitespace/newlines preserved exactly).
  Direction per word: score = #chars found only in A's outputs vs only in B's outputs. Majority wins; tie/zero → the
  whole-text majority; still zero → source = `fallbackSourceHkl` if it is A or B, else A.
- Convert a word: walk left→right; at each position the source layout may match a multi-char key output (e.g. "لا") as
  well as single chars. When several segmentations exist and the target layout's letters are Latin, pick the one with
  the best English letter-bigram log-probability (EnglishScorer, embedded 26×26 table, case-insensitive);
  otherwise prefer the longest match. Chars not in the source map pass through unchanged.
- targetHkl = target layout of the majority of converted chars.
Required test cases (tests/ConverterTests.cs, real layouts 0409 and 0401 loaded via LoadKeyboardLayout):
`"hgsghl ugd;l"` → `"السلام عليكم"`; `"شلاخعف"` → `"about"`; `"مهلاف"` → `"light"`; `"لامشؤن"` → `"black"`;
whitespace/newlines preserved; mixed-direction words; pure-digit text unchanged; random keystroke round-trip property
(render random keystrokes in both layouts; converting the English rendering must give exactly the Arabic rendering).

## UI
- RTL (`RightToLeft.Yes`, `RightToLeftLayout = true`), font Segoe UI 10pt, clean white surface, one accent colour,
  generous spacing, flat custom-drawn primary button, DPI-scaled via Theme.S. No default-WinForms grey look.
- Onboarding: icon + "أهلاً بيك في لخفنه", one-line explanation with a live example (`hgsghl ugd;l` ← `السلام عليكم`),
  HotkeyBox ("دوس الاختصار اللي يريحك"), language pair (two dropdowns only if >2 layouts installed, else a read-only line),
  checkboxes: يشتغل مع الويندوز / يغيّر لغة الكيبورد بعد التحويل / لو مفيش كلام متحدد، يحدد السطر لوحده;
  primary button "يلا نبدأ" (install mode: same label, installs). Inline Arabic validation messages, no message boxes.
- Settings: same controls + version + small "إلغاء تثبيت البرنامج" link + save/cancel.

## Known limitations (documented, not solved)
- Apps running as administrator can't receive input from a non-elevated app (UIPI) → we tell the user.
- Exe is unsigned → SmartScreen warning on first download until it's code-signed.
