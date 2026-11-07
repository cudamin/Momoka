namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["home-carousel-fixture"])
        {
            HomeCarouselTests.Register();
            GaussianBackdropTests.Register();
            return TestHarness.Run();
        }
        if (args is ["verification-harness-fixture"])
        {
            TestHarnessTests.Register();
            TestEntryPointTests.Register();
            StubTransportTests.Register();
            return TestHarness.Run();
        }
        if (args is ["signin-server-binding-fixture", var bindingShell, var bindingRoot])
            return SignInServerBindingTests.Run(bindingShell, bindingRoot);
        if (args is ["signin-server-binding-core-fixture"])
        {
            SignInServerBindingTests.RegisterCore();
            return TestHarness.Run();
        }
        if (args is ["subtitle-target-fixture"])
        {
            SubtitleManagementTests.Register();
            SubtitleTargetTests.Register();
            ItemActionTests.Register();
            JellyfinProtocolTests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-cookie-ownership-fixture"])
        {
            MoviePilotCookieOwnershipTests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-boundary-fixture"])
        {
            MoviePilotSafetyTests.Register();
            AniBtIntegrationTests.Register();
            return TestHarness.Run();
        }
        if (args is ["episode-removal-fixture"])
        {
            EpisodeRemovalTests.Register();
            return TestHarness.Run();
        }
        if (args is ["detail-customization-fixture"])
        {
            DetailLayoutTests.Register();
            GaussianBackdropTests.Register();
            SettingsLayoutTests.Register();
            return TestHarness.Run();
        }
        if (args is ["toolbar-icon-fixture"])
        {
            ToolbarIconTests.Register();
            return TestHarness.Run();
        }
        if (args is ["account-menu-fixture"])
        {
            AccountMenuTests.Register();
            return TestHarness.Run();
        }
        if (args is ["media-source-size-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            MediaSourceSizeTests.Register();
            return TestHarness.Run();
        }
        if (MpvProcessTests.IsChild(args)) return MpvProcessTests.RunChildAsync(args).GetAwaiter().GetResult();
        if (args is ["selfcheck-mutex-fixture", var source, var target])
            return SelfCheckTests.RunMutexChild(source, target);
        if (args is ["test-harness-fixture", var scenario])
            return TestHarnessTests.RunChild(scenario);
        if (args is ["activation-listener-fixture", var activationScenario])
            return ActivationListenerTests.RunChild(activationScenario);
        if (args is ["artwork-preview-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            ArtworkPreviewTests.Register();
            return TestHarness.Run();
        }
        if (args is ["navigation-timing-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            NavigationTimingTests.Register();
            return TestHarness.Run();
        }
        if (args is ["performance-probe-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            StartupArgsTests.Register();
            PerformanceProbeRulesTests.Register();
            return TestHarness.Run();
        }
        if (args is ["input-focus-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            InputFocusThemeTests.Register();
            NeutralFeedbackTests.Register();
            return TestHarness.Run();
        }
        if (args is ["acrylic-controls-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            AcrylicControlsTests.Register();
            return TestHarness.Run();
        }
        if (args is ["input-theme-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            InputControlThemeTests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-media-management-fixture"])
        {
            MoviePilotMediaManagementTests.Register();
            return TestHarness.Run();
        }
        if (args is ["composition-readiness-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            CompositionPositionReadinessTests.Register();
            CompositionProbePixelsTests.Register();
            ProbeEvidenceTests.Register();
            CompactImagesProbeTests.Register();
            return TestHarness.Run();
        }
        if (args is ["settings-storage-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SettingsTests.Register();
            return TestHarness.Run();
        }
        if (args is ["device-header-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            DeviceHeaderTransmissionTests.Register();
            return TestHarness.Run();
        }
        if (args is ["danmaku-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            DanmakuTests.Register();
            return TestHarness.Run();
        }
        if (args is ["mpv-ui-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            MpvUiTests.Register();
            return TestHarness.Run();
        }
        if (args is ["selfcheck-layout-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SelfCheckLayoutRulesTests.Register();
            return TestHarness.Run();
        }
        if (args is ["startup-state-contract-fixture", var startupShell, var startupRoot])
            return StartupStateContractTests.Run(startupShell, startupRoot);
        if (args is ["startup-state-contract-fixture", var startupCompatShell, var startupCompatRoot, "desired-invariants"])
            return StartupStateContractTests.Run(startupCompatShell, startupCompatRoot);
        if (args is ["startup-reapply-notice-fixture", var reapplyShell, var reapplyRoot])
            return StartupReapplyNoticeTests.Run(reapplyShell, reapplyRoot);
        if (args is ["startup-reapply-notice-fixture", var reapplyCompatShell, var reapplyCompatRoot, "desired-invariants"])
            return StartupReapplyNoticeTests.Run(reapplyCompatShell, reapplyCompatRoot);
        if (args is ["settings-search-fixture", var searchShell, var searchRoot])
            return SettingsSearchTests.Run(searchShell, searchRoot);
        if (args is ["settings-page-state-fixture", var shellAssembly, var sourceRoot])
            return SettingsPageStateTests.Run(shellAssembly, sourceRoot);
        if (args is ["settings-applicability-fixture", var applicabilityShell, var applicabilityRoot])
            return SettingsApplicabilityTests.Run(applicabilityShell, applicabilityRoot);
        if (args is ["library-resume-paging-fixture", var resumeShell, var resumeRoot])
            return LibraryResumePagingTests.Run(resumeShell, resumeRoot);
        if (args is ["library-deep-return-fixture", var deepShell, var deepRoot])
            return LibraryDeepReturnTests.Run(deepShell, deepRoot);
        if (args is ["home-performance-fixture", var homeShell, var homeRoot])
            return HomePerformanceTests.Run(homeShell, homeRoot);
        if (args is ["detail-deferred-cards-fixture", var detailShell, var detailRoot])
            return DetailDeferredCardsTests.Run(detailShell, detailRoot);
        if (args is ["budget-cache-fixture"])
        {
            BudgetCacheTests.Register();
            return TestHarness.Run();
        }
        if (args is ["library-favorite-source-fixture", var favoriteShell, var favoriteRoot])
            return LibraryFavoriteSourceTests.Run(favoriteShell, favoriteRoot);
        if (args is ["settings-screenshot-restore-fixture", var screenshotShell, var screenshotRoot])
            return SettingsScreenshotRestoreTests.Run(screenshotShell, screenshotRoot);
        if (args is ["settings-screenshot-restore-fixture", var screenshotCompatShell, var screenshotCompatRoot, "desired-invariants"])
            return SettingsScreenshotRestoreTests.Run(screenshotCompatShell, screenshotCompatRoot);
        if (args is ["fnnas-library-editor-fixture", var editorShell, var editorRoot])
            return FnNasLibraryEditorTests.Run(editorShell, editorRoot);
        if (args is ["fnnas-user-state-fixture", var stateShell, var stateRoot])
            return FnNasUserStateWriteTests.Run(stateShell, stateRoot);
        if (args is ["subtitle-forced-only-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SubtitleForcedOnlyTests.Register();
            return TestHarness.Run();
        }
        if (args is ["player-track-readback-fixture", var trackShell, var trackRoot])
            return PlayerVolumeGestureTests.RunTracks(trackShell, trackRoot);
        if (args is ["player-volume-gesture-fixture", var volumeShell, var volumeRoot])
            return PlayerVolumeGestureTests.Run(volumeShell, volumeRoot);
        if (args is ["svp-pipe-child", var svpPipe]) return SvpRuntimeTests.RunPipeChild(svpPipe);
        if (args is ["svp-native-fixture", var svpNative]) return SvpRuntimeTests.RunNative(svpNative);
        if (args is ["svp-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SvpTests.Register();
            SvpRuntimeTests.Register();
            return TestHarness.Run();
        }
        if (args is ["video-sync-review-fixture", var videoShell, var videoRoot])
            return SettingsApplicabilityTests.Run(videoShell, videoRoot, videoOnly: true);
        if (args is ["playback-review-fixture"])
        {
            PlaybackTests.Register();
            SubtitleBehaviorTests.Register();
            return TestHarness.Run();
        }
        if (args is ["audio-output-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            AudioOutputTests.Register();
            return TestHarness.Run();
        }
        if (args is ["selfcheck-output-isolation-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            SelfCheckTests.Register();
            SelfCheckOutputIsolationTests.Register();
            return TestHarness.Run();
        }
        if (args is ["http-download-error-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            HttpDownloadErrorTests.Register();
            HttpDeadlineTests.Register();
            HttpDiagnosticsTests.Register();
            return TestHarness.Run();
        }
        if (args is ["restore-identity-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            RestoreIdentityR003Tests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-login-redirect-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            MoviePilotLoginRedirectTests.Register();
            return TestHarness.Run();
        }
        if (args is ["fnnas-protocol-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            FnNasProtocolTests.Register();
            FnNasLibraryInputTests.Register();
            FnNasUserStateWriteTests.Register();
            FnNasMediaTests.Register();
            return TestHarness.Run();
        }
        if (args is ["plex-protocol-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PlexProtocolTests.Register();
            PlexMediaTests.Register();
            return TestHarness.Run();
        }
        if (args is ["deep-browse-protocol-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            DeepBrowseProtocolTests.Register();
            return TestHarness.Run();
        }
        if (args is ["plex-login-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PlexV2LoginTests.Register();
            return TestHarness.Run();
        }
        if (args is ["realtime-handshake-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            EmbyRealtimeTests.Register();
            return TestHarness.Run();
        }
        if (args is ["emby-metadata-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            EmbyMetadataEditorTests.Register();
            DialogChromeTests.Register();
            return TestHarness.Run();
        }
        if (args is ["metadata-date-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            ItemMetadataDateTests.Register();
            return TestHarness.Run();
        }
        if (args is ["anibt-receipt-state-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            AniBtReceiptStateTests.Register();
            return TestHarness.Run();
        }
        if (args is ["detail-subject-removal-fixture", var removalShell, var removalRoot])
            return DetailSubjectRemovalTests.Run(removalShell, removalRoot);
        if (args is ["detail-action-readiness-fixture", var readinessShell, var readinessRoot])
            return DetailActionReadinessTests.Run(readinessShell, readinessRoot);
        if (args is ["marked-watched-source-fixture"])
            return MarkedWatchedSourceTests.Run();
        if (args is ["moviepilot-episode-group-playback-fixture"])
            return MoviePilotEpisodeGroupPlaybackTests.Run();
        if (args is ["moviepilot-subscription-page-state-fixture", var subscriptionShell, var subscriptionRoot])
            return MoviePilotSubscriptionPageStateTests.Run(subscriptionShell, subscriptionRoot);
        if (args is ["moviepilot-series-transfer-fixture"])
        {
            MoviePilotSeriesTransferTests.Register();
            MoviePilotTransferHistoryViewTests.Register();
            MoviePilotTransferTests.Register();
            MoviePilotAuditTests.Register();
            MoviePilotTransferSubmissionTests.Register();
            return TestHarness.Run();
        }
        if (args is ["moviepilot-transfer-submission-fixture"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            MoviePilotTransferSubmissionTests.Register();
            return TestHarness.Run();
        }
        if (args.Length != 0)
        {
            Console.Error.WriteLine("未知测试参数或参数数量错误；无参数运行完整套件，请按专项文档提供参数。");
            return 2;
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Momoka Core 测试");
        Console.WriteLine();

        TestHarnessTests.Register();
        TestEntryPointTests.Register();
        StubTransportTests.Register();
        ActivationListenerTests.Register();
        PlaybackTests.Register();
        EpisodeRemovalTests.Register();
        MarkedWatchedSourceTests.Register();
        KeywordPriorityTests.Register();
        PlaybackLifecycleTests.Register();
        MediaVersionDiscoveryTests.Register();
        MediaSourceSizeTests.Register();
        BackendLifecycleTests.Register();
        MpvCustomConfigTests.Register();
        AudioOutputTests.Register();
        VideoOutputTests.Register();
        Anime4KTests.Register();
        ShaderTransactionTests.Register();
        PlaybackTrackStateTests.Register();
        SubtitleFileTests.Register();
        SubtitleBehaviorTests.Register();
        SubtitleForcedOnlyTests.Register();
        SubtitleManagementTests.Register();
        SubtitleTargetTests.Register();
        MpvProcessTests.Register();
        SelfCheckTests.Register();
        SelfCheckOutputIsolationTests.Register();
        SettingsTests.Register();
        SettingsLayoutTests.Register();
        ToolbarIconTests.Register();
        StartupEntryTests.Register();
        NotificationTests.Register();
        ServerSettingsTests.Register();
        ServerAccessTests.Register();
        ServerOrderingTests.Register();
        ShortcutTests.Register();
        SeekKeyTests.Register();
        ServiceTests.Register();
        FontTests.Register();
        HtmlColorTests.Register();
        SubtitlePreviewTests.Register();
        EmbyTests.Register();
        ItemMetadataDateTests.Register();
        EmbyMetadataEditorTests.Register();
        EmbyRealtimeTests.Register();
        FnNasProtocolTests.Register();
        FnNasLibraryInputTests.Register();
        FnNasUserStateWriteTests.Register();
        FnNasMediaTests.Register();
        PlexProtocolTests.Register();
        PlexMediaTests.Register();
        JellyfinWebConsoleTests.Register();
        SessionTests.Register();
        IdentityRegressionTests.Register();
        HttpRedirectTests.Register();
        HttpDiagnosticsTests.Register();
        FailurePathTests.Register();
        ImageCacheTests.Register();
        BudgetCacheTests.Register();
        BrowseSnapshotTests.Register();
        DeepBrowseProtocolTests.Register();
        ImageIdentityTests.Register();
        BrowsingPreferencesTests.Register();
        LibrarySearchTests.Register();
        AggregateQueryTests.Register();
        LibraryBrowseTests.Register();
        LibraryRecommendedTests.Register();
        LibraryFavoritesTests.Register();
        LibraryUpcomingTests.Register();
        LibraryDisplayTests.Register();
        LibraryEpisodeFolderTests.Register();
        LibraryIndexTests.Register();
        MoviePilotSearchSitesTests.Register();
        MoviePilotResourceOptionsTests.Register();
        MoviePilotAvailabilityTests.Register();
        MoviePilotMediaDetailTests.Register();
        ItemDetailTests.Register();
        ItemArtworkTests.Register();
        ArtworkPreviewTests.Register();
        ItemMenuTests.Register();
        ItemIdentifyTests.Register();
        ItemActionTests.Register();
        HomeCarouselTests.Register();
        HomeFoldTests.Register();
        BackdropBlurTests.Register();
        GaussianBackdropTests.Register();
        HomeLayoutTests.Register();
        DetailLayoutTests.Register();
        RemovedMediaTests.Register();
        HomeLatestTests.Register();
        LibraryTotalTests.Register();
        HomeResumeTextTests.Register();
        ResumeRowTests.Register();
        UiLifecycleTests.Register();
        StartIntentTests.Register();
        DiagnosticsTests.Register();
        NavigationTimingTests.Register();
        ScreenTests.Register();
        CardStripTests.Register();
        NeutralFeedbackTests.Register();
        InputFocusThemeTests.Register();
        CardSizeTests.Register();
        WrapLayoutTests.Register();
        StartupArgsTests.Register();
        ThemeTests.Register();
        InputControlThemeTests.Register();
        AcrylicControlsTests.Register();
        DialogChromeTests.Register();
        AccountMenuTests.Register();
        PlayerPaletteTests.Register();
        PlayerMotionTests.Register();
        PlayerInteractionTests.Register();
        CoverArtworkTests.Register();
        PictureRevealTests.Register();
        StatusCoalescerTests.Register();
        ResizeFreezeTests.Register();
        VolumeScaleTests.Register();
        TimelineScaleTests.Register();
        TimelineParityTests.Register();
        SpeedWheelTests.Register();
        MpvUiTests.Register();
        MediaVersionTests.Register();
        ReleaseGroupTests.Register();
        InlineSwitchTests.Register();
        SvpTests.Register();
        SvpRuntimeTests.Register();
        WindowFormTests.Register();
        PinIndicatorTests.Register();
        AboutTests.Register();
        DanmakuTests.Register();
        UpdateTests.Register();
        MoviePilotTests.Register();
        AniBtTests.Register();
        AniBtSeriesIdentityTests.Register();
        AniBtPrepareConcurrencyTests.Register();
        AniBtIntegrationTests.Register();
        AniBtReceiptStateTests.Register();
        MoviePilotSubscriptionTests.Register();
        MoviePilotEpisodeGroupPlaybackTests.Register();
        MoviePilotAuditTests.Register();
        MoviePilotMediaManagementTests.Register();
        MoviePilotTransferTests.Register();
        MoviePilotSeriesTransferTests.Register();
        MoviePilotTransferHistoryViewTests.Register();
        MoviePilotTransferSubmissionTests.Register();
        MoviePilotVersionQueryTests.Register();
        MoviePilotEpisodePatternTests.Register();
        SelfCheckLayoutRulesTests.Register();
        CompositionPositionReadinessTests.Register();
        CompositionProbePixelsTests.Register();
        ProbeEvidenceTests.Register();
        CompactImagesProbeTests.Register();
        PerformanceProbeRulesTests.Register();
        AgreementsTests.Register();

        return TestHarness.Run();
    }
}
