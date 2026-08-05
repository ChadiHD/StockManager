# SMDesktopUI.UITests

FlaUI (UI Automation) tests that drive the real **SMDesktopUI** WPF window. Playwright cannot
automate WPF; FlaUI talks to Windows UI Automation, so it can.

## What it covers

- **`SignInButton_IsDisabled_UntilEmailAndPasswordEntered`** — Bug 1 regression. Launches the app,
  asserts the Sign in button starts disabled, types an email + password, and asserts the button
  becomes enabled. This only passes when the `PasswordBox` is actually bound to
  `LoginViewModel.Password` (so the `CanLogIn` guard re-evaluates).
- **`ShellMenu_HeadersPresent_AndCaptureSavedForVisualReview`** — Bug 2. Asserts the `Operations`
  and `File` menu headers exist, opens the `File` dropdown, and saves a full-screen screenshot to
  `bin/<Config>/net10.0-windows/TestArtifacts/shell-menu.png`. Font / contrast / overlap are
  visual and can't be asserted through UI Automation — review that PNG by eye.

## Requirements

- Runs on an **interactive Windows desktop session** (not a headless/service CI agent).
- **SMDesktopUI must be built first**, and its exe must not be locked by a running instance.

## Run

```bash
dotnet build SMDesktopUI/SMDesktopUI.csproj -c Debug
```

```bash
dotnet test SMDesktopUI.UITests/SMDesktopUI.UITests.csproj -c Debug
```

The exe is located by walking up to `StockManager.sln` then into
`SMDesktopUI/bin/<Config>/net10.0-windows7.0/SMDesktopUI.exe`. Override with the
`SMDESKTOPUI_EXE` environment variable if your layout differs.
