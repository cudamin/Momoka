using System.Text.Json;
using Momoka.Theming;

namespace Momoka.Emby;

public static partial class EmbyWebConsole
{
    /// <summary>
    /// Jellyfin Web 的原生外观。与 Emby 那份分开：Jellyfin 10.10 的管理页一半是 React／MUI 外壳，
    /// 一半仍是旧版 jQuery 表单，类名和主题机制都不一样；两套皮肤混用只会互相盖错。
    /// <para>
    /// 只改这个 WebView 里的 CSS，不写服务器主题，也不改用户其他设备上的 Jellyfin。React 外壳的
    /// 全局导航与抽屉由 Momoka 设置页替代，页签宿主则由共用规则判断；内容页自己的标题、
    /// 标签页、保存按钮都留着，网页的交互不变。
    /// </para>
    /// </summary>
    public static string JellyfinNativeSkinScript(UiPalette palette)
    {
        static string Css(ThemeColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--momoka-window"] = Css(palette.Window),
            ["--momoka-surface"] = Css(palette.Surface),
            ["--momoka-surface-alt"] = Css(palette.SurfaceAlt),
            ["--momoka-surface-elevated"] = Css(palette.SurfaceElevated),
            ["--momoka-surface-hover"] = Css(palette.SurfaceHover),
            ["--momoka-border"] = Css(palette.Border),
            ["--momoka-border-strong"] = Css(palette.BorderStrong),
            ["--momoka-text"] = Css(palette.Text),
            ["--momoka-text-dim"] = Css(palette.TextDim),
            ["--momoka-text-faint"] = Css(palette.TextFaint),
            ["--momoka-accent"] = Css(palette.Accent),
            ["--momoka-accent-hover"] = Css(palette.AccentHover),
            ["--momoka-accent-pressed"] = Css(palette.AccentPressed),
            ["--momoka-accent-soft"] = Css(palette.AccentSoft),
            ["--momoka-on-accent"] = Css(palette.TextOnAccent),
            ["--momoka-danger"] = Css(palette.Danger),
            ["--momoka-warning"] = Css(palette.Warning),
            ["--momoka-info"] = Css(palette.Info)
        };
        var variablesJson = JsonSerializer.Serialize(variables);

        const string css = """
            html, body, #reactRoot, .backgroundContainer, .backgroundContainer-transparent {
              background-color: var(--momoka-window) !important;
              background-image: none !important;
            }
            main, .mainAnimatedPages, .skinBody {
              background-color: transparent !important;
            }

            body {
              font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif !important;
              color: var(--momoka-text) !important;
              -webkit-font-smoothing: antialiased;
            }

            /* 只隐藏重复抽屉/底栏；顶栏内的原页面页签由 HideJellyfinDrawerScript 保留。 */
            .mainDrawer, .drawer-backdrop, .appfooter, .dashboardFooter,
            .btnOpenNavDrawer, .btnToggleNavDrawer, .btnPinNavDrawer {
              display: none !important;
            }
            body.dashboardDocument .MuiDrawer-root,
            body.dashboardDocument .MuiDrawer-root .MuiBackdrop-root {
              display: none !important;
            }
            body.dashboardDocument main {
              width: 100% !important;
              margin-left: 0 !important;
              padding-top: var(--momoka-page-tabs-height, 0px) !important;
            }

            h1, h2, h3, .sectionTitle, .pageTitle, .MuiTypography-h4, .MuiTypography-h5, .MuiTypography-h6 {
              font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif !important;
              color: var(--momoka-text) !important;
              letter-spacing: 0 !important;
              text-shadow: none !important;
            }
            h1, h2, .sectionTitle, .pageTitle {
              font-size: 20px !important;
              font-weight: 600 !important;
            }

            a, .button-link, .textActionButton, .cardTextActionButton {
              color: var(--momoka-accent) !important;
              text-shadow: none !important;
            }
            a:hover, .button-link:hover, .textActionButton:hover, .cardTextActionButton:hover {
              color: var(--momoka-accent-hover) !important;
            }
            .secondaryText, .fieldDescription, .listItemBodyText-secondary, .cardText-secondary,
            .toggleFieldDescription, .checkboxFieldDescription, .MuiFormHelperText-root {
              color: var(--momoka-text-dim) !important;
              text-shadow: none !important;
            }

            /* 旧版设置页：每个 fieldset 一张卡片，紧凑排布，不要白边。 */
            .view > .scrollSlider {
              padding: 24px 28px 32px !important;
            }
            .padded-top-page { padding-top: var(--momoka-page-tabs-height, 0px) !important; }
            .view:not(.padded-top-page) > .scrollSlider {
              padding-top: calc(24px + var(--momoka-page-tabs-height, 0px)) !important;
            }
            /* AppBody 的旧路由视图是绝对定位，main 的 padding 不会推开它；用同一实测偏移。 */
            body.dashboardDocument .mainAnimatedPage {
              top: var(--momoka-page-tabs-height, 0px) !important;
            }
            /* React main 或旧 mainAnimatedPage 已预留宿主高度；嵌套表单不要再留一份。 */
            body.dashboardDocument main .padded-top-page { padding-top: 0 !important; }
            body.dashboardDocument main .view > .scrollSlider { padding-top: 24px !important; }
            .settingsContainer, .dashboardContainer, .configPage, .readOnlyContent, .content-primary {
              box-sizing: border-box !important;
              max-width: none !important;
              width: 100% !important;
            }
            .settingsContainer, .dashboardContainer, .configPage, .content-primary {
              padding: 24px 28px 32px !important;
            }

            /* 旧版常规页按 verticalSection 分组，没有 fieldset；有控件的分组各成一张卡片。 */
            form.dashboardGeneralForm > .verticalSection:has(.inputContainer),
            form.dashboardGeneralForm > .verticalSection:has(.selectContainer),
            form.dashboardGeneralForm > .verticalSection:has(.checkboxContainer) {
              background: var(--momoka-surface) !important;
              border: 0 !important;
              border-radius: 12px !important;
              padding: 16px 20px !important;
              margin: 0 0 12px !important;
              box-sizing: border-box !important;
            }
            form.dashboardGeneralForm > .verticalSection:has(.inputContainer) .sectionTitle,
            form.dashboardGeneralForm > .verticalSection:has(.selectContainer) .sectionTitle,
            form.dashboardGeneralForm > .verticalSection:has(.checkboxContainer) .sectionTitle {
              font-size: 16px !important;
              font-weight: 600 !important;
            }
            form.dashboardGeneralForm .sectionTitleContainer {
              margin-bottom: 12px !important;
            }
            .fieldsets {
              gap: 12px !important;
            }
            fieldset {
              position: relative !important;
              background: var(--momoka-surface) !important;
              border: 0 !important;
              border-radius: 12px !important;
              margin: 0 !important;
              padding: 16px 20px !important;
              box-sizing: border-box !important;
            }
            fieldset:has(> legend) {
              padding-top: 48px !important;
            }
            fieldset > legend {
              position: absolute !important;
              top: 15px !important;
              left: 20px !important;
              display: block !important;
              float: none !important;
              width: auto !important;
              margin: 0 !important;
              padding: 0 !important;
              font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif !important;
              font-size: 16px !important;
              font-weight: 600 !important;
              color: var(--momoka-text) !important;
            }
            .fieldset-fields { margin: 0 !important; }
            .fieldset-field { margin: 0 0 18px !important; }
            .fieldset-fields > :last-child { margin-bottom: 0 !important; }
            .inputLabel, .selectLabelText, .emby-textarea-labeltext,
            .emby-toggle-label > span:first-of-type, .MuiInputLabel-root, .MuiFormLabel-root {
              display: block !important;
              color: var(--momoka-text) !important;
              font-size: 14px !important;
              font-weight: 400 !important;
              margin-bottom: 6px !important;
            }

            /* 输入框、下拉、文本域：WinUI 的 32 高、4 圆角。 */
            .emby-input, .emby-select, .emby-select-withcolor, .emby-textarea,
            .MuiInputBase-root, .MuiOutlinedInput-root {
              box-sizing: border-box !important;
              width: 100% !important;
              min-height: 32px !important;
              padding: 0 12px !important;
              background: var(--momoka-surface-alt) !important;
              color: var(--momoka-text) !important;
              border: 1px solid var(--momoka-border) !important;
              border-radius: 4px !important;
              box-shadow: none !important;
              font-family: inherit !important;
              font-size: 14px !important;
              font-weight: 400 !important;
            }
            .emby-input, .emby-select, .emby-select-withcolor { height: 32px !important; }
            .emby-textarea {
              height: auto !important;
              min-height: 120px !important;
              padding: 8px 12px !important;
              line-height: 1.45 !important;
            }
            .emby-input:focus, .emby-select:focus, .emby-select-withcolor:focus, .emby-textarea:focus,
            .MuiInputBase-root.Mui-focused, .MuiOutlinedInput-root.Mui-focused {
              border-color: var(--momoka-accent) !important;
              box-shadow: 0 0 0 1px var(--momoka-accent) !important;
              outline: none !important;
            }
            .emby-select-withcolor > option {
              color: var(--momoka-text) !important;
              background: var(--momoka-surface-alt) !important;
            }
            .emby-input::placeholder, .emby-textarea::placeholder,
            .MuiInputBase-input::placeholder { color: var(--momoka-text-faint) !important; }
            .selectContainer, .inputContainer, .toggleContainer { margin-bottom: 18px !important; }
            .MuiInputBase-input, .MuiSelect-select { color: var(--momoka-text) !important; }

            /* 开关：WinUI 的 40×20。MUI 的开关是同一件事的另一套类名。 */
            .toggleSwitch {
              width: 40px !important;
              height: 20px !important;
              box-sizing: border-box !important;
              border-radius: 10px !important;
              background: var(--momoka-surface-alt) !important;
              border: 1px solid var(--momoka-border-strong) !important;
            }
            .toggleSwitch::before {
              width: 12px !important;
              height: 12px !important;
              left: 3px !important;
              inset-inline-start: 3px !important;
              background: var(--momoka-text-dim) !important;
              box-shadow: none !important;
            }
            .emby-toggle:checked ~ .toggleSwitch {
              background: var(--momoka-accent) !important;
              border-color: var(--momoka-accent) !important;
            }
            .emby-toggle:checked ~ .toggleSwitch::before {
              left: calc(100% - 15px) !important;
              inset-inline-start: calc(100% - 15px) !important;
              background: var(--momoka-on-accent) !important;
            }
            .emby-toggle-label > span:first-of-type { display: inline !important; margin-bottom: 0 !important; }
            .MuiSwitch-switchBase.Mui-checked { color: var(--momoka-on-accent) !important; }
            .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track {
              background-color: var(--momoka-accent) !important;
              opacity: 1 !important;
            }
            .MuiSwitch-track {
              background-color: var(--momoka-border-strong) !important;
              opacity: 1 !important;
            }

            /* 复选框。 */
            .checkboxOutline {
              border-color: var(--momoka-border-strong) !important;
              background: var(--momoka-surface-alt) !important;
              border-radius: 4px !important;
            }
            .emby-checkbox:checked + span + .checkboxOutline {
              border-color: var(--momoka-accent) !important;
              background-color: var(--momoka-accent) !important;
            }
            .checkboxLabel { color: var(--momoka-text) !important; }
            .MuiCheckbox-root.Mui-checked, .MuiRadio-root.Mui-checked { color: var(--momoka-accent) !important; }
            .MuiCheckbox-root, .MuiRadio-root { color: var(--momoka-text-faint) !important; }

            /* 按钮：主按钮用 Momoka 强调色，其余走 WinUI 的次要按钮样子。 */
            .emby-button {
              font-family: inherit !important;
              text-transform: none !important;
              letter-spacing: 0 !important;
            }
            .button-submit, .raised.submit,
            .MuiButton-containedPrimary, .MuiButton-contained.MuiButton-colorPrimary {
              display: inline-flex !important;
              align-items: center !important;
              justify-content: center !important;
              box-sizing: border-box !important;
              height: 32px !important;
              min-height: 32px !important;
              padding: 0 16px !important;
              border: 1px solid var(--momoka-accent) !important;
              border-radius: 4px !important;
              background: var(--momoka-accent) !important;
              color: var(--momoka-on-accent) !important;
              font-size: 14px !important;
              font-weight: 600 !important;
              box-shadow: none !important;
              text-transform: none !important;
            }
            .button-submit:hover, .raised.submit:hover,
            .MuiButton-containedPrimary:hover, .MuiButton-contained.MuiButton-colorPrimary:hover {
              background: var(--momoka-accent-hover) !important;
              border-color: var(--momoka-accent-hover) !important;
            }
            .raised, .fab, .emby-button.block,
            .MuiButton-outlined, .MuiButton-text, .MuiButton-outlinedPrimary {
              box-sizing: border-box !important;
              min-height: 32px !important;
              padding: 0 12px !important;
              border: 1px solid var(--momoka-border) !important;
              border-radius: 4px !important;
              background: var(--momoka-surface-alt) !important;
              color: var(--momoka-text) !important;
              font-size: 14px !important;
              font-weight: 400 !important;
              box-shadow: none !important;
              text-transform: none !important;
            }
            .raised:hover, .fab:hover,
            .MuiButton-outlined:hover, .MuiButton-text:hover {
              background: var(--momoka-surface-hover) !important;
            }
            /* 保存按钮同时带着 raised：上面那条次要按钮规则必须在主按钮这里让开。 */
            .button-submit, .button-submit.block, .raised.submit {
              background: var(--momoka-accent) !important;
              border-color: var(--momoka-accent) !important;
              color: var(--momoka-on-accent) !important;
              font-weight: 600 !important;
            }
            .button-submit:hover, .button-submit.block:hover, .raised.submit:hover {
              background: var(--momoka-accent-hover) !important;
              border-color: var(--momoka-accent-hover) !important;
            }
            .button-submit:active, .button-submit.block:active, .raised.submit:active {
              background: var(--momoka-accent-pressed) !important;
              border-color: var(--momoka-accent-pressed) !important;
            }
            .button-submit .material-icons, .raised.submit .material-icons {
              color: var(--momoka-on-accent) !important;
            }
            .paper-icon-button-light, .MuiIconButton-root {
              border-radius: 4px !important;
              color: var(--momoka-text-dim) !important;
            }
            .paper-icon-button-light:hover, .MuiIconButton-root:hover {
              background: var(--momoka-surface-hover) !important;
              color: var(--momoka-text) !important;
            }
            .button-link, .textActionButton, .cardTextActionButton {
              background: transparent !important;
              border: 0 !important;
              box-shadow: none !important;
              padding: 0 !important;
              min-height: 0 !important;
            }
            button:focus-visible, a:focus-visible, input:focus-visible, select:focus-visible, textarea:focus-visible {
              outline: 2px solid var(--momoka-accent) !important;
              outline-offset: 2px !important;
            }

            /* 标签页与分段条。 */
            .emby-tab-button { color: var(--momoka-text-dim) !important; }
            .emby-tab-button-active { color: var(--momoka-text) !important; }
            .MuiTab-root { color: var(--momoka-text-dim) !important; text-transform: none !important; }
            .MuiTab-root.Mui-selected { color: var(--momoka-accent) !important; }
            .MuiTabs-indicator { background-color: var(--momoka-accent) !important; }

            /* 卡片、列表与信息条：和 Emby 那一套同样的面色与圆角。 */
            .MuiPaper-root, .readOnlyContent, .paperList, .visualCardBox,
            .dashboardSection, .cardBox {
              background-color: var(--momoka-surface) !important;
              color: var(--momoka-text) !important;
              border: 0 !important;
              border-radius: 12px !important;
              box-shadow: none !important;
            }
            body.dashboardDocument .MuiDrawer-root .MuiDrawer-paper { display: none !important; }
            .dashboardSections { display: flex !important; flex-wrap: wrap !important; gap: 12px !important; }
            .dashboardSection { padding: 16px 20px !important; }
            .dashboardSection:not(:has(*)) { display: none !important; }
            .infoBanner, .warningBanner, .betaInfoBanner {
              background: var(--momoka-surface-alt) !important;
              border: 0 !important;
              border-radius: 6px !important;
              color: var(--momoka-text) !important;
              box-shadow: none !important;
            }
            .infoBannerIcon, .serverUpToDateCheckIcon { color: var(--momoka-accent) !important; }

            .listItem { border-bottom: 1px solid var(--momoka-border) !important; }
            .listItem:last-child { border-bottom: 0 !important; }
            .listItem-content, .listItem-content-bg {
              background: transparent !important;
              border: 0 !important;
              border-radius: 6px !important;
            }
            .listItem-hoverable:hover .listItem-content,
            .listItem-hoverable:hover .listItem-content-bg { background: var(--momoka-surface-hover) !important; }
            .listItemImageContainer, .defaultCardBackground {
              background: var(--momoka-surface-alt) !important;
              background-image: none !important;
            }
            .listItemBodyText { color: var(--momoka-text) !important; text-shadow: none !important; }
            .MuiListItem-root, .MuiMenuItem-root { color: var(--momoka-text) !important; }
            .MuiListItem-root:hover, .MuiMenuItem-root:hover {
              background-color: var(--momoka-surface-hover) !important;
            }
            .MuiDivider-root { border-color: var(--momoka-border) !important; }

            .itemsViewSettingsContainer { justify-content: flex-start !important; gap: 8px !important; padding: 0 0 16px !important; }
            .itemsContainer { justify-content: flex-start !important; }
            .listTotalRecordCount { color: var(--momoka-text-dim) !important; font-size: 14px !important; }
            .cardBox { overflow: hidden !important; }
            .cardContent, .cardContent-button, .cardContent-background,
            .cardImageContainer, .defaultCardBackground {
              border-radius: 12px !important;
              box-shadow: none !important;
              background-color: var(--momoka-surface-alt) !important;
              background-image: none !important;
            }
            .cardText, .cardText-first { color: var(--momoka-text) !important; text-shadow: none !important; }
            .cardTextActionButton { color: var(--momoka-text) !important; font-weight: 600 !important; }
            .cardTextActionButton:hover { color: var(--momoka-accent) !important; }

            /* 媒体资料管理器：网页自己的固定目录树，深色卡片与强调色图标。 */
            .editPageSidebar {
              background: var(--momoka-surface) !important;
              border-right: 1px solid var(--momoka-border) !important;
            }
            #editItemMetadataPage { padding-top: 24px !important; }
            .metadataSidebarIcon { color: var(--momoka-accent) !important; }
            .jstree-anchor { color: var(--momoka-text) !important; }
            .jstree-anchor.jstree-hovered,
            .jstree-anchor.jstree-clicked {
              background: var(--momoka-surface-hover) !important;
              color: var(--momoka-text) !important;
              box-shadow: none !important;
            }

            /* 表格是管理页里最常见的密度来源，别让它自带灰底。 */
            .MuiTable-root { background: transparent !important; }
            .MuiTableCell-root {
              color: var(--momoka-text) !important;
              border-bottom-color: var(--momoka-border) !important;
            }
            .MuiTableHead-root .MuiTableCell-root { color: var(--momoka-text-dim) !important; }

            /* 系统滚动条与选区。 */
            ::-webkit-scrollbar { width: 12px !important; height: 12px !important; background: transparent !important; }
            ::-webkit-scrollbar-track { background: transparent !important; }
            ::-webkit-scrollbar-thumb {
              background-color: var(--momoka-border-strong) !important;
              border: 4px solid transparent !important;
              background-clip: padding-box !important;
              border-radius: 8px !important;
            }
            ::-webkit-scrollbar-thumb:hover {
              background-color: var(--momoka-text-dim) !important;
              background-clip: padding-box !important;
            }
            ::selection { background: var(--momoka-accent) !important; color: var(--momoka-on-accent) !important; }
            """;

        var cssLiteral = JsonSerializer.Serialize(css);

        return $$"""
            (function () {
              try {
                var vars = {{variablesJson}};
                var css = {{cssLiteral}};

                function apply() {
                  try {
                    var root = document.documentElement;
                    if (!root) return;
                    for (var name in vars) root.style.setProperty(name, vars[name], "important");

                    var style = document.getElementById("momoka-jellyfin");
                    if (!style) {
                      style = document.createElement("style");
                      style.id = "momoka-jellyfin";
                    }
                    style.textContent = css;
                    // 网页的主题样式表是启动后插进 <head> 的；每次重试都把自己的样式挪到最后。
                    (document.head || root).appendChild(style);
                  } catch (e) {
                    // 一次没赶上不要紧，后面还有 DOMContentLoaded 与定时器。
                  }
                }

                apply();
                document.addEventListener("DOMContentLoaded", apply, { once: true });
                window.addEventListener("load", apply, { once: true });
                setTimeout(apply, 300);
                setTimeout(apply, 1200);
                setTimeout(apply, 2500);
              } catch (e) {
                // 皮肤没套上不该把控制台变成白屏。
              }
            })();
            """;
    }
}
