using System;
using Salasasa.DataLab;
using UnityEngine;

namespace Salasasa.Statistics
{
    [ExecuteAlways, DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class StatisticsWhitespaceFit : MonoBehaviour
    {
        public const float ReferenceWidth = 1600f;
        public const float DesignWidth = 1328f;
        public const float DesignHeight = 744f;
        public const float VerticalMargin = 16f;
        public const float WideHorizontalMargin = 32f, WideVerticalMargin = 24f, WideColumnGap = 32f;
        public const float StatisticsFraction = .49f;
        public const float HeaderHeight = 72f, FooterHeight = 56f;
        public const float FilterLabelTop = 72f, FilterDropdownTop = 102f, SectionHeadingTop = 164f, TableTop = 204f;

        [Serializable]
        public sealed class WideReferences
        {
            public HomeDataPanel panel;
            public RectTransform left, right, title, header, mainRuleLabel, filters, overviewTitle, recentTitle, divider;
            public RectTransform fanFooter, recordsFooter;
            public RectTransform[] filterLabels;
            public RectTransform fanTitle;
            public RectTransform[] fanColumns, fanHeadings, fanHeadingNames, fanHeadingCounts,
                fanHeadingPercents, fanRows, fanDividers;
        }

        [SerializeField] RectTransform available;
        [SerializeField] RectTransform shell;
        [SerializeField] UnityEngine.UI.Image background;
        [SerializeField] UnityEngine.UI.Image backgroundSource;
        [SerializeField] WideReferences wide;
        Vector2 lastAvailable;
        float appliedScale;

        public RectTransform Available => available;
        public RectTransform Shell => shell;
        public UnityEngine.UI.Image Background => background;
        public UnityEngine.UI.Image BackgroundSource => backgroundSource;
        public float AppliedScale => appliedScale;
        public Vector2 DesignSize => new Vector2(DesignWidth, DesignHeight);
        public bool IsWide => wide != null && wide.panel;
        public RectTransform StatisticsColumn => IsWide ? wide.left : null;
        public RectTransform RecentColumn => IsWide ? wide.right : null;
        public static Vector2 WideSizeFor(Vector2 size) => new Vector2(
            Mathf.Max(1, size.x - WideHorizontalMargin * 2), Mathf.Max(1, size.y - WideVerticalMargin));

        public void ConfigureWideBaked(WideReferences references)
        {
            wide = references;
            ApplyNow();
        }

        public void ConfigureBaked(RectTransform availableRect, RectTransform shellRect)
        {
            available = availableRect;
            shell = shellRect;
            ApplyNow();
        }

        public void ConfigureSurfaceBaked(UnityEngine.UI.Image surface, UnityEngine.UI.Image paletteSource)
        {
            background = surface;
            backgroundSource = paletteSource;
            ApplySurface();
        }

        public static float ScaleFor(Vector2 size)
        {
            float widthScale = Mathf.Max(1f, size.x) / DesignWidth;
            float heightScale = Mathf.Max(1f, size.y - VerticalMargin * 2f) / DesignHeight;
            return Mathf.Min(1f, Mathf.Min(widthScale, heightScale));
        }

        void OnEnable() { if (!shell || appliedScale <= 0 || available && (available.rect.size - lastAvailable).sqrMagnitude > .0001f) ApplyNow(); }

        void LateUpdate()
        {
            if (!available || !shell) return;
            if ((available.rect.size - lastAvailable).sqrMagnitude > .0001f) ApplyNow();
            SyncSurfaceColor();
        }

        public void ApplyNow()
        {
            if (!available || !shell || available == shell || available.rect.width <= 0 || available.rect.height <= 0) return;
            lastAvailable = available.rect.size;
            if (IsWide)
            {
                ApplyWide();
                ApplySurface();
                return;
            }
            // Bake the authored size independently of the Editor's current Game View.
            appliedScale = Application.isPlaying ? ScaleFor(lastAvailable) : 1f;
            shell.anchorMin = shell.anchorMax = shell.pivot = Vector2.one * .5f;
            shell.anchoredPosition = Vector2.zero;
            shell.sizeDelta = DesignSize;
            shell.localScale = Vector3.one * appliedScale;
            ApplySurface();
        }

        void ApplyWide()
        {
            var panel = wide.panel;
            Vector2 size = WideSizeFor(lastAvailable);
            appliedScale = 1;
            shell.anchorMin = shell.anchorMax = shell.pivot = new Vector2(.5f, 1);
            shell.anchoredPosition = new Vector2(0, -WideVerticalMargin);
            shell.sizeDelta = size;
            shell.localScale = Vector3.one;
            float left = Mathf.Max(1, size.x - WideColumnGap) * StatisticsFraction;
            float right = Mathf.Max(1, size.x - WideColumnGap) - left;
            float rightX = left + WideColumnGap;
            Box(wide.left, 0, 0, left, size.y);
            Box(wide.right, rightX, 0, right, size.y);
            float headerCenter = HeaderHeight * .5f - WideVerticalMargin;
            Box(wide.title, 0, headerCenter - 21, 300, 42);
            if(wide.header) Box(wide.header, -WideHorizontalMargin, -WideVerticalMargin, size.x + WideHorizontalMargin * 2, HeaderHeight);
            if(panel.playerIdInput) {
                Box(wide.title, 0, headerCenter - 21, 184, 42);
                Box(panel.playerIdInput, 208, headerCenter - 18, 240, 36);
                Box(panel.queryPlayer, 460, headerCenter - 18, 80, 36);
                Box(panel.status, size.x - 592, headerCenter - 17, 260, 34);
                Box(panel.refresh, size.x - 300, headerCenter - 19, 88, 38);
                Box(panel.visitDataStation, size.x - 192, headerCenter - 19, 192, 38);
            } else {
                Box(panel.status, size.x - 366, headerCenter - 17, 252, 34);
                Box(panel.refresh, size.x - 88, headerCenter - 19, 88, 38);
            }
            Stretch(wide.filters);
            UnityEngine.Component[] dropdowns;
            RectTransform[] labels;
            float[] xs,widths;
            if(panel.mainRules) {
                float available=left-36, mainWidth=available*.20f, subWidth=available*.36f, scopeWidth=available*.22f;
                xs=new[]{0,mainWidth+12,mainWidth+subWidth+24,mainWidth+subWidth+scopeWidth+36,0};
                widths=new[]{mainWidth,subWidth,scopeWidth,available-mainWidth-subWidth-scopeWidth,right};
                dropdowns=new UnityEngine.Component[]{panel.mainRules,panel.allRules,panel.statisticsScene,panel.statisticsMode,panel.recordScene};
                labels=new[]{wide.mainRuleLabel,wide.filterLabels[0],wide.filterLabels[1],wide.filterLabels[2],wide.filterLabels[3]};
            } else {
                float available=left-24, ruleWidth=available*.44f, scopeWidth=available*.27f;
                xs=new[]{0,ruleWidth+12,ruleWidth+scopeWidth+24,0};
                widths=new[]{ruleWidth,scopeWidth,available-ruleWidth-scopeWidth,right};
                dropdowns=new UnityEngine.Component[]{panel.allRules,panel.statisticsScene,panel.statisticsMode,panel.recordScene};
                labels=wide.filterLabels;
            }
            for (int i = 0; i < dropdowns.Length; i++)
            {
                Box(labels[i], xs[i], FilterLabelTop, widths[i], 24);
                Box(dropdowns[i], xs[i], FilterDropdownTop, widths[i], 40);
            }
            Box(wide.overviewTitle, 0, SectionHeadingTop, 220, 30);
            Box(panel.context, 232, SectionHeadingTop, left - 232, 30);
            Box(panel.overviewRoot.transform, 0, TableTop, left, 248);
            float metricWidth = (left - 24) / 4;
            for (int i = 0; i < panel.metrics.Count; i++)
            {
                var metric = panel.metrics[i];
                Box(metric.root.transform, i % 4 * (metricWidth + 8), i / 4 * 64, metricWidth, 56);
                Box(metric.label, 12, 5, metricWidth - 24, 22);
                Box(metric.value, 12, 23, metricWidth - 24, 33);
            }
            float footerTop = size.y - FooterHeight, bodyBottom = footerTop - 12;
            float fanTop = TableTop + 272, fanHeight = bodyBottom - fanTop;
            Box(panel.overviewNote, 0, TableTop + 248, left, 22);
            Box(panel.fanStatistics.root.transform, 0, fanTop, left, fanHeight);
            ApplyFans(left, fanHeight);
            Box(wide.recentTitle, 0, SectionHeadingTop, 220, 30);
            Box(panel.recordsStatus, 232, SectionHeadingTop, right - 232, 30);
            float recordsHeight = bodyBottom - TableTop;
            Box(panel.recordsRoot.transform, 0, TableTop, right, recordsHeight);
            foreach (var layout in panel.recordLayouts)
            {
                Stretch((RectTransform)layout.root.transform);
                float rowHeight = (recordsHeight - 8 * Mathf.Max(0, layout.rows.Count - 1)) / Mathf.Max(1, layout.rows.Count);
                for (int i = 0; i < layout.rows.Count; i++)
                {
                    Box(layout.rows[i].root.transform, 0, i * (rowHeight + 8), right, rowHeight);
                    layout.rows[i].card.RefreshGeometry();
                }
            }
            Box(panel.recordEmpty, 12, 0, right - 24, recordsHeight);
            if(wide.recordsFooter) Box(wide.recordsFooter, 0, footerTop, right, FooterHeight);
            float recentPagerTop = wide.recordsFooter ? 14 : size.y - 36;
            Box(panel.previous, 0, recentPagerTop, 88, 28);
            Box(panel.next, right - 88, recentPagerTop, 88, 28);
            Box(panel.pageNumber, 96, recentPagerTop, right - 192, 28);
            if(wide.fanFooter) {
                Box(wide.fanFooter, 0, footerTop, left, FooterHeight);
                Box(panel.fanStatistics.previous, 0, 14, 88, 28);
                Box(panel.fanStatistics.next, left - 88, 14, 88, 28);
                Box(panel.fanStatistics.pageNumber, 96, 14, left - 192, 28);
            }
            Box(wide.divider, left + WideColumnGap * .5f, SectionHeadingTop + 2, 1, footerTop - SectionHeadingTop - 2);
        }

        void ApplyFans(float width, float height)
        {
            var fans = wide.panel.fanStatistics;
            Box(wide.fanTitle, 0, 0, 220, 30);
            if(!wide.fanFooter) {
                Box(fans.previous, width - 132, 0, 32, 30);
                Box(fans.pageNumber, width - 96, 0, 60, 30);
                Box(fans.next, width - 32, 0, 32, 30);
            }
            Box(fans.scope, 224, 0, width - 368, 30);
            Box(fans.note, 0, 30, width, 10);
            Box(fans.table.transform, 0, 40, width, height - 40);
            Box(fans.empty, 0, 40, width, height - 40);
            float columnWidth = (width - 24) * .5f;
            float rowHeight = (height - 68) * .25f;
            float nameWidth = columnWidth - 174;
            for (int i = 0; i < 2; i++)
            {
                Box(wide.fanColumns[i], i * (columnWidth + 24), 0, columnWidth, height - 40);
                Box(wide.fanHeadings[i], 0, 0, columnWidth, 24);
                Box(wide.fanHeadingNames[i], 8, 0, nameWidth, 24);
                Box(wide.fanHeadingCounts[i], columnWidth - 166, 0, 68, 24);
                Box(wide.fanHeadingPercents[i], columnWidth - 92, 0, 84, 24);
                Box(wide.fanRows[i], 0, 28, columnWidth, height - 68);
            }
            for (int i = 0; i < fans.rows.Count; i++)
            {
                var row = fans.rows[i];
                Box(row.root.transform, 0, i % 4 * rowHeight, columnWidth, rowHeight);
                Box(row.label, 8, 0, nameWidth, rowHeight);
                Box(row.count, columnWidth - 166, 0, 68, rowHeight);
                Box(row.percent, columnWidth - 92, 0, 84, rowHeight);
                Box(wide.fanDividers[i], 0, rowHeight - 1, columnWidth, 1);
            }
        }

        static void Box(Component target, float x, float y, float width, float height)
        {
            var rect = (RectTransform)target.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(Mathf.Max(1, width), Mathf.Max(1, height));
            rect.localScale = Vector3.one;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * .5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        void ApplySurface()
        {
            if (!background || !available || background.transform.parent != available) return;
            var rect = background.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * .5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            SyncSurfaceColor();
        }

        void SyncSurfaceColor()
        {
            if (background && backgroundSource && background.color != backgroundSource.color)
                background.color = backgroundSource.color;
        }
    }
}
