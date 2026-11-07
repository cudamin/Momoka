using System.Xml.Linq;
using Microsoft.UI.Xaml;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>真实站点 VM 与本树 XAML 契约；不以源码断言冒充 WinUI 渲染或真实输入。</summary>
internal static class SiteGridContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register(string sourceRoot)
    {
        Test("站点网格：入口是原生按钮，内容由单一ContentDialog承载", () =>
        {
            var picker = Load(sourceRoot, "Views/MoviePilotSitePicker.xaml");
            Assert.Equal("Button", Named(picker, "ChooseSites").Name.LocalName);
            Assert.False(picker.Descendants().Any(node => node.Name.LocalName is "Flyout" or "ListView" or "ItemsControl"));
            var dialog = Load(sourceRoot, "Views/MoviePilotSiteDialog.xaml");
            Assert.Equal("ContentDialog", dialog.Root!.Name.LocalName);
            Assert.Equal("None", dialog.Root.Attribute("DefaultButton")?.Value);
            Assert.Equal("关闭站点选择", Named(dialog, "CloseSiteDialog").Attribute("AutomationProperties.Name")?.Value);
            Assert.Equal("选择站点", Named(dialog, "SiteDialogHeader").Descendants()
                .Single(node => node.Name.LocalName == "TextBlock").Attribute("Text")?.Value);
            var styles = Load(sourceRoot, "Theme/Styles.xaml");
            Assert.Equal("{StaticResource EgDialogSurfaceStyle}", styles.Descendants().Single(node =>
                node.Name.LocalName == "Style" && node.Attribute("TargetType")?.Value == "local:MoviePilotSiteDialog")
                .Attribute("BasedOn")?.Value);
        });

        Test("站点网格：三列自适应、原生整卡Toggle与横向操作条", () => CheckGrid(Load(sourceRoot, "Views/MoviePilotSiteDialog.xaml")));
        Test("站点网格：退回竖表或固定卡宽被拒绝", () =>
        {
            var dialog = Load(sourceRoot, "Views/MoviePilotSiteDialog.xaml");
            Named(dialog, "SiteChoices").Name = dialog.Root!.Name.Namespace + "ItemsControl";
            Assert.Throws<AssertionException>(() => CheckGrid(dialog));
            dialog = Load(sourceRoot, "Views/MoviePilotSiteDialog.xaml");
            Named(dialog, "SelectSite").SetAttributeValue("Width", "240");
            Assert.Throws<AssertionException>(() => CheckGrid(dialog));
        });

        Test("站点网格：局部主题保留原生模板和三种字典，无截图固定色", () =>
        {
            var controls = Load(sourceRoot, "Theme/MoviePilotSiteControls.xaml");
            var dictionaries = controls.Descendants().Where(node => node.Name.LocalName == "ResourceDictionary"
                && node.Attribute(Xaml + "Key") is not null).ToArray();
            Assert.Equal("Dark|Light|HighContrast", string.Join('|', dictionaries.Select(node => node.Attribute(Xaml + "Key")!.Value)));
            Assert.Equal("{StaticResource DefaultToggleButtonStyle}", controls.Descendants().Single(node =>
                node.Attribute(Xaml + "Key")?.Value == "SiteCardToggleStyle").Attribute("BasedOn")?.Value);
            Assert.False(controls.Descendants().Any(node => node.Name.LocalName is "ControlTemplate" or "Color" or "SolidColorBrush"));
            foreach (var dictionary in dictionaries.Where(node => node.Attribute(Xaml + "Key")!.Value != "HighContrast"))
                Assert.Equal("EgAccentSoftBrush", dictionary.Elements().Single(node =>
                    node.Attribute(Xaml + "Key")?.Value == "ToggleButtonBackgroundChecked").Attribute("ResourceKey")?.Value);
        });

        Test("站点网格：草稿按卡退出默认，计数与圆形标识通知，取消零保存", () =>
        {
            using var fixture = new SiteFixture();
            var owner = fixture.Picker();
            using var draft = owner.CreateDraft();
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            var notified = new List<string>();
            draft.Sites[0].PropertyChanged += (_, args) => notified.Add(args.PropertyName!);
            Assert.True(draft.CanPick && draft.UseDefault);
            draft.Sites[0].Selected = true;
            Assert.False(draft.UseDefault);
            Assert.Equal("已选择 1/2", draft.SelectionText);
            Assert.Equal(Visibility.Visible, draft.Sites[0].SelectedVisibility);
            Assert.Equal(Visibility.Collapsed, draft.Sites[0].UnselectedVisibility);
            Assert.True(notified.Contains("SelectedVisibility") && notified.Contains("UnselectedVisibility"));
            draft.Cancel();
            Assert.False(draft.CommitSelection());
            Assert.True(owner.UseDefault);
            Assert.Equal(0, fixture.Saves);
        });

        Test("站点网格：确认保存一次、同步宿主目录和偏好，保留已有资源", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults = "[" + SearchSitePreferenceTests.Result("既有资源", "站点甲") + "]";
            var vm = new Momoka.Shell.ViewModels.MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            var owner = vm.SearchSites;
            using var draft = owner.CreateDraft();
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            draft.Sites[1].Selected = true;
            Assert.True(draft.CommitSelection());
            Assert.True(draft.IsDraftOwnerCurrent, "本次保存的同步服务事件不应使草稿过期");
            Assert.Equal(1, fixture.Saves);
            Assert.Equal("2", owner.Selection!.QueryValue());
            Assert.Equal(2, owner.Sites.Count);
            Assert.Equal(1, vm.Versions.Count);
            Assert.Equal(2, fixture.Transport.Requests.Count, "确认只保存，不由VM擅自发出搜索");
        });

        Test("站点网格：全选总是显式选择，清空不退默认且设置可确认", () =>
        {
            using var fixture = new SiteFixture();
            using var draft = fixture.Picker().CreateDraft();
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            draft.SelectAllCommand.Execute(null);
            draft.SelectAllCommand.Execute(null);
            Assert.False(draft.UseDefault);
            Assert.Equal("1,2", draft.Selection!.QueryValue());
            draft.ClearSelectionCommand.Execute(null);
            Assert.False(draft.UseDefault);
            Assert.False(draft.CanSearch);
            Assert.True(draft.CanComplete);
            Assert.True(draft.CommitSelection());
            Assert.Equal(1, fixture.Saves);
        });

        Test("站点网格：忙态禁操作、失败可刷新、空态不扩大显式搜索", () =>
        {
            using var fixture = new SiteFixture();
            using var draft = fixture.Picker().CreateDraft();
            draft.Busy = true;
            Assert.False(draft.CanComplete || draft.CanSearch || draft.CanPick);
            Assert.False(draft.SelectAllCommand.CanExecute(null));
            draft.Busy = false;
            fixture.Transport.FailCatalogue = true;
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            Assert.True(draft.NoticeOpen && draft.RefreshCommand.CanExecute(null));
            fixture.Transport.FailCatalogue = false;
            fixture.Transport.Catalogue = "[]";
            ResourceFixture.Complete(draft.ReloadAsync());
            draft.UseDefault = false;
            Assert.Equal(Visibility.Visible, draft.EmptyVisibility);
            Assert.Equal("已选择 0/0", draft.SelectionText);
            Assert.False(draft.CanSearch);
            Assert.Equal(0, fixture.Saves);
        });

        Test("站点网格：未出现的偏好刷新不覆盖，默认切换保留手动编号", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            var owner = fixture.Picker();
            using var draft = owner.CreateDraft();
            fixture.Transport.Catalogue = "[{\"id\":1,\"name\":\"甲\",\"is_active\":true}]";
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            Assert.False(draft.CanSearch);
            fixture.Transport.Catalogue = SiteTransport.FullCatalogue;
            ResourceFixture.Complete(draft.ReloadAsync());
            Assert.Equal("2", draft.Selection!.QueryValue());
            draft.UseDefault = true;
            draft.UseDefault = false;
            Assert.Equal("2", draft.Selection!.QueryValue());
            Assert.Equal(1, fixture.Saves);
        });

        Test("站点网格：切连接、宿主重新Attach、离页使旧草稿确认失效", () =>
        {
            using var fixture = new SiteFixture();
            var owner = fixture.Picker();
            using var old = owner.CreateDraft();
            ResourceFixture.Complete(old.EnsureLoadedAsync());
            old.Sites[0].Selected = true;
            owner.Attach(fixture.Service);
            Assert.False(old.CanComplete || old.CommitSelection());
            using var detached = owner.CreateDraft();
            owner.Cancel();
            Assert.False(detached.CommitSelection());
            owner.Attach(fixture.Service);
            using var changed = owner.CreateDraft();
            fixture.Settings.MoviePilot.Username = "different-anonymous-user";
            Assert.False(changed.CanComplete || changed.CommitSelection());
            Assert.Equal(0, fixture.Saves);
        });

        Test("站点网格：保存失败留下草稿和原因，不误报搜索确认", () =>
        {
            using var fixture = new SiteFixture();
            using var draft = fixture.Picker().CreateDraft();
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            draft.Sites[0].Selected = true;
            fixture.FailSave = true;
            Assert.False(draft.CommitSelection());
            Assert.True(draft.NoticeOpen && draft.NoticeTitle!.Contains("未能保存"));
            fixture.FailSave = false;
            Assert.True(draft.CommitSelection());
            Assert.Equal(2, fixture.Saves);
        });

        Test("站点网格：打开后外部偏好更改拒绝旧草稿覆盖", () =>
        {
            using var fixture = new SiteFixture();
            var owner = fixture.Picker();
            using var draft = owner.CreateDraft();
            ResourceFixture.Complete(draft.EnsureLoadedAsync());
            draft.Sites[0].Selected = true;
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            Assert.False(draft.CanComplete || draft.CommitSelection());
            Assert.Equal(1, fixture.Saves);
            Assert.Equal("2", string.Join(',', fixture.Service.SearchSitePreference().SiteIds));
        });
    }

    private static void CheckGrid(XDocument dialog)
    {
        var grid = Named(dialog, "SiteChoices");
        Assert.Equal("ItemsRepeater", grid.Name.LocalName);
        var layout = grid.Descendants().Single(node => node.Name.LocalName == "UniformGridLayout");
        Assert.Equal("3", layout.Attribute("MaximumRowsOrColumns")?.Value);
        Assert.Equal("168", layout.Attribute("MinItemWidth")?.Value);
        Assert.Equal("Fill", layout.Attribute("ItemsStretch")?.Value);
        Assert.Equal("OnGridSizeChanged", grid.Attribute("SizeChanged")?.Value);
        var card = Named(dialog, "SelectSite");
        Assert.Equal("ToggleButton", card.Name.LocalName);
        Assert.Equal("{x:Bind Selected, Mode=TwoWay}", card.Attribute("IsChecked")?.Value);
        Assert.True(card.Attribute("Width") is null);
        Assert.Equal("WrapRow", Named(dialog, "SiteActions").Name.LocalName);
        foreach (var name in new[] { "SelectAllSites", "ClearSelectedSites", "ReloadSites", "SelectedSiteCount" })
            Assert.True(Named(dialog, name).Ancestors().Contains(Named(dialog, "SiteActions")));
        Assert.False(dialog.Descendants().Any(node => node.Name.LocalName is "ListView" or "ItemsControl" or "StackLayout"));
    }

    private static XDocument Load(string root, string relative) => XDocument.Load(Path.Combine(root, "src/Momoka.Shell", relative));
    private static XElement Named(XDocument xml, string name) => xml.Descendants().Single(node => node.Attribute(Xaml + "Name")?.Value == name);
}
