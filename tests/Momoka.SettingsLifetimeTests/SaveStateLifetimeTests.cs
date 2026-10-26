using Momoka.Shell.Views;

namespace Momoka.Tests;

/// <summary>R-010 executes the current extracted SettingsPage with its real current Shell VM.</summary>
internal static class SaveStateLifetimeTests
{
    internal static void Register()
    {
        foreach (var hide in new[] { "显式Hide", "标题栏X" })
        {
            TestHarness.Test("R010父设置页共享失败/恢复实时同步且不替换草稿 " + hide, () => ActiveState(hide));
            TestHarness.Test("R010父设置页旧保存通知隐藏后不回填/重开重新读取 " + hide, () => HiddenState(hide));
        }
        TestHarness.Test("R010父设置页乱序保存通知只读最终状态", ReorderedState);
        TestHarness.Test("R010父设置页同源重开旧活动票不覆盖新草稿", ReenteredState);
    }

    private static bool Unsaved(object vm) => ShellRuntime.Current.Get<bool>(vm, "HasUnsavedChanges");

    private static (SettingsPage Page, SettingsWindow Window, object Sections, object Draft) Open(LifetimeFixture fixture)
    {
        var page = fixture.NewCachedPage("主页");
        var sections = ShellRuntime.Current.List(fixture.Vm, "Sections");
        var draft = sections[0]!;
        var window = new SettingsWindow(page);
        window.Show(new SettingsRequest(fixture.Services, "主页"));
        return (page, window, sections, draft);
    }

    private static void Hide(SettingsWindow window, string path)
    {
        if (path == "显式Hide") window.Hide();
        else Assert.True(window.BoundaryWindow.RaiseClosing().Cancel);
    }

    private static void AssertDraft(LifetimeFixture fixture, object sections, object draft)
    {
        Assert.True(ReferenceEquals(sections, ShellRuntime.Current.List(fixture.Vm, "Sections")));
        Assert.True(ReferenceEquals(draft, ShellRuntime.Current.List(fixture.Vm, "Sections")[0]));
        Assert.Equal("主页", ShellRuntime.Current.Get<string>(fixture.Vm, "SelectedCategory"));
    }

    private static void ActiveState(string hide)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var opened = Open(fixture);
        Assert.Equal(1, fixture.Settings.SaveSubscribers);
        Assert.False(Unsaved(fixture.Vm));
        fixture.Settings.CompleteSave(true);
        Assert.False(Unsaved(fixture.Vm), "UI前在队列中，不从后台写VM");
        opened.Page.DispatcherQueue.Drain();
        Assert.True(Unsaved(fixture.Vm));
        fixture.Settings.CompleteSave(false);
        opened.Page.DispatcherQueue.Drain();
        Assert.False(Unsaved(fixture.Vm));
        AssertDraft(fixture, opened.Sections, opened.Draft);
        Assert.Equal(0, fixture.Settings.Writes);
        Hide(opened.Window, hide);
        Assert.Equal(0, fixture.Settings.SaveSubscribers);
    }

    private static void HiddenState(string hide)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var opened = Open(fixture);
        fixture.Settings.CompleteSave(true);
        Assert.True(opened.Page.DispatcherQueue.Count > 0);
        Hide(opened.Window, hide);
        opened.Page.DispatcherQueue.Drain();
        Assert.False(Unsaved(fixture.Vm), "隐藏前排队通知不能在隐藏后回填");
        Assert.Equal(0, fixture.Settings.SaveSubscribers);
        fixture.Settings.CompleteSave(true);
        Assert.Equal(0, opened.Page.DispatcherQueue.Count);
        opened.Window.Show(new SettingsRequest(fixture.Services, "主页"));
        Assert.True(Unsaved(fixture.Vm), "重新打开同步当前失败状态，不要重Reload");
        Assert.Equal(1, fixture.Settings.SaveSubscribers);
        AssertDraft(fixture, opened.Sections, opened.Draft);
        Hide(opened.Window, hide);
    }

    private static void ReorderedState()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var opened = Open(fixture);
        fixture.Settings.CompleteSave(true);
        fixture.Settings.CompleteSave(false);
        opened.Page.DispatcherQueue.Drain();
        Assert.False(Unsaved(fixture.Vm));
        AssertDraft(fixture, opened.Sections, opened.Draft);
        opened.Window.Hide();
    }

    private static void ReenteredState()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var opened = Open(fixture);
        fixture.Settings.CompleteSave(true);
        opened.Window.Hide();
        fixture.Settings.CompleteSave(false);
        opened.Window.Show(new SettingsRequest(fixture.Services, "主页"));
        Assert.False(Unsaved(fixture.Vm));
        opened.Page.DispatcherQueue.Drain();
        Assert.False(Unsaved(fixture.Vm));
        Assert.Equal(1, fixture.Settings.SaveSubscribers);
        AssertDraft(fixture, opened.Sections, opened.Draft);
        opened.Window.Hide();
        Assert.Equal(0, fixture.Settings.SaveSubscribers);
    }
}
