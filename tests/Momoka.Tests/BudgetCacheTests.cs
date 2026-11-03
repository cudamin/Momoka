using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// <see cref="BudgetCache{TKey,TValue}"/> 的全部约定是「总量按条目自报的成本算」和「最久没用的先走」。
/// 轮播背景用它，因为同一张请求回来的图可能是 720 高也可能是 1600 高，按张数封顶挡不住内存。
/// </summary>
internal static class BudgetCacheTests
{
    public static void Register()
    {
        Test("预算缓存：按成本累计，换值时替换旧成本", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);

            Assert.True(cache.Set("a", 30));
            Assert.True(cache.Set("b", 50));
            Assert.Equal(80, cache.Bytes);
            Assert.Equal(2, cache.Count);

            Assert.True(cache.Set("a", 40));
            Assert.Equal(90, cache.Bytes, "替换后的总数应该是新成本，不是两份相加");
            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGet("a", out var a));
            Assert.Equal(40, a);
        });

        Test("预算缓存：超预算从最久没用的开始淘汰", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);
            cache.Set("a", 40);
            cache.Set("b", 40);
            Assert.True(cache.TryGet("a", out _));

            Assert.True(cache.Set("c", 40));

            Assert.True(cache.TryGet("a", out _), "刚看过的不能被淘汰");
            Assert.False(cache.TryGet("b", out _), "最久没看的那一条先走");
            Assert.True(cache.TryGet("c", out _));
            Assert.Equal(80, cache.Bytes);
        });

        Test("预算缓存：单条超过预算不写入，也不留下同键旧值", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);
            cache.Set("a", 40);

            Assert.False(cache.Set("a", 120), "一条就超过整个预算的，存进去只会把所有别的挤掉");
            Assert.Equal(0, cache.Count);
            Assert.Equal(0, cache.Bytes);
            Assert.False(cache.TryGet("a", out _));
        });

        Test("预算缓存：Remove 与 Clear 把账一起抹掉", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);
            cache.Set("a", 30);
            cache.Set("b", 20);

            Assert.True(cache.Remove("a"));
            Assert.False(cache.Remove("a"));
            Assert.Equal(20, cache.Bytes);
            Assert.Equal(1, cache.Count);

            cache.Clear();
            Assert.Equal(0, cache.Bytes);
            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGet("b", out _));
        });

        Test("预算缓存：容量为零不存任何东西", () =>
        {
            var cache = new BudgetCache<string, int>(0, value => value);

            Assert.False(cache.Set("a", 0));
            Assert.Equal(0, cache.Count);
            Assert.Equal(0, cache.Bytes);
        });

        Test("预算缓存：负数成本按零算，容量边界允许刚好装满", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);
            Assert.True(cache.Set("zero", -5));
            Assert.Equal(0, cache.Bytes);

            Assert.True(cache.Set("full", 100));
            Assert.Equal(100, cache.Bytes);
            Assert.True(cache.TryGet("full", out _));
        });

        Test("预算缓存：淘汰次序从最久到最新", () =>
        {
            var cache = new BudgetCache<string, int>(100, value => value);
            cache.Set("a", 30);
            cache.Set("b", 30);
            cache.Set("c", 30);
            cache.TryGet("a", out _);

            Assert.Equal("a,c,b", string.Join(',', cache.Keys));
        });
    }
}
