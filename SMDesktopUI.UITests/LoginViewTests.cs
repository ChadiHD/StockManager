using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace SMDesktopUI.UITests;

public class LoginViewTests
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The Sign in button is always enabled; validation runs on click. Asserts the button is
    /// enabled with an empty form and that clicking it surfaces a validation error message
    /// (bound to LoginViewModel.ErrorMessage) rather than doing nothing or crashing.
    /// </summary>
    [Fact]
    public void SignInButton_IsAlwaysEnabled_AndShowsValidationErrorForEmptyForm()
    {
        using var app = DesktopApp.Launch();
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, WindowTimeout)
            ?? throw new TimeoutException("SMDesktopUI main window did not appear.");
        window.SetForeground();

        var signIn = FindById(window, "LogIn").AsButton();

        // New behaviour: enabled even with empty fields.
        Assert.True(signIn.IsEnabled, "Sign in should always be enabled; validation happens on click.");

        // Invoke uses the InvokePattern (no foreground/focus dependency).
        signIn.Invoke();

        // The error Border is collapsed until validation fails, so wait until the ErrorMessage
        // element exists AND carries text.
        AutomationElement? error = null;
        Retry.WhileTrue(
            () =>
            {
                error = window.FindFirstDescendant(cf => cf.ByAutomationId("ErrorMessage"));
                return error is null || string.IsNullOrWhiteSpace(error.Name);
            },
            timeout: ElementTimeout,
            interval: TimeSpan.FromMilliseconds(150));

        Assert.True(error is not null && !string.IsNullOrWhiteSpace(error.Name),
            "Expected a validation message when signing in with an empty form.");

        app.Close();
    }

    /// <summary>
    /// Bug 2 coverage: the top menu headers must be present, and we open the File menu and save a
    /// full-screen screenshot to TestArtifacts so the font/contrast of the dropdown (previously
    /// white-on-white / wrong font) can be reviewed by eye. Colour/font are visual and cannot be
    /// asserted through UI Automation.
    /// </summary>
    [Fact]
    public void ShellMenu_HeadersPresent_AndCaptureSavedForVisualReview()
    {
        using var app = DesktopApp.Launch();
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, WindowTimeout)
            ?? throw new TimeoutException("SMDesktopUI main window did not appear.");

        var operations = Retry.WhileNull(
            () => window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Operations"))),
            ElementTimeout).Result;
        var file = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.MenuItem).And(cf.ByName("File")));

        Assert.True(operations is not null, "'Operations' menu header not found.");
        Assert.True(file is not null, "'File' menu header not found.");

        // Open the File dropdown so it appears in the capture, then snapshot the screen (the WPF
        // menu popup is a separate HWND, so a full-screen capture includes it).
        try { file!.AsMenuItem().Expand(); }
        catch { file!.Click(); }
        Thread.Sleep(500);

        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestArtifacts");
        Directory.CreateDirectory(artifacts);
        var pngPath = Path.Combine(artifacts, "shell-menu.png");
        Capture.Screen().ToFile(pngPath);

        Assert.True(File.Exists(pngPath), $"Expected a menu screenshot at {pngPath}.");

        app.Close();
    }

    private static AutomationElement FindById(Window window, string automationId)
    {
        var element = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            ElementTimeout).Result;

        return element ?? throw new TimeoutException($"Element with AutomationId '{automationId}' not found.");
    }
}
