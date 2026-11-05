using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Infrastructure;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private void MeasureSeriesShelf()
    {
        // 始终测普通剧页的标题和两行文字，电影/季页无需实例化或加载一份剧集列表。
        // 不量沉浸后的坐标：收起卡面、隐藏标题线和滚动均不能改变触发位置。
        var head = new ShelfHead { Title = "更多来自：第 1 季", ShowLines = true };
        head.Measure(new Size(600, double.PositiveInfinity));
        var title = new TextBlock { Text = "集名", Style = (Style)Application.Current.Resources["EgBodyStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 2, Height = CardSize.MediaTitleHeight, LineHeight = CardSize.MediaTitleExtra,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap };
        var subtitle = new TextBlock { Text = "第 1 集 · 24 分钟", Style = (Style)Application.Current.Resources["EgDataStyle"], MaxLines = 1 };
        title.Measure(new Size(CardSize.WideWidth, double.PositiveInfinity));
        subtitle.Measure(new Size(CardSize.WideWidth, double.PositiveInfinity));

        // BodySheet / EpisodePanel 的普通上内边距、标题间距、ShelfStrip 顶距和卡片文字顶距。
        var cardHeight = CardSize.WideHeight + 8 + title.DesiredSize.Height + subtitle.DesiredSize.Height;
        // CardTemplate 的 Button 沿用平台垂直居中，在集带固定高度内分摊上下余量。
        var centered = Math.Max(0, CardSize.WideHeight + CardSize.Chrome + CardSize.MediaTitleExtra - 6 - cardHeight) / 2;
        ViewModel.SetSeriesShelfTextBottom(28 + 24 + head.DesiredSize.Height + 10 + 6 + centered + cardHeight);
    }
}
