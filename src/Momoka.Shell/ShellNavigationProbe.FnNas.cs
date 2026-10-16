using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FnNasAdministrationAsync()
    {
        using var fixture = CreateFixture();
        var serverSettings = JsonNode.Parse("""{"name":"离线飞牛","lan":"zh-CN","direct_link_enable":1,"direct_link_allowed_level":0,"file_monitor":1,"meta_dir_vol_index":1,"mediasrv_cache_vol_index":1,"gpu_acc":1,"cpu_allow_decoding":1,"future":{"keep":true}}""")!.AsObject();
        var library = JsonNode.Parse("""{"guid":"library","name":"电影库","category":"Movie","dir_list":["/media/movies"],"lan":"zh-CN","prefer_local_nfo":1,"skip_filesize":3} """)!.AsObject();
        var admin = true;
        var writes = new List<(string Path, JsonNode? Body)>();
        fixture.Transport.Reply = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(token));
            object? data;
            if (path.EndsWith("/user/loginByPassword", StringComparison.Ordinal)) data = new { token = "fnnas-offline-token" };
            else if (path.EndsWith("/user/info", StringComparison.Ordinal)) data = new { guid = "admin", username = "offline", is_admin = admin ? 1 : 0 };
            else if (path.EndsWith("/sys/config", StringComparison.Ordinal)) data = new { server_name = "离线飞牛", server_guid = "server", supported_languages = new[] { "zh-CN", "en" }, default_language = "zh-CN" };
            else if (path.EndsWith("/mdb/list", StringComparison.Ordinal)) data = new object[] { library.DeepClone(), new { guid = "second", name = "电视剧", category = "TV", dir_list = new[] { "/media/tv" } } };
            else if (path.EndsWith("/mdb/library", StringComparison.Ordinal) && request.Method == HttpMethod.Get) data = library;
            else if (path.EndsWith("/server/info", StringComparison.Ordinal) && request.Method == HttpMethod.Get) data = serverSettings;
            else if (path.EndsWith("/server/allVols", StringComparison.Ordinal)) data = new { vols = new[] { new { index = 1, state = 0 }, new { index = 2, state = 0 } } };
            else if (path.EndsWith("/search/indexStatus", StringComparison.Ordinal)) data = new { rebuilding = false };
            else if (path.EndsWith("/manager/template/permission", StringComparison.Ordinal) && request.Method == HttpMethod.Get) data = new { media_permission = 1, mediadb_list = new[] { "library" } };
            else if (path.EndsWith("/manager/user/list", StringComparison.Ordinal)) data = new[] { new { guid = "user", username = "访客", is_admin = 0, media_permission = 1, mediadb_list = new[] { "library" } } };
            else if (path.EndsWith("/task/schedule/getSetting", StringComparison.Ordinal)) data = new { task_switch = "1" };
            else if (path.EndsWith("/task/schedule/list", StringComparison.Ordinal)) data = new[] { new { name = "scrap-item", last_run_start_time = 1700000000 }, new { name = "extra-subtitle", last_run_start_time = 0 } };
            else if (path.EndsWith("/server/getAppAuthorizedDir", StringComparison.Ordinal)) data = new { authDirList = new[] { new { path = "/media" } } };
            else if (path.EndsWith("/server/path", StringComparison.Ordinal)) data = new[] { new { filename = "movies", is_dir = true } };
            else if (request.Method != HttpMethod.Get && (path.EndsWith("/mdb/library", StringComparison.Ordinal)
                || path.EndsWith("/mdb/setSort", StringComparison.Ordinal) || path.EndsWith("/manager/user/create", StringComparison.Ordinal)
                || path.EndsWith("/manager/template/permission", StringComparison.Ordinal) || path.EndsWith("/server/info", StringComparison.Ordinal)
                || path.EndsWith("/task/schedule/set", StringComparison.Ordinal)))
            {
                writes.Add((path, body?.DeepClone()));
                if (path.EndsWith("/server/info", StringComparison.Ordinal)) serverSettings = body!.AsObject();
                if (path.EndsWith("/mdb/library", StringComparison.Ordinal)) { library = body!.AsObject(); library["guid"] = "library"; }
                data = true;
            }
            else throw new InvalidOperationException("飞牛离线夹具未声明的请求：" + path);
            return Json(new { code = 0, data });
        };
        var server = new ServerProfile { Name = "离线飞牛", Url = "https://fnnas.invalid/v", ServerKind = MediaServerKind.FnNas };
        var account = new AccountProfile { Username = "offline" };
        server.Accounts.Add(account); fixture.Settings.Servers.Add(server);
        await fixture.Session.SignInAsync(server, account, "offline-password", account.Username, false, default);
        fixture.Capabilities.ServerKind = MediaServerKind.FnNas;
        fixture.Capabilities.IsAdministrator = true;
        var frame = new Frame { Margin = new Thickness(24, 42, 24, 16) };
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(FnNasLibrariesPage), new FnNasLibrariesRequest(fixture.Services));
            var libraries = (FnNasLibrariesPage)frame.Content;
            await UntilAsync(() => libraries.ViewModel.IsReady && !libraries.ViewModel.Busy);
            Require(libraries.ViewModel.Libraries.Count == 2, "飞牛媒体库未加载");
            await FnNasScreensAsync(libraries, "libraries");
            libraries.ViewModel.SortCommand.Execute(null);
            libraries.ViewModel.SelectedLibrary = libraries.ViewModel.Libraries[0];
            libraries.ViewModel.MoveDownCommand.Execute(null);
            await libraries.ViewModel.SaveOrderCommand.ExecuteAsync(null);
            Require(writes.Last().Body!["list_sort"]![0]!["guid"]!.GetValue<string>() == "second", "飞牛排序没有写入实际新顺序");
            await libraries.ViewModel.OpenAsync(libraries.ViewModel.Libraries.Single(row => row.Id == "library"));
            Get<TextBox>(libraries, "FnNasLibraryName").Text = "改名后的电影库";
            await FnNasScreensAsync(libraries, "library-editor");
            await libraries.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(library["name"]!.GetValue<string>() == "改名后的电影库" && !libraries.ViewModel.Editing, "飞牛媒体库编辑没有保存");
            await libraries.ViewModel.NewLibraryCommand.ExecuteAsync(null);
            var beforeCancel = writes.Count;
            libraries.ViewModel.BackCommand.Execute(null);
            Require(writes.Count == beforeCancel && !libraries.ViewModel.Editing, "退出新建媒体库提交了草稿");

            frame.Navigate(typeof(FnNasUsersPage), new FnNasUsersRequest(fixture.Services));
            var users = (FnNasUsersPage)frame.Content;
            await UntilAsync(() => users.ViewModel.IsReady && !users.ViewModel.Busy);
            Require(users.ViewModel.Users.Count == 1, "飞牛用户列表未加载");
            await FnNasScreensAsync(users, "users");
            users.ViewModel.DefaultsCommand.Execute(null);
            Get<ComboBox>(users, "FnNasUserPermission").SelectedIndex = 2;
            await users.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(writes.Last().Body!["media_permission"]!.GetValue<int>() == 2, "默认用户权限未保存");
            users.ViewModel.NewUserCommand.Execute(null);
            Get<TextBox>(users, "FnNasUserName").Text = "新访客";
            Get<PasswordBox>(users, "FnNasUserPassword").Password = "offline-pass";
            Get<PasswordBox>(users, "FnNasUserConfirmPassword").Password = "offline-pass";
            await FnNasScreensAsync(users, "user-editor");
            await users.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(writes.Last().Body!["password"]!.GetValue<string>() != "offline-pass" && !users.ViewModel.Editing, "新用户密码未摘要或创建未完成");

            frame.Navigate(typeof(FnNasSettingsPage), new FnNasSettingsRequest(fixture.Services));
            var settings = (FnNasSettingsPage)frame.Content;
            await UntilAsync(() => settings.ViewModel.IsReady && !settings.ViewModel.Busy);
            Require(settings.ViewModel.CanSave && settings.ViewModel.Languages.Count == 2, "飞牛配置或语言未加载");
            await FnNasScreensAsync(settings, "settings");
            Require(!Descendants(settings).OfType<ToggleSwitch>().Any(control => control.Header?.ToString()?.Contains("转码", StringComparison.Ordinal) == true), "仍显示转码控件");
            Get<TextBox>(settings, "FnNasServerName").Text = "离线修改名称";
            Get<ToggleSwitch>(settings, "FnNasFileMonitor").IsOn = false;
            Require(!Descendants(settings).OfType<Control>().Select(control => control switch
                { ToggleSwitch toggle => toggle.Header?.ToString(), ComboBox choice => choice.Header?.ToString(), _ => null })
                .Any(label => label?.Contains("网盘", StringComparison.Ordinal) == true || label?.Contains("直链", StringComparison.Ordinal) == true),
                "飞牛设置不应显示网盘功能");
            await settings.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(serverSettings["name"]!.GetValue<string>() == "离线修改名称"
                && serverSettings["file_monitor"]!.GetValue<int>() == 0 && serverSettings["gpu_acc"]!.GetValue<int>() == 1
                && serverSettings["future"]!["keep"]!.GetValue<bool>()
                && serverSettings["direct_link_enable"]!.GetValue<int>() == 1
                && serverSettings["direct_link_allowed_level"]!.GetValue<int>() == 0, "保存配置修改了未编辑字段");

            frame.Navigate(typeof(FnNasTasksPage), new FnNasTasksRequest(fixture.Services));
            var tasks = (FnNasTasksPage)frame.Content;
            await UntilAsync(() => tasks.ViewModel.IsReady && !tasks.ViewModel.Busy);
            Require(tasks.ViewModel.Tasks.Count == 2, "飞牛计划任务未加载");
            await FnNasScreensAsync(tasks, "tasks");
            Get<ToggleSwitch>(tasks, "FnNasTasksEnabled").IsOn = false;
            await tasks.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(writes.Last().Body!["task_switch"]!.GetValue<string>() == "2", "任务开关未写入正确类型和值");
            admin = false;
            await tasks.ViewModel.ReloadAsync();
            Require(tasks.ViewModel.ContentVisibility == Visibility.Collapsed && tasks.ViewModel.RecoveryVisibility == Visibility.Visible, "权限撤销没有隐藏管理表单和保留恢复入口");
            admin = true;
            await tasks.ViewModel.ReloadAsync();
            Require(tasks.ViewModel.ContentVisibility == Visibility.Visible, "权限恢复后表单未恢复");
            var beforeSignOut = writes.Count;
            fixture.Session.SignOut();
            await tasks.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(writes.Count == beforeSignOut, "退出后旧任务页仍写入服务器");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
        }
    }

    private async Task FnNasScreensAsync(Page page, string name)
    {
        foreach (var width in new[] { 1100, 760 })
        {
            ResizeInspect(width, 940);
            await LayoutAsync(page);
            Require(page.ActualWidth > 0 && page.ActualHeight > 0, "飞牛页面没有实际布局");
            await SaveProbeScreenAsync($"fnnas-{name}-{width}-screen.png");
        }
    }
}
