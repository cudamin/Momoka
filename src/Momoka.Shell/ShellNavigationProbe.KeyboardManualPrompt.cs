using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Momoka.Shell;

/// <summary>Visible guidance belongs only to the manual inspect fixture, outside the product shell.</summary>
internal sealed class KeyboardManualPrompt
{
    internal Grid Surface { get; }
    private readonly TextBlock _instruction;

    internal KeyboardManualPrompt(Action cancel)
    {
        Surface = new Grid
        {
            Padding = new Thickness(12, 6, 12, 6),
            Background = (Brush)Application.Current.Resources["EgSurfaceBrush"],
            ColumnSpacing = 12
        };
        Surface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Surface.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _instruction = new TextBlock
        {
            Style = (Style)Application.Current.Resources["EgBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_instruction, "KeyboardManualInstruction");
        AutomationProperties.SetLiveSetting(_instruction, AutomationLiveSetting.Polite);
        AutomationProperties.SetHeadingLevel(_instruction, AutomationHeadingLevel.Level1);
        var stop = new Button { Content = "取消验收（Ctrl+Shift+Q）", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(stop, "KeyboardManualCancel");
        AutomationProperties.SetName(stop, "取消手动键盘验收");
        stop.Click += (_, _) => cancel();
        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Q, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        accelerator.Invoked += (_, args) => { args.Handled = true; cancel(); };
        Surface.KeyboardAccelerators.Add(accelerator);
        Surface.Children.Add(_instruction);
        Grid.SetColumn(stop, 1);
        Surface.Children.Add(stop);
    }

    internal void Show(string stage, bool resumeFromSearch = false, bool resumeFromResult = false) =>
        _instruction.Text = KeyboardJourneyProbeRules.ManualInstruction(stage, resumeFromSearch, resumeFromResult);
}
