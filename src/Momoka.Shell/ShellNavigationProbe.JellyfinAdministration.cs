using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task JellyfinAdministrationAsync()
    {
        using var fixture = CreateFixture();
        var users = new UsersProbeServer();
        var libraries = new LibrariesProbeServer();
        var admin = true;
        var restart = false;
        var writes = new List<string>();
        fixture.Transport.Reply = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal))
                return Json(new { ProductName = "Jellyfin", Version = "12.2.0", Id = "fixture-jellyfin", ServerName = "Jellyfin 离线服务器" });
            if (path.EndsWith("Users/Me", StringComparison.Ordinal))
            {
                users.Users["user"]["Policy"]!["IsAdministrator"] = admin;
                return Json(users.Users["user"]);
            }
            if (path.EndsWith("System/Info", StringComparison.Ordinal))
                return Json(new { ServerName = "Jellyfin 离线服务器", Version = "12.2.0", CanSelfRestart = restart });
            if (path.EndsWith("UserViews", StringComparison.Ordinal)) return Json(new { Items = Array.Empty<EmbyItem>() });
            if (path.EndsWith("/Users", StringComparison.Ordinal)) return Json(users.Users.Values.ToArray());
            if (path.EndsWith("Library/VirtualFolders", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Json(libraries.Folders.Values.ToArray());
            if (path.EndsWith("Libraries/AvailableOptions", StringComparison.Ordinal))
                return Json(JsonNode.Parse("""{"MetadataReaders":[{"Name":"Nfo"}],"MetadataSavers":[{"Name":"Nfo"}],"SubtitleFetchers":[{"Name":"OpenSubtitles"}],"LyricFetchers":[{"Name":"歌词提供者"}],"TypeOptions":[{"Type":"Movie","MetadataFetchers":[{"Name":"TMDB","DefaultEnabled":true}],"ImageFetchers":[{"Name":"TMDB","DefaultEnabled":true}]}]}""")!);
            if (path.EndsWith("/Sessions", StringComparison.Ordinal)) return Json(Array.Empty<EmbyDashboardSession>());
            if (path.EndsWith("ActivityLog/Entries", StringComparison.Ordinal)) return Json(new EmbyActivityResult());
            if (path.EndsWith("Environment/DirectoryContents", StringComparison.Ordinal)) return Json(Array.Empty<object>());
            if (path.EndsWith("System/Configuration/metadata", StringComparison.Ordinal)
                || path.EndsWith("Library/SelectableMediaFolders", StringComparison.Ordinal)
                || path.EndsWith("Users/CopyDataOptions", StringComparison.Ordinal)
                || path.EndsWith("/Features", StringComparison.Ordinal))
                throw new InvalidOperationException("Jellyfin 不应请求 Emby 专属接口：" + path);
            if (request.Method != HttpMethod.Get) writes.Add(path);
            if (path.Contains("/Users/", StringComparison.Ordinal) || path.EndsWith("Auth/Providers", StringComparison.Ordinal)
                || path.EndsWith("Localization/ParentalRatings", StringComparison.Ordinal) || path.EndsWith("/Channels", StringComparison.Ordinal)
                || path.EndsWith("/Devices", StringComparison.Ordinal) || path.EndsWith("Library/MediaFolders", StringComparison.Ordinal))
                return await users.ReplyAsync(request, token);
            return await libraries.ReplyAsync(request, token);
        };
        await fixture.SelectAsync("jellyfin.invalid", kind: MediaServerKind.Jellyfin);
        Require(fixture.Session.Connection?.IsJellyfin == true, "离线 Jellyfin 没有按公开产品信息识别");

        var userPage = new ServerUsersPage();
        _root.Children.Add(userPage);
        try
        {
            var vm = userPage.ViewModel;
            vm.Attach(fixture.Session);
            await vm.ReloadAsync();
            await vm.OpenAsync(vm.Users.First(row => row.Id == "family"));
            await LayoutAsync(userPage);
            Require(vm.CanSave && vm.Folders.Count > 0, "Jellyfin 用户编辑没有加载");
            foreach (var name in new[] { "UserNameBox", "UserConnectBox" })
                Require(Get<TextBox>(userPage, name).Visibility == Visibility.Collapsed, name + " 没有隐藏");
            foreach (var name in new[] { "UserStreamLimit", "UserAutoRemoteQuality" })
                Require(Get<NumberBox>(userPage, name).Visibility == Visibility.Collapsed, name + " 没有隐藏");
            Require(Get<MenuFlyoutItem>(userPage, "UserCopyData").Visibility == Visibility.Collapsed
                && Get<MenuFlyoutItem>(userPage, "UserPreferences").Visibility == Visibility.Collapsed, "Jellyfin 用户菜单仍有无效入口");
            Require(vm.PermissionGroups.SelectMany(group => group.Options).All(option => JellyfinManagement.SupportsUserPermission(option.Id)), "Jellyfin 仍显示无效权限字段");
            Require(vm.Unrated.All(option => option.Id != "Game"), "Jellyfin 家长控制仍包含不支持的游戏枚举");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 1000);
                await LayoutAsync(userPage);
                await SaveUsersFrameAsync(userPage, $"jellyfin-users-profile-{width}.png");
            }
            await vm.SaveCommand.ExecuteAsync(null);
            Require(writes.Count == 0, "未改动 Jellyfin 用户草稿却写回了不支持的默认字段");
            vm.Tab = "password";
            await LayoutAsync(userPage);
            Require(vm.PinVisibility == Visibility.Collapsed, "Jellyfin 个人 PIN 区域没有隐藏");
            Require(vm.CurrentPasswordVisibility == Visibility.Collapsed, "编辑其他用户时要求输入当前密码");
            await vm.OpenAsync(vm.Users.First(row => row.Id == "user"));
            vm.Tab = "password";
            await LayoutAsync(userPage);
            Require(Get<PasswordBox>(userPage, "UserCurrentPasswordBox").Visibility == Visibility.Visible,
                "Jellyfin 修改自身密码时没有当前密码输入框");
            vm.CurrentPassword = "temporary-fixture";
            await vm.OpenAsync(vm.Users.First(row => row.Id == "family"));
            Require(vm.CurrentPassword.Length == 0 && vm.CurrentPasswordVisibility == Visibility.Collapsed,
                "切换编辑用户后仍保留当前密码");
        }
        finally { userPage.Release(); _root.Children.Remove(userPage); }

        var libraryPage = new ServerLibrariesPage();
        _root.Children.Add(libraryPage);
        try
        {
            var vm = libraryPage.ViewModel;
            vm.Attach(fixture.Session);
            await vm.ReloadAsync();
            await vm.OpenAsync(vm.Libraries[0]);
            await LayoutAsync(libraryPage);
            Require(vm.CanSave && vm.Sections.SelectMany(section => section.Fields).All(field => JellyfinManagement.SupportsLibrarySetting(field.Key)), "Jellyfin 媒体库编辑包含无效字段");
            Require(vm.Sections.SelectMany(section => section.Fields).Any(field => field.Key == "EnableRealtimeMonitor"), "Jellyfin 支持的实时监控没有显示");
            Require(vm.Providers.Any(group => group.Kind == "lyrics"), "Jellyfin 单数 LyricFetchers 没有适配");
            Require(vm.Paths.All(row => row.EditVisibility == Visibility.Collapsed), "已有 Jellyfin 路径仍显示无效编辑按钮");
            Require(vm.ContentTypes.All(type => type.Value is not ("games" or "audiobooks")), "Jellyfin 新建库仍包含不支持的类型");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 1000);
                await LayoutAsync(libraryPage);
                await SaveUsersFrameAsync(libraryPage, $"jellyfin-libraries-editor-{width}.png");
            }
            await vm.SaveCommand.ExecuteAsync(null);
            Require(writes.Count == 0, "未改动 Jellyfin 库草稿却写回了配置");
            await vm.OpenAdvancedAsync();
            await LayoutAsync(libraryPage);
            Require(vm.CanSaveAdvanced && vm.AdvancedSections.SelectMany(section => section.Fields).All(field => JellyfinManagement.SupportsAdvancedSetting(field.Key)), "Jellyfin 高级设置没有按能力过滤");
            var directories = await vm.BrowseAsync("", "", "");
            Require(directories is not null && !directories.OfType<JsonObject>().Any(item => EmbyLibraryOptions.Text(item, "Path") == "Network"), "Jellyfin 根目录仍附加不支持的网络设备");
        }
        finally { libraryPage.Release(); _root.Children.Remove(libraryPage); }

        var dashboard = new ServerDashboardPage();
        _root.Children.Add(dashboard);
        try
        {
            var vm = dashboard.ViewModel;
            vm.Attach(fixture.Session);
            await vm.ReloadAsync();
            await LayoutAsync(dashboard);
            Require(vm.CanManage && !vm.CanRestart && Get<MenuFlyoutItem>(dashboard, "ServerRestartMenuItem").Visibility == Visibility.Collapsed,
                "Jellyfin 不可自行重启时没有隐藏按钮");
            Require(Get<MenuFlyoutItem>(dashboard, "ServerShutdownMenuItem").Text == "关闭 Jellyfin Server", "控制台按钮仍使用 Emby 名称");
            await SaveUsersFrameAsync(dashboard, "jellyfin-dashboard.png");
            restart = true;
            await vm.ReloadAsync();
            await LayoutAsync(dashboard);
            Require(vm.CanRestart && vm.RestartVisibility == Visibility.Visible, "服务器恢复自行重启能力后按钮没有恢复");
            admin = false;
            await vm.ReloadAsync();
            await LayoutAsync(dashboard);
            Require(!vm.CanManage && Get<Button>(dashboard, "ServerDashboardPowerButton").Visibility == Visibility.Collapsed,
                "Jellyfin 撤销管理员权限后仍显示电源操作");
        }
        finally { dashboard.Release(); _root.Children.Remove(dashboard); ResizeInspect(1420, 980); }
    }
}
