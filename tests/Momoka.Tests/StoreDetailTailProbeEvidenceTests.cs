using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class StoreDetailTailProbeEvidenceTests
{
    internal static void Register()
    {
        Test("商店尾部探针：短内容、有预告、窄窗与内容更高的准确补高均通过", () =>
        {
            StoreDetailTailProbeEvidence.Require(Reading());
            // 2026-11-18 用户令「放预告片的时候下面会出现大片空位」：控制排进了画框，预告在播也不再补那 72 ——
            // 同一份读数把 WideTrailer 打开，期望值一个数都不变。
            StoreDetailTailProbeEvidence.Require(Reading() with { WideTrailer = true });
            // 窄商店背景至少420；不是零尾部，亦不要求在紧凑单集读到隐藏的大卡框。
            StoreDetailTailProbeEvidence.Require(Reading() with
            {
                PictureHeight = 420,
                ModelMinimum = 36,
                ActualMinimum = 36,
                ContentHeight = 60,
                ActualHeight = 60,
                PictureBottom = 420,
                FirstCardTop = 488
            });
            StoreDetailTailProbeEvidence.Require(Reading() with
            {
                HeroHeight = 520,
                ModelMinimum = 0,
                ActualMinimum = 0,
                ActualHeight = 2,
                DetailsBottom = 520,
                FirstCardTop = 550
            });
        });
        Test("商店尾部探针：零或多补不能通过，模型正确但控件仍旧也拒绝", () =>
        {
            foreach (var broken in new[]
            {
                Reading() with { ModelMinimum = 0, ActualMinimum = 0, ActualHeight = 2 },
                Reading() with { ActualMinimum = 0 },
                Reading() with { ModelMinimum = 50, ActualMinimum = 50, ActualHeight = 50 },
                Reading() with { ActualHeight = 36 },
                Reading() with { ContentHeight = 80 },
                // 多补那 72（预告控制排那一档的老算法）必须被拒绝。
                Reading() with { ModelMinimum = 106, ActualMinimum = 106, ActualHeight = 106 }
            }) Assert.Throws<InvalidOperationException>(() => StoreDetailTailProbeEvidence.Require(broken));
        });
        Test("商店尾部探针：实际剧照与操作下沿决定首卡避让，不从模型高度猜", () =>
        {
            var read = Reading();
            // 首卡只留15DIP，落在取整误差容差内；14DIP仍红。
            StoreDetailTailProbeEvidence.Require(read with { FirstCardTop = 433 });
            foreach (var broken in new[]
            {
                read with { FirstCardTop = 432 },
                read with { PictureBottom = 480 },
                read with { ControlsBottom = 480 },
                read with { DetailsBottom = 480 },
                read with { CoverBottom = 480 }
            }) Assert.Throws<InvalidOperationException>(() => StoreDetailTailProbeEvidence.Require(broken));
        });
        Test("商店尾部探针：无图或实际隐藏画面保留内容，坏边界拒绝", () =>
        {
            var plain = Reading() with
            {
                ReserveBackdrop = false,
                ModelMinimum = 0,
                ActualMinimum = 0,
                ActualHeight = 2,
                PictureBottom = null,
                FirstCardTop = 430
            };
            StoreDetailTailProbeEvidence.Require(plain);
            StoreDetailTailProbeEvidence.Require(plain with { FirstCardTop = null });
            StoreDetailTailProbeEvidence.Require(Reading() with { PictureBottom = null });
            foreach (var broken in new[]
            {
                plain with { ModelMinimum = 34, ActualMinimum = 34, ActualHeight = 34 },
                Reading() with { ActualHeight = double.NaN },
                Reading() with { PictureBottom = double.PositiveInfinity },
                Reading() with { FirstCardTop = -1 },
                Reading() with { ContentHeight = -1 }
            }) Assert.Throws<InvalidOperationException>(() => StoreDetailTailProbeEvidence.Require(broken));
        });
    }

    private static StoreDetailTailRead Reading() => new(true, false, 418, 400, 34, 34, 2, 34,
        286, 400, 418, null, 462);
}
