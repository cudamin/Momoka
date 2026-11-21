using Microsoft.UI.Xaml;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PickerFitTests
{
    internal static void Register()
    {
        Test("文件选项自检：商店宽版单音轨文字占左列，右侧字幕不误报整排偏移", () =>
        {
            var page = new DetailPage();
            page.SetUpPickerGeometry();
            var reading = page.PickerFit();
            Assert.True(reading.Ok, reading.Detail);
            Assert.Contains("整排贴着左边", reading.Detail);
        });
        Test("文件选项自检：隐藏或未布局的单音轨文字不能掩盖真实偏移", () =>
        {
            foreach (var missing in new[] { "row-hidden", "text-hidden", "row-width", "row-height", "text-width", "text-height" })
            {
                var page = new DetailPage();
                page.SetUpPickerGeometry();
                switch (missing)
                {
                    case "row-hidden": page.AudioPickerRow.Visibility = Visibility.Collapsed; break;
                    case "text-hidden": page.ColumnAudioText.Visibility = Visibility.Collapsed; break;
                    case "row-width": page.AudioPickerRow.ActualWidth = 0; break;
                    case "row-height": page.AudioPickerRow.ActualHeight = 0; break;
                    case "text-width": page.ColumnAudioText.ActualWidth = 0; break;
                    case "text-height": page.ColumnAudioText.ActualHeight = 0; break;
                }
                Assert.False(page.PickerFit().Ok, missing);
            }
        });
        Test("文件选项自检：文字与字幕整体右移仍失败，选择框超宽仍失败", () =>
        {
            var page = new DetailPage();
            page.SetUpPickerGeometry();
            page.AudioPickerRow.LayoutX = 40;
            page.SubtitlePickerRow.LayoutX += 40;
            Assert.False(page.PickerFit().Ok);
            page.SetUpPickerGeometry();
            page.AudioPickerRow.LayoutX = 0;
            page.ColumnSubtitlePicker.ActualWidth = 500;
            var reading = page.PickerFit();
            Assert.False(reading.Ok);
            Assert.Contains("出界", reading.Detail);
        });
        Test("文件选项自检：沉浸宽版和紧凑版保留各自左沿与越界判据", () =>
        {
            foreach (var store in new[] { false, true })
                foreach (var compact in new[] { false, true })
                {
                    var page = new DetailPage();
                    page.SetUpPickerGeometry(store, compact);
                    Assert.True(page.PickerFit().Ok);
                    if (compact) page.SubtitlePicker.ActualWidth = 800;
                    else page.ColumnSubtitlePicker.ActualWidth = 800;
                    Assert.False(page.PickerFit().Ok);
                }
        });
        Test("文件选项自检：双份显隐与缺少播放落点仍拒绝", () =>
        {
            var page = new DetailPage();
            page.SetUpPickerGeometry();
            page.PickerPanel.Visibility = Visibility.Visible;
            Assert.False(page.PickerFit().Ok);
            page.PickerPanel.Visibility = Visibility.Collapsed;
            page.ViewModel.PlayTarget = null;
            Assert.False(page.PickerFit().Ok);
        });
    }
}
