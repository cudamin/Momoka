using System.Text.Json;
using Momoka.Theming;

namespace Momoka.Emby;

public static partial class EmbyWebConsole
{
    /// <summary>
    /// 把网页端那套 Material／Emby 皮肤往 WinUI 的样子拉：Segoe 字体、卡片分组、32 高的输入框和下拉、
    /// 40×20 的开关、4 圆角按钮、平铺的设备卡片、系统滚动条。只改这个 WebView 里的样式，不写服务器。
    /// <para>
    /// 选择器来自这台服务器 4.10 的真实页面结构（设置页的 <c>fieldset</c>／<c>fieldset-field</c>、
    /// 设备页和用户页的 <c>cardBox</c>、活动列表的 <c>listItem</c>），不是照截图猜的。每一条都带
    /// <c>!important</c>：网页自己的主题样式是启动后动态插进来的，光靠先后顺序靠不住。
    /// </para>
    /// <para>
    /// 页面还留着网页端自己的交互（保存、下拉、开关都还是网页自己的控件）；这里改的是外观，不动行为。
    /// </para>
    /// </summary>
    public static string NativeSkinScript(UiPalette palette)
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
            html, .backgroundContainer {
              background-color: var(--momoka-window) !important;
            }

            body {
              font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif !important;
              color: var(--momoka-text) !important;
              -webkit-font-smoothing: antialiased;
            }

            /* 网页端自己的顶栏、底栏和页脚都不属于 WinUI 的这一页。 */
            .skinHeader, .skinHeader-withBackground, .appfooter, .dashboardFooter {
              display: none !important;
            }

            a, .button-link, .cardTextActionButton, .textActionButton {
              color: var(--momoka-accent) !important;
              text-shadow: none !important;
            }
            a:hover, .button-link:hover, .cardTextActionButton:hover, .textActionButton:hover {
              color: var(--momoka-accent-hover) !important;
            }
            .secondaryText, .fieldDescription, .listItemBodyText-secondary, .cardText-secondary,
            .toggleFieldDescription, .checkboxFieldDescription {
              color: var(--momoka-text-dim) !important;
              text-shadow: none !important;
            }

            h1, h2, h3, .sectionTitle, .serverNameHeader, .pageTitle {
              font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif !important;
              color: var(--momoka-text) !important;
              letter-spacing: 0 !important;
              text-shadow: none !important;
            }
            h1, h2, .sectionTitle, .serverNameHeader {
              font-size: 20px !important;
              font-weight: 600 !important;
            }
            h3 {
              font-size: 14px !important;
              font-weight: 600 !important;
            }

            /* 页面边距：HideDrawerScript 为了去掉顶栏把 padded-* 归零了，这里按 WinUI 的页面边距补回来。 */
            .view > .scrollSlider {
              padding: 24px 28px 32px !important;
            }
            .settingsContainer, .dashboardContainer {
              padding: 24px 32px 32px !important;
            }

            /* ---- 设置页：每个 fieldset 一张卡片 ---- */
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
            .fieldset-fields {
              margin: 0 !important;
            }
            .fieldset-field {
              margin: 0 0 18px !important;
            }
            .fieldset-fields > :last-child {
              margin-bottom: 0 !important;
            }
            .inputLabel, .selectLabelText, .emby-textarea-labeltext,
            .emby-toggle-label > span:first-of-type {
              display: block !important;
              color: var(--momoka-text) !important;
              font-size: 14px !important;
              font-weight: 400 !important;
              margin-bottom: 6px !important;
            }
            .fieldDescription {
              font-size: 12px !important;
              line-height: 1.45 !important;
              margin-top: 6px !important;
            }

            /* ---- 输入框、下拉、文本域 ---- */
            .emby-input, .emby-select, .emby-textarea {
              box-sizing: border-box !important;
              width: 100% !important;
              height: 32px !important;
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
            .emby-textarea {
              height: auto !important;
              min-height: 120px !important;
              padding: 8px 12px !important;
              line-height: 1.45 !important;
            }
            .emby-input:focus, .emby-select:focus, .emby-textarea:focus {
              border-color: var(--momoka-accent) !important;
              box-shadow: 0 0 0 1px var(--momoka-accent) !important;
              outline: none !important;
            }
            .emby-input::placeholder, .emby-textarea::placeholder {
              color: var(--momoka-text-faint) !important;
            }
            .emby-select-wrapper {
              height: 32px !important;
            }
            .selectContainer, .inputContainer, .toggleContainer {
              margin-bottom: 18px !important;
            }

            /* ---- 开关：WinUI 的 40×20 轨道 ---- */
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
            .emby-toggle-label > span:first-of-type {
              display: inline !important;
              margin-bottom: 0 !important;
            }

            /* ---- 复选框：16 的方框，对勾跟着走 ---- */
            .emby-checkbox + .checkboxLabel::before {
              width: 16px !important;
              height: 16px !important;
              margin-top: 0 !important;
              border: 1px solid var(--momoka-border-strong) !important;
              border-radius: 4px !important;
              background: var(--momoka-surface-alt) !important;
              box-sizing: border-box !important;
            }
            .emby-checkbox:checked + .checkboxLabel::before {
              background: var(--momoka-accent) !important;
              border-color: var(--momoka-accent) !important;
            }
            .emby-checkbox:checked + .checkboxLabel::after {
              color: var(--momoka-on-accent) !important;
              top: 10px !important;
              left: 3px !important;
              height: 4px !important;
              width: 10px !important;
              border-left-width: 2px !important;
              border-bottom-width: 2px !important;
            }
            .checkboxLabel::after {
              left: 3px !important;
              inset-inline-start: 3px !important;
            }

            /* ---- 按钮 ---- */
            .emby-button {
              font-family: inherit !important;
              text-transform: none !important;
              letter-spacing: 0 !important;
            }
            .button-submit, .raised.submit {
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
            }
            .button-submit:hover, .raised.submit:hover {
              background: var(--momoka-accent-hover) !important;
              border-color: var(--momoka-accent-hover) !important;
            }
            .button-submit:active, .raised.submit:active {
              background: var(--momoka-accent-pressed) !important;
              border-color: var(--momoka-accent-pressed) !important;
            }
            .button-submit .md-icon, .raised.submit .md-icon {
              color: var(--momoka-on-accent) !important;
              margin: 0 6px 0 0 !important;
            }
            .raised, .fab, .button-submit.block {
              box-sizing: border-box !important;
              height: 32px !important;
              min-height: 32px !important;
              padding: 0 12px !important;
              border: 1px solid var(--momoka-border) !important;
              border-radius: 4px !important;
              background: var(--momoka-surface-alt) !important;
              color: var(--momoka-text) !important;
              font-size: 14px !important;
              font-weight: 400 !important;
              box-shadow: none !important;
            }
            .raised:hover, .fab:hover {
              background: var(--momoka-surface-hover) !important;
            }
            .fab, .fab-mini {
              width: 32px !important;
              min-width: 32px !important;
              padding: 0 !important;
            }
            .button-submit.block {
              width: auto !important;
              min-width: 88px !important;
              margin-left: auto !important;
            }
            form.dashboardGeneralForm > div:last-child {
              display: flex !important;
              justify-content: flex-end !important;
            }
            /* 主按钮放在 .raised 之后再说一遍：网页里“连接帮助”同时带着 raised 类，
               否则会被上面那条次要按钮的底色盖掉。 */
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
            .button-submit .md-icon, .button-submit.block .md-icon, .raised.submit .md-icon {
              color: var(--momoka-on-accent) !important;
            }
            .button-link, .textActionButton, .cardTextActionButton {
              background: transparent !important;
              border: 0 !important;
              box-shadow: none !important;
              padding: 0 !important;
              height: auto !important;
              min-height: 0 !important;
              font-weight: 400 !important;
            }
            .paper-icon-button-light {
              box-sizing: border-box !important;
              border-radius: 4px !important;
              color: var(--momoka-text-dim) !important;
            }
            .paper-icon-button-light:hover {
              background: var(--momoka-surface-hover) !important;
              color: var(--momoka-text) !important;
            }
            button:focus-visible, a:focus-visible, input:focus-visible, select:focus-visible, textarea:focus-visible {
              outline: 2px solid var(--momoka-accent) !important;
              outline-offset: 2px !important;
            }

            /* ---- 控制台首页：每一段一张卡片 ---- */
            .dashboardSections {
              display: flex !important;
              flex-wrap: wrap !important;
              gap: 12px !important;
            }
            .dashboardSection {
              box-sizing: border-box !important;
              background: var(--momoka-surface) !important;
              border: 0 !important;
              border-radius: 12px !important;
              padding: 16px 20px !important;
            }
            /* 网页里留着一个没有内容的占位分区；画上卡片边之后它就成了空盒子。 */
            .dashboardSection:not(:has(*)) {
              display: none !important;
            }
            .dashboardSection-mini {
              padding: 16px 20px !important;
            }
            .infoBanner, .warningBanner, .betaInfoBanner {
              background: var(--momoka-surface-alt) !important;
              border: 0 !important;
              border-radius: 6px !important;
              color: var(--momoka-text) !important;
              box-shadow: none !important;
            }
            .infoBannerIcon {
              color: var(--momoka-accent) !important;
            }
            .serverUpToDateCheckIcon {
              color: var(--momoka-accent) !important;
            }
            .sectionTitleContainer {
              margin-bottom: 8px !important;
            }
            .sectionTitleTextButton, .sectionTitleTextButton-link {
              color: var(--momoka-text) !important;
            }
            .sectionTitleMoreIcon {
              color: var(--momoka-text-dim) !important;
            }

            /* ---- 活动列表 / 用户列表的行 ---- */
            .listItem {
              border-bottom: 1px solid var(--momoka-border) !important;
            }
            .listItem:last-child {
              border-bottom: 0 !important;
            }
            .listItem-content, .listItem-content-bg {
              background: transparent !important;
              border: 0 !important;
              border-radius: 6px !important;
            }
            .listItem-hoverable:hover .listItem-content,
            .listItem-hoverable:hover .listItem-content-bg {
              background: var(--momoka-surface-hover) !important;
            }
            .listItemImageContainer, .defaultCardBackground {
              background: var(--momoka-surface-alt) !important;
              background-image: none !important;
            }
            .listItemIcon, .cardImageIcon {
              color: var(--momoka-text-dim) !important;
            }
            .listItemBodyText {
              color: var(--momoka-text) !important;
              text-shadow: none !important;
            }

            /* ---- 设备 / 用户卡片 ---- */
            .itemsViewSettingsContainer {
              justify-content: flex-start !important;
              gap: 8px !important;
              padding: 0 0 16px !important;
            }
            .itemsContainer {
              justify-content: flex-start !important;
            }
            .listTotalRecordCount {
              color: var(--momoka-text-dim) !important;
              font-size: 14px !important;
            }
            .cardBox {
              background: var(--momoka-surface) !important;
              border: 0 !important;
              border-radius: 12px !important;
              box-shadow: none !important;
              overflow: hidden !important;
            }
            .cardContent, .cardContent-button, .cardContent-background {
              border-radius: 12px !important;
              box-shadow: none !important;
            }
            .cardContent, .cardContent-button, .cardContent-background,
            .cardImageContainer, .defaultCardBackground {
              background-color: var(--momoka-surface-alt) !important;
              background-image: none !important;
            }
            .cardText, .cardText-first {
              color: var(--momoka-text) !important;
              text-shadow: none !important;
            }
            .cardTextActionButton {
              color: var(--momoka-text) !important;
              font-weight: 600 !important;
            }
            .cardTextActionButton:hover {
              color: var(--momoka-accent) !important;
            }
            .cardText-secondary {
              color: var(--momoka-text-dim) !important;
            }
            .cardOverlayButton-fab {
              width: 28px !important;
              height: 28px !important;
              min-height: 28px !important;
              border-radius: 4px !important;
              background: var(--momoka-surface-elevated) !important;
              border: 1px solid var(--momoka-border) !important;
            }

            /* ---- 系统滚动条 ---- */
            ::-webkit-scrollbar {
              width: 12px !important;
              height: 12px !important;
              background: transparent !important;
            }
            ::-webkit-scrollbar-track {
              background: transparent !important;
            }
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
            ::selection {
              background: var(--momoka-accent) !important;
              color: var(--momoka-on-accent) !important;
            }
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

                    var style = document.getElementById("momoka-native");
                    if (!style) {
                      style = document.createElement("style");
                      style.id = "momoka-native";
                    }
                    style.textContent = css;
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
