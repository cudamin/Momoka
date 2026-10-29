// Inert accessibility boundary: the dialog only sets a screen-reader name; the fixture never reads the UIA tree.
namespace Microsoft.UI.Xaml.Automation;

public static class AutomationProperties
{
    public static void SetName(object element, string name) { }
}
