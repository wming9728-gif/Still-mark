# InkPage

**English** · [简体中文](README.zh-CN.md)

A local Markdown reader and editor for Windows 11. It runs completely offline — no account, network or paid service needed. The interface is available in English and Simplified Chinese.

![Split view](docs/screenshots/split-en.png)

## Run it

Build the portable program from source (see [Build from source](#build-from-source)); it ends up in `dist\InkPage\InkPage.exe`. Open [docs/example.md](docs/example.md) for a quick tour.

- Portable: nothing to install, and no .NET installation needed (the runtime is bundled in the exe).
- The preview uses the Microsoft Edge WebView2 Runtime, which ships with Windows 11. If it is missing, editing and saving still work and the preview area explains what is needed.
- Opening files: drag a `.md` file onto the window (editor or preview), use Menu → Open…, or run `InkPage.exe <path>`.
- InkPage never writes to the registry and does not register file types, so it is **not** in the "Open with" list at first. Add it once:
  1. In File Explorer, right-click any `.md` file → "Open with" → "Choose another app".
  2. Scroll to the bottom, click "Choose an app on your PC" and select `InkPage.exe`.
  3. "Always" makes InkPage the default for `.md` files (double-click opens them in InkPage); "Just once" only uses it this time.

  After that, InkPage stays in the "Open with" list. If you move the InkPage folder later, select it again the same way.

## Interface language

- By default InkPage follows the Windows display language: Chinese on Simplified Chinese systems, English everywhere else (Traditional Chinese systems currently get English too).
- Menu → `Language / 语言` switches between "Use System Language", English and 简体中文. The change applies **immediately**, without a restart, and is remembered.
- `--lang en` / `--lang zh-Hans` on the command line picks the language for one session only (the saved choice is unchanged).
- The Open / Save file dialogs are drawn by Windows and always use the system language; the print dialog uses the language InkPage started with.

## Using InkPage

| Area | What it does |
| --- | --- |
| `Read / Split / Edit` (top centre) | Three modes. Split = edit the source on the left with a live preview on the right; both sides scroll together |
| Outline (left) | Click a heading to jump to it. Shown at every start and whenever a file is opened; hide it for the session with the button at the top right or Ctrl+Shift+O. It is narrower in Split mode to leave room for both panes, and steps aside automatically on narrow windows (below about 900 px in Split mode, 700 px otherwise) |
| Toolbar above the editor | Bold, italic, strikethrough, inline code, heading level, lists, task list, quote, code block, link, image, table, horizontal rule |
| Right-click menus | Editor: undo, redo, cut, copy, paste, select all, common formatting, find, replace. Preview: copy, select all, copy image / link |
| Top right | Outline toggle, find, theme (system / light / dark) |
| Status bar | Caret position, word count, zoom, line endings (LF / CRLF / mixed), encoding (UTF-8 / UTF-8 BOM / UTF-16 / GB18030) |
| ≡ menu (top left) | File commands, recent files, edit, view, language, file format conversion, export to HTML, print, about. View → "Code Ligatures in Preview" is off by default, so code reads exactly as written (`<=` is not drawn as `≤`); turn it on if you like ligatures |

Keyboard shortcuts:

| Shortcut | Action | Shortcut | Action |
| --- | --- | --- | --- |
| Ctrl+N / Ctrl+Shift+N | New / new window | Ctrl+F / Ctrl+H | Find / replace |
| Ctrl+O | Open | F3 / Shift+F3 | Next / previous match |
| Ctrl+S / Ctrl+Shift+S | Save / save as | Ctrl+1 / 2 / 3 | Read / Split / Edit |
| Ctrl+Z / Ctrl+Y | Undo / redo | Ctrl+B / Ctrl+I | Bold / italic |
| Ctrl+K / Ctrl+Shift+K | Link / code block | Ctrl+Alt+1…6 | Heading level |
| Ctrl+` | Inline code | Ctrl+= / Ctrl+- / Ctrl+0 (or Ctrl+wheel) | Zoom in / out / reset |
| Ctrl+Shift+X | Strikethrough | Ctrl+Shift+O | Show / hide the outline |
| Ctrl+P | Print | Esc | Close the find bar |
| Enter | Continues lists, tasks and quotes; Enter on an empty item ends the list | Shift+Enter | Plain line break |

In the find box, Enter / Shift+Enter also move to the next / previous match — handy if another program has taken over F3.

### File safety (the main design goal)

- No change, no write: saving an unedited document does not touch the file (same bytes, same modification time).
- The original format is kept: encoding, BOM and every line's own line ending (LF / CRLF / mixed); new and pasted line breaks follow the line they are on.
- Rendering is display-only: the preview never writes anything back to the source.
- Safe writes: temporary file in the same folder → flush to disk → byte-for-byte check → atomic replace → check again. If any step fails, the original file is left as it was.
- Conflict protection: changes or deletion by other programs are reported; unsaved work is never silently overwritten, and overwriting needs an explicit confirmation.
- Files whose encoding cannot be identified safely (broken UTF-8, binary, UTF-32, …) open read-only and can only be saved under a new name.
- Unsaved work is written to a recovery draft every 8 seconds; after a crash or a failed save, InkPage offers to restore it at the next start.

### Data folder

Settings, recent files, recovery drafts and the preview cache are kept in `data\` next to `InkPage.exe` (portable). If that folder is not writable (for example under Program Files), `%LOCALAPPDATA%\InkPage` is used instead.

## Project layout

```
InkPage/
├─ src/
│  ├─ InkPage.Core/         UI-independent core: encoding detection, safe writes, session / conflict detection, drafts, Markdown rendering
│  │  └─ Resources/         core messages (CoreStrings.resx English / CoreStrings.zh-Hans.resx Chinese)
│  └─ InkPage/              WPF desktop app: main window, editor extensions, themes, dialogs, preview page (web/)
│     └─ Localization/      UI strings (Strings.resx English / Strings.zh-Hans.resx Chinese) and language switching
├─ tools/                   packaging, icon generation, offline front-end asset download
├─ docs/                    example documents and screenshots
├─ README.md / README.zh-CN.md
└─ InkPage.sln
```

### Translations

All interface text lives in resource files; the code itself contains no text in either language:

- `src/InkPage/Localization/Strings.resx` (English, also the fallback) and `Strings.zh-Hans.resx` (Simplified Chinese);
- `src/InkPage.Core/Resources/CoreStrings.resx` and `CoreStrings.zh-Hans.resx` (file-handling messages).

Both languages must have the same entries with the same placeholders (such as `{0}`). Adding a language means adding `Strings.<culture>.resx` and `CoreStrings.<culture>.resx` and registering the language in `Localization/Loc.cs` and the menu.

## Build from source

Requires the .NET 10 SDK.

```bash
dotnet build InkPage.sln
```

Portable release build (self-contained single file in `dist\InkPage`; an existing `dist\InkPage\data` folder is kept):

```bash
powershell -ExecutionPolicy Bypass -File tools/publish.ps1
```

InkPage has an automation channel for GUI testing. It only exists when the program is started with `--automation <pipe name>` (a named pipe restricted to the current user); a normal start never opens it.

## Technology and third-party components

| Component | Used for | License |
| --- | --- | --- |
| .NET 10 / WPF | Desktop app framework | MIT |
| AvalonEdit 6.3 | Source editor (IME, undo / redo, find, mixed line endings kept) | MIT |
| Markdig 1.4 | Markdown parsing (CommonMark + GFM tables / task lists / strikethrough / footnotes / alerts) | BSD-2-Clause |
| Microsoft Edge WebView2 | Reading view and preview rendering | Microsoft |
| highlight.js 11.12 | Code highlighting (bundled offline) | BSD-3-Clause |
| JetBrains Mono | Code font in the preview (bundled offline) | OFL-1.1 |

Typography: a clean sans-serif layout with GitHub-style tables and code blocks, 16 px text at 1.75 line height, light and dark themes. The reading sheet widens with the window — 1040 px in a normal window, up to 1360 px (text column up to 1180 px) — so a maximised window is not mostly margin.
