using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class UpcomingPaginationTests
{
    internal static void Register()
    {
        Test("PERF-06 真LoadAsync：已删项过滤仍保留原始读进度，重载清旧offset", () =>
            SingleThreadPump.Run(async () =>
            {
                using var env = new MemoryEnvironment();
                var source = env.Source("removed", "account");
                using var vm = new LibraryViewModel(env.Images);
                vm.SwitchSource(source);
                vm.Hide(source.Bind(Item("deleted", null)));
                vm.Read = (_, _) => Task.FromResult<ItemsResult?>(new()
                {
                    Items = [source.Bind(Item("deleted", null)), source.Bind(Item("retained", null))],
                    TotalRecordCount = 4
                });
                await vm.Load(true);
                Assert.Equal("retained", vm.Cards.Single().Item.Id);
                Assert.Equal(2, vm.ReadCount);
                Assert.Equal(2, vm.RawReadEnd);
                vm.Read = (_, _) => Task.FromResult<ItemsResult?>(new() { Items = [], TotalRecordCount = 0 });
                await vm.Load(true);
                Assert.Equal(0, vm.ReadCount);
                Assert.Equal(0, vm.RawReadEnd);
                Assert.Equal(0, vm.Cards.Count);
            }));
        foreach (var dated in new[] { true, false })
            Test("PERF-06 真实分组：1万同" + (dated ? "日" : "未知日期") + "加100仅Add并保留集合／卡identity", () =>
            {
                using var env = new MemoryEnvironment();
                using var vm = new LibraryViewModel(env.Images);
                var date = dated ? (DateTimeOffset?)new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero) : null;
                for (var i = 0; i < 10_000; i++) vm.Cards.Add(vm.NewCard(Item(i.ToString(), date)));
                vm.Update();
                var days = vm.UpcomingDays;
                var day = days.Single();
                var cards = day.Cards;
                var retained = cards.ToArray();
                var events = new List<NotifyCollectionChangedAction>();
                cards.CollectionChanged += (_, args) => events.Add(args.Action);
                var dayEvents = 0;
                days.CollectionChanged += (_, _) => dayEvents++;
                for (var i = 10_000; i < 10_100; i++) vm.Cards.Add(vm.NewCard(Item(i.ToString(), date)));
                vm.Update();
                Assert.True(ReferenceEquals(days, vm.UpcomingDays));
                Assert.True(ReferenceEquals(day, vm.UpcomingDays.Single()));
                Assert.True(ReferenceEquals(cards, day.Cards));
                Assert.Equal(10_100, cards.Count);
                Assert.Equal(100, events.Count);
                Assert.True(events.All(action => action == NotifyCollectionChangedAction.Add));
                Assert.Equal(0, dayEvents);
                for (var i = 0; i < retained.Length; i++) Assert.True(ReferenceEquals(retained[i], cards[i]));
                vm.Update();
                Assert.Equal(100, events.Count, "重复更新没有额外集合通知");
            });

        Test("PERF-06 真diff比较数：1万加100是10000次引用比较而非101014950", () =>
        {
            var current = new ObservableCollection<object>(Enumerable.Range(0, 10_000).Select(_ => new object()));
            var desired = current.Concat(Enumerable.Range(0, 100).Select(_ => new object())).ToArray();
            var comparer = new CountingReferences<object>();
            UpcomingListDiff.Apply(current, desired, comparer);
            Assert.Equal(10_000L, comparer.Comparisons);
            Assert.Equal(10_100, current.Count);
            Console.WriteLine($"    实际 UpcomingListDiff.Apply：10000 + 100，引用比较 {comparer.Comparisons} 次");
        });

        Test("PERF-06 真diff删除／反序：不逐卡IndexOf，不Reset且保留每个对象", () =>
        {
            var current = new ObservableCollection<object>(Enumerable.Range(0, 2_000).Select(_ => new object()));
            var desired = current.Where((_, index) => index % 4 != 0).Reverse().ToArray();
            var comparer = new CountingReferences<object>();
            var resets = 0;
            current.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            UpcomingListDiff.Apply(current, desired, comparer);
            Assert.True(current.Zip(desired).All(pair => ReferenceEquals(pair.First, pair.Second)));
            Assert.True(comparer.Comparisons < 12_000, "重排只做线性身份比较；集合本身Move位移仍有代价");
            Assert.Equal(0, resets);
            Console.WriteLine($"    实际删除／反序diff：2000 -> 1500，引用比较 {comparer.Comparisons} 次");
        });

        Test("PERF-06 真分组：新日期插入未知组之前，同日顺序／日期间移动／删除不Clear", () =>
        {
            using var env = new MemoryEnvironment();
            using var vm = new LibraryViewModel(env.Images);
            var first = vm.NewCard(Item("first", At(1)));
            var same = vm.NewCard(Item("same", At(1)));
            var unknown = vm.NewCard(Item("unknown", null));
            vm.Cards.Add(first); vm.Cards.Add(same); vm.Cards.Add(unknown);
            vm.Update();
            var days = vm.UpcomingDays;
            var oldDay = days[0];
            var oldUnknown = days[1];
            var resets = 0;
            days.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            oldDay.Cards.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var newDate = vm.NewCard(Item("date2", At(2)));
            vm.Cards.Add(newDate);
            vm.Update();
            Assert.Equal(3, days.Count);
            Assert.True(ReferenceEquals(oldDay, days[0]));
            Assert.True(ReferenceEquals(oldUnknown, days[2]));
            Assert.True(ReferenceEquals(newDate, days[1].Cards.Single()));
            vm.Cards.Move(1, 0);
            vm.Update();
            Assert.True(ReferenceEquals(same, oldDay.Cards[0]));
            Assert.True(ReferenceEquals(first, oldDay.Cards[1]));
            first.Item.PremiereDate = At(2);
            vm.Update();
            Assert.True(ReferenceEquals(same, oldDay.Cards.Single()));
            Assert.True(ReferenceEquals(first, days[1].Cards[0]));
            vm.Cards.Remove(same);
            vm.Update();
            Assert.Equal(2, days.Count);
            Assert.True(ReferenceEquals(oldUnknown, days[1]));
            Assert.Equal(0, resets);
            vm.Cards.Clear();
            vm.Update();
            Assert.Equal(0, days.Count);
            Assert.True(ReferenceEquals(days, vm.UpcomingDays));
        });

        Test("PERF-06 真diff：中间插入、删除前缀／后缀、完整替换与随机排列均保持准确顺序", () =>
        {
            var pool = Enumerable.Range(0, 100).Select(_ => new object()).ToArray();
            var current = new ObservableCollection<object>(pool.Take(10));
            var resets = 0;
            current.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var random = new Random(6029);
            for (var pass = 0; pass < 120; pass++)
            {
                var desired = pool.OrderBy(_ => random.Next()).Take(random.Next(0, 101)).ToArray();
                UpcomingListDiff.Apply(current, desired);
                Assert.Equal(desired.Length, current.Count);
                for (var i = 0; i < desired.Length; i++) Assert.True(ReferenceEquals(desired[i], current[i]));
            }
            Assert.Equal(0, resets);
        });

        Test("PERF-06 真LoadAsync：重复页项仍消耗原始offset，旧卡保留，source-qualified ID不混", () =>
            SingleThreadPump.Run(async () =>
            {
                using var env = new MemoryEnvironment();
                var source = env.Source("server-a", "account");
                var other = env.Source("server-b", "account");
                using var vm = new LibraryViewModel(env.Images);
                vm.SwitchSource(source);
                var starts = new List<int>();
                vm.Read = (start, _) =>
                {
                    starts.Add(start);
                    var ids = start == 0 ? new[] { "one", "one", "two" } : new[] { "two", "three", "three" };
                    return Task.FromResult<ItemsResult?>(new() { Items = ids.Select(id => source.Bind(Item(id, null))).ToList(), TotalRecordCount = 20 });
                };
                await vm.Load(true);
                var first = vm.Cards[0];
                var collection = vm.Cards;
                var day = vm.UpcomingDays.Single();
                await vm.Load(false);
                Assert.True(starts.SequenceEqual(new[] { 0, 3 }));
                Assert.Equal(6, vm.ReadCount);
                Assert.Equal(3, vm.Cards.Count);
                Assert.True(ReferenceEquals(first, vm.Cards[0]));
                Assert.True(ReferenceEquals(collection, vm.Cards));
                Assert.True(ReferenceEquals(day, vm.UpcomingDays.Single()));
                vm.Cards.Add(vm.NewCard(other.Bind(Item("one", null))));
                vm.Update();
                Assert.Equal(4, day.Cards.Count);
                Assert.True(day.Cards[0].Item.IdentityKey != day.Cards[^1].Item.IdentityKey);
            }));

        Test("PERF-06 真LoadAsync：旧source迟到／离页／来源撤销都不能覆盖新请求", () =>
            SingleThreadPump.Run(async () =>
            {
                using var env = new MemoryEnvironment();
                var oldSource = env.Source("old", "account");
                var currentSource = env.Source("new", "account");
                using var vm = new LibraryViewModel(env.Images);
                var cards = vm.Cards;
                var days = vm.UpcomingDays;
                var late = new TaskCompletionSource<ItemsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.SwitchSource(oldSource);
                vm.Read = (_, _) => late.Task;
                var oldLoad = vm.Load(true);
                vm.SwitchSource(currentSource);
                vm.Read = (_, _) => Task.FromResult<ItemsResult?>(new() { Items = [currentSource.Bind(Item("same-id", null))], TotalRecordCount = 10 });
                await vm.Load(true);
                var newCard = cards.Single();
                late.SetResult(new() { Items = [oldSource.Bind(Item("same-id", null))], TotalRecordCount = 1 });
                await oldLoad;
                Assert.True(ReferenceEquals(newCard, cards.Single()));
                Assert.True(ReferenceEquals(cards, vm.Cards) && ReferenceEquals(days, vm.UpcomingDays));
                Assert.Equal(1, vm.ReadCount);
                Assert.False(vm.Busy);
                var abandoned = new TaskCompletionSource<ItemsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.Read = (_, _) => abandoned.Task;
                var leaving = vm.Load(false);
                vm.Leave();
                abandoned.SetResult(new() { Items = [currentSource.Bind(Item("after-leave", null))] });
                await leaving;
                Assert.True(ReferenceEquals(newCard, cards.Single()));
                vm.SwitchSource(currentSource);
                var revoked = new TaskCompletionSource<ItemsResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.Read = (_, _) => revoked.Task;
                var unavailable = vm.Load(true);
                env.Revoke(currentSource);
                revoked.SetResult(new() { Items = [currentSource.Bind(Item("revoked", null))] });
                await unavailable;
                Assert.Equal(0, days.Count);
                Assert.Equal(0, cards.Count);
            }));
    }

    private static DateTimeOffset At(int day) => new(2026, 11, day, 12, 0, 0, TimeSpan.Zero);
    private static EmbyItem Item(string id, DateTimeOffset? date) => new() { Id = id, Name = id, Type = EmbyItemType.Episode, PremiereDate = date };
    private sealed class CountingReferences<T> : IEqualityComparer<T> where T : class
    {
        internal long Comparisons { get; private set; }
        public bool Equals(T? x, T? y) { Comparisons++; return ReferenceEquals(x, y); }
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
}
