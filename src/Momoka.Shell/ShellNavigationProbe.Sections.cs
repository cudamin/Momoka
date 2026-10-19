using Momoka.Infrastructure;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// Which <c>--probe-section</c> area each case belongs to. Lambda cases pass their section
    /// explicitly at the call site; a case missing from here makes the probe record a failed check.
    /// </summary>
    private static readonly Dictionary<string, string> CaseSections = new(StringComparer.Ordinal)
    {
        // 外壳：登录、侧边栏、历史、身份切换、通知、诊断与对话框
        [nameof(SignInAppearanceAsync)] = ShellProbeSections.Shell,
        [nameof(SidebarAsync)] = ShellProbeSections.Shell,
        [nameof(NotificationEmptyAsync)] = ShellProbeSections.Shell,
        [nameof(NotificationCancelAsync)] = ShellProbeSections.Shell,
        [nameof(StaleCardAsync)] = ShellProbeSections.Shell,
        [nameof(ShellHistoryAsync)] = ShellProbeSections.Shell,
        [nameof(ShellIdentityAsync)] = ShellProbeSections.Shell,
        [nameof(ShellSwitchRaceAsync)] = ShellProbeSections.Shell,
        [nameof(ShellDirectoryRetryAsync)] = ShellProbeSections.Shell,
        [nameof(DiagnosticsEmptyAsync)] = ShellProbeSections.Shell,
        [nameof(DialogConstructionAsync)] = ShellProbeSections.Shell,

        // 设置：设置页、服务器页、Jellyfin 控制台与对话框
        [nameof(SettingsEntranceAsync)] = ShellProbeSections.Settings,
        [nameof(SettingsHomeLibraryPlacementAsync)] = ShellProbeSections.Settings,
        [nameof(SettingsStartupRegistrationAsync)] = ShellProbeSections.Settings,
        [nameof(SettingsAdministrationGateAsync)] = ShellProbeSections.Settings,
        [nameof(MultiServerSettingsGroupsAsync)] = ShellProbeSections.Settings,
        [nameof(ServersPageSummaryAsync)] = ShellProbeSections.Settings,
        [nameof(JellyfinConsoleNavigationAsync)] = ShellProbeSections.Settings,
        [nameof(JellyfinDialogCapabilitiesAsync)] = ShellProbeSections.Settings,

        // 媒体库：浏览、详情、集页、文件选项与主页行
        [nameof(EpisodeBandAsync)] = ShellProbeSections.Library,
        [nameof(SeriesCardDatesAsync)] = ShellProbeSections.Library,
        [nameof(LibraryBrowseAsync)] = ShellProbeSections.Library,
        [nameof(FnNasLibrarySpacingAsync)] = ShellProbeSections.Library,
        [nameof(LibraryFavoritesAsync)] = ShellProbeSections.Library,
        [nameof(LibraryUpcomingAsync)] = ShellProbeSections.Library,
        [nameof(DetailWriteAsync)] = ShellProbeSections.Library,
        [nameof(DetailPickerLayoutAsync)] = ShellProbeSections.Library,
        [nameof(PickerDropDownAsync)] = ShellProbeSections.Library,
        [nameof(HiddenHomeAsync)] = ShellProbeSections.Library,
        [nameof(SeasonTrailingPlayAsync)] = ShellProbeSections.Library,

        // 搜索：查询状态、分页、切季与导航竞态
        [nameof(ResumeQueryAsync)] = ShellProbeSections.Search,
        [nameof(SeasonRefreshRaceAsync)] = ShellProbeSections.Search,
        [nameof(NavigationRestoreRaceAsync)] = ShellProbeSections.Search,
        [nameof(PagingFailureAsync)] = ShellProbeSections.Search,
        [nameof(PagingCancelAsync)] = ShellProbeSections.Search,
        [nameof(QueryFailureAsync)] = ShellProbeSections.Search,
        [nameof(SearchStateAsync)] = ShellProbeSections.Search,
        [nameof(SearchInteractionAsync)] = ShellProbeSections.Search,
        [nameof(SearchHeaderLayoutAsync)] = ShellProbeSections.Search,
        [nameof(JumpCancelAsync)] = ShellProbeSections.Search,
        [nameof(SeasonCancelAsync)] = ShellProbeSections.Search,
        [nameof(SeasonFailureAsync)] = ShellProbeSections.Search,
        [nameof(EmptySeasonAsync)] = ShellProbeSections.Search,

        // MoviePilot：入口显隐、主页混排、资源与订阅
        [nameof(MoviePilotSidebarAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotAvailabilityAsync)] = ShellProbeSections.MoviePilot,
        [nameof(HomeOrderAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotUnavailableNavigationAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMenusAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSearchSitesAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotResourceOptionsAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionsAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionDetailAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMovieSubscriptionDetailAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotAnimeSubscriptionDetailAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionIdentityAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionPosterRefreshAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionFilesRecoveryAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionRecreateAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSubscriptionReviewAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMediaBrowsingAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSeasonSelectionAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMediaReadRacesAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMediaBackgroundAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotSeasonPartialSubmitAsync)] = ShellProbeSections.MoviePilot,
        [nameof(MoviePilotMediaSubtitlesAsync)] = ShellProbeSections.MoviePilot,

        // AniBT：搜索、分集与逐文件整理
        [nameof(AniBtSearchAsync)] = ShellProbeSections.AniBt,
        [nameof(AniBtEpisodeSearchAsync)] = ShellProbeSections.AniBt,
        [nameof(AniBtScrapeAsync)] = ShellProbeSections.AniBt,

        // 多账号与多来源
        [nameof(AggregateBrowsingAsync)] = ShellProbeSections.Accounts,
        [nameof(SourceCaptionTransitionsAsync)] = ShellProbeSections.Accounts,
        [nameof(MultiSourceSignInAsync)] = ShellProbeSections.Accounts,
        [nameof(MultiSourceDefaultExpiryAsync)] = ShellProbeSections.Accounts,

        // 封面选择、写入与迟到结果
        [nameof(CoverPickRaceAsync)] = ShellProbeSections.Covers,
        [nameof(CoverRefreshAsync)] = ShellProbeSections.Covers,
        [nameof(CoverCancelAsync)] = ShellProbeSections.Covers,
        [nameof(CoverWriteAsync)] = ShellProbeSections.Covers,
        [nameof(CoverUncertainAsync)] = ShellProbeSections.Covers,
        [nameof(CoverLateImageAsync)] = ShellProbeSections.Covers,

        // 收藏与删除
        [nameof(FavoritesNavigationAsync)] = ShellProbeSections.Favorites,
        [nameof(FavoritesRaceAsync)] = ShellProbeSections.Favorites,
        [nameof(DeletePreservesListAsync)] = ShellProbeSections.Favorites,
        [nameof(DeletePagingRaceAsync)] = ShellProbeSections.Favorites,
        [nameof(DeleteContainerAsync)] = ShellProbeSections.Favorites,
        [nameof(DeleteDetailCardAsync)] = ShellProbeSections.Favorites,

        // 播放返回、加载中禁播与继续观看
        [nameof(WatchedResumeSlotAsync)] = ShellProbeSections.Playback,
        [nameof(PlaybackBusyAsync)] = ShellProbeSections.Playback,
        [nameof(HomePlaybackReturnAsync)] = ShellProbeSections.Playback,
    };
}
