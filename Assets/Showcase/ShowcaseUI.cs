using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Collin.Core;

/// <summary>
/// 展板（AGENTS.md §6）：纯 UI，运行时枚举 Packages/com.collin.*/Showcase.json。
/// 全部控件由此脚本代码构建（DESIGN D-6）——场景文件里只允许存在挂本脚本的 GameObject，
/// 插件增减永不修改场景。左侧条目列表按依赖拓扑排序，右侧显示摘要/依赖闭包/演示视频。
/// 视频文件缺失或未配置时显示占位文案；点击「播放演示」用 HandheldPlay 就地播放。
/// </summary>
[RequireComponent(typeof(Canvas))]
public class ShowcaseUI : MonoBehaviour
{
    private readonly List<ShowcaseEntry> _entries = new List<ShowcaseEntry>();
    private RectTransform _listRoot;
    private Text _detailTitle;
    private Text _detailCategory;
    private Text _detailSummary;
    private Text _detailLineage;
    private Text _detailDeps;
    private RawImage _videoImage;
    private VideoPlayer _videoPlayer;
    private RenderTexture _videoRT;
    private Coroutine _videoCo;
    private string _currentVideoPath;
    private string _cycleWarning = "";
    private readonly List<Button> _itemButtons = new List<Button>();
    private int _selected = -1;

    private static readonly Color Bg = new Color(0.10f, 0.11f, 0.13f);
    private static readonly Color Panel = new Color(0.16f, 0.17f, 0.20f);
    private static readonly Color PanelHi = new Color(0.23f, 0.26f, 0.31f);
    private static readonly Color TextMain = new Color(0.92f, 0.93f, 0.95f);
    private static readonly Color TextDim = new Color(0.62f, 0.66f, 0.72f);

    private void Start()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 没有 GraphicRaycaster，EventSystem 收不到任何 UI 命中——整块展板对点击是聋的。
        // 场景文件按 D-6 只允许存这一个 GameObject，故射线组件也在此代码补齐。
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        BuildLayout();
        Reload();
    }

    private void OnDestroy()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            Object.Destroy(_videoPlayer);
        }
        if (_videoRT != null)
        {
            _videoRT.Release();
            Object.Destroy(_videoRT);
        }
    }

    // ── 布局构建（代码一次成型） ──────────────────────────────

    private void BuildLayout()
    {
        // 左栏：标题 + 滚动列表
        var left = CreateRect("Left", transform,
            new Vector2(0f, 0f), new Vector2(0.34f, 1f), Vector2.zero, Vector2.zero);
        var leftBg = left.gameObject.AddComponent<Image>();
        leftBg.color = Panel;

        var title = CreateText("Title", left, 30, TextMain,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -52f), new Vector2(-16f, -12f));
        title.text = "插件展板";

        var scrollRt = CreateRect("Scroll", left,
            new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, -12f), new Vector2(-12f, -60f));
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        var viewport = CreateRect("Viewport", scrollRt,
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = CreateRect("Content", viewport,
            new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f); // 顶部对齐，否则列表被 ScrollRect 钳到中部
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = viewport;
        _listRoot = content;

        // 右栏：详情（文本区可滚动，避免长摘要被截断；底部固定视频区）
        var right = CreateRect("Right", transform,
            new Vector2(0.34f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        var rightBg = right.gameObject.AddComponent<Image>();
        rightBg.color = Bg;

        var textScrollRt = CreateRect("TextScroll", right,
            new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, 380f), new Vector2(-12f, -12f));
        var textScroll = textScrollRt.gameObject.AddComponent<ScrollRect>();
        textScroll.horizontal = false;
        textScroll.vertical = true;
        var textViewport = CreateRect("Viewport", textScrollRt,
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        textViewport.gameObject.AddComponent<RectMask2D>();
        var textContent = CreateRect("Content", textViewport,
            new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        textContent.pivot = new Vector2(0.5f, 1f);
        var tvlg = textContent.gameObject.AddComponent<VerticalLayoutGroup>();
        tvlg.spacing = 8f;
        tvlg.padding = new RectOffset(12, 12, 6, 12);
        tvlg.childControlWidth = true;
        tvlg.childControlHeight = true;
        tvlg.childForceExpandWidth = true;
        tvlg.childForceExpandHeight = false;
        textContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;
        textScroll.content = textContent;
        textScroll.viewport = textViewport;

        _detailTitle = CreateAutoText("DetailTitle", textContent, 40, TextMain);
        _detailCategory = CreateAutoText("DetailCategory", textContent, 22, TextDim);
        _detailSummary = CreateAutoText("DetailSummary", textContent, 24, TextMain);
        _detailLineage = CreateAutoText("DetailLineage", textContent, 19, TextDim);
        _detailDeps = CreateAutoText("DetailDeps", textContent, 18, TextDim);

        var videoNode = CreateRect("Video", right,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-263f, 64f), new Vector2(263f, 360f));
        _videoImage = videoNode.gameObject.AddComponent<RawImage>();
        _videoImage.color = new Color(0f, 0f, 0f, 1f);

        var playBtnRt = CreateRect("PlayBtn", right,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-90f, 4f), new Vector2(90f, 56f));
        MakeButton(playBtnRt, "播放演示", OnPlayClicked);

        _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        _videoPlayer.playOnAwake = false;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        // 演示视频为合成素材（静音轨）；AudioOutputMode.Direct 在编辑器下会让异步 prepare 挂起，
        // 故用 None。若日后换入带真实音轨的录屏，改回 AudioSource 即可。
        _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        _videoRT = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32);
        _videoRT.Create();
        _videoPlayer.targetTexture = _videoRT;

        // 场景无相机会在 Game 视图中央留下 "No cameras rendering" 占位框，压住视频区。
        // 补一台只负责清屏、不剔除任何物体的相机消掉它（Overlay 画布始终绘于其上）。
        if (UnityEngine.Camera.main == null)
        {
            var camGo = new GameObject("ShowcaseCamera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bg;
            cam.cullingMask = 0;
            cam.orthographic = true;
            cam.depth = -1f;
        }
    }

    // ── 数据与刷新 ────────────────────────────────────────────

    /// <summary>枚举并重建左栏列表；外部可经 eval 调 Refresh() 热更新。</summary>
    public void Reload()
    {
        foreach (var b in _itemButtons)
        {
            if (b != null) Object.Destroy(b.gameObject);
        }
        _itemButtons.Clear();

        var result = ShowcaseRuntime.LoadAndSort();
        _entries.Clear();
        _entries.AddRange(result.ordered);

        if (result.cycleDetected)
        {
            _cycleWarning = "⚠ 检测到依赖环，尾部条目顺序不可靠 · ";
        }
        if (_entries.Count == 0)
        {
            _detailTitle.text = "没有可展示的插件";
            _detailSummary.text = "Packages/ 下未发现含 Showcase.json 的 com.collin.* 包。";
            _videoImage.texture = null;
            return;
        }
        RebuildLineageList();
        Select(0);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        _selected = index;
        var entry = _entries[index];

        for (int i = 0; i < _itemButtons.Count; i++)
        {
            var img = _itemButtons[i].GetComponent<Image>();
            if (img != null)
            {
                img.color = i == index ? PanelHi : Panel;
            }
        }

        StopVideo();
        _detailTitle.text = string.IsNullOrEmpty(entry.name) ? entry.packageId : entry.name;
        _detailCategory.text = string.IsNullOrEmpty(entry.category) ? "未分类" : entry.category;
        _detailSummary.text = string.IsNullOrEmpty(entry.summary) ? "（无简介）" : entry.summary;

        var lineage = new List<string>();
        if (!string.IsNullOrEmpty(entry.extractedFrom))
        {
            var src = ShowcaseSorter.ExtractedFromEntry(entry, _entries);
            string srcName = src != null
                ? (string.IsNullOrEmpty(src.name) ? src.packageId : src.name)
                : entry.extractedFrom;
            lineage.Add(ShowcaseSorter.TierOf(entry) == ShowcaseSorter.TierTool
                ? "抽离自 " + srcName + "（向上抽离·工具，运行依赖 " + srcName + "）"
                : "抽离自 " + srcName + "（向下抽离·能力）");
        }
        foreach (var kid in ShowcaseSorter.ExtracteesOf(entry, _entries))
        {
            lineage.Add("已抽离出 " + (string.IsNullOrEmpty(kid.name) ? kid.packageId : kid.name));
        }
        if (ShowcaseSorter.TierOf(entry) == ShowcaseSorter.TierMiddleware)
        {
            lineage.Add("谱系层：中间件层 · 所有包的公共依赖");
        }
        else if (lineage.Count == 0)
        {
            lineage.Add("谱系层：" + TierLabel(ShowcaseSorter.TierOf(entry)) + " · 未登记抽离血缘");
        }
        _detailLineage.text = string.Join("\n", lineage);

        var closure = ShowcaseSorter.ExportClosure(entry.packageId, _entries);
        closure.Remove(entry.packageId);
        var names = new List<string>();
        foreach (var e in _entries)
        {
            if (closure.Contains(e.packageId))
            {
                names.Add(string.IsNullOrEmpty(e.name) ? e.packageId : e.name);
            }
        }
        _detailDeps.text = _cycleWarning + (names.Count == 0
            ? "导出携带：无（独立包）"
            : "导出携带（依赖闭包）：" + string.Join("、", names));

        _currentVideoPath = ShowcaseRuntime.ResolveVideoPath(entry);
        if (string.IsNullOrEmpty(_currentVideoPath))
        {
            _videoImage.texture = null;
            ShowVideoPlaceholder(string.IsNullOrEmpty(entry.video)
                ? "该包未配置演示视频"
                : "视频文件缺失: " + entry.video);
        }
        else
        {
            HideVideoPlaceholder();
            // 用规范 file:/// 三斜杠形式；file:// + "D:/x" 会被当作主机名 D: 解析，prepare 永不完成
            // 本地文件直接给绝对路径：file:// 前缀会把盘符 "D:" 当主机名（urlparse 已证明），
            // prepare 永不完成；Unity VideoPlayer 原生支持绝对路径。
            _videoPlayer.url = _currentVideoPath;
        }
    }

    private void OnPlayClicked()
    {
        if (string.IsNullOrEmpty(_currentVideoPath)) return;
        StopVideo();
        _videoCo = StartCoroutine(PlayVideo());
    }

    private IEnumerator PlayVideo()
    {
        ShowVideoPlaceholder("视频准备中…");
        _videoPlayer.Prepare();
        const float timeout = 15f;
        float t = 0f;
        while (!_videoPlayer.isPrepared && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!_videoPlayer.isPrepared)
        {
            // 不再无限自旋黑屏：把失败原因显示出来
            ShowVideoPlaceholder("视频解码失败或超时\n" + _currentVideoPath);
            yield break;
        }
        HideVideoPlaceholder();
        _videoImage.texture = _videoRT;
        _videoPlayer.Play();
        while (_videoPlayer.isPlaying) yield return null;
    }

    private void StopVideo()
    {
        if (_videoCo != null) { StopCoroutine(_videoCo); _videoCo = null; }
        if (_videoPlayer != null) _videoPlayer.Stop();
        if (_videoImage != null) _videoImage.texture = null;
    }

    private void ShowVideoPlaceholder(string msg)
    {
        var t = _videoImage.transform.Find("Placeholder");
        if (t == null)
        {
            var rt = CreateRect("Placeholder", _videoImage.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var txt = rt.gameObject.AddComponent<Text>();
            txt.font = LegacySkin();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = TextDim;
            txt.fontSize = 20;
        }
        _videoImage.color = new Color(0.06f, 0.06f, 0.08f, 1f);
        _videoImage.transform.Find("Placeholder").GetComponent<Text>().text = msg;
        _videoImage.transform.Find("Placeholder").gameObject.SetActive(true);
    }

    private void HideVideoPlaceholder()
    {
        var t = _videoImage.transform.Find("Placeholder");
        if (t != null) t.gameObject.SetActive(false);
        _videoImage.color = Color.white;
    }

    // ── uGUI 小工具 ───────────────────────────────────────────

    private void AddItem(ShowcaseEntry entry, int index, bool indented = false)
    {
        var rt = CreateRect("Item_" + entry.packageId, _listRoot,
            new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 64f;
        var label = CreateText("Label", rt, 22, TextMain,
            Vector2.zero, Vector2.one,
            new Vector2(indented ? 34f : 12f, -6f), new Vector2(-12f, -6f));
        string tierMark = indented
            ? "↳ <color=#c9a86a>" + TierLabel(ShowcaseSorter.TierOf(entry)) + "</color> "
            : "";
        label.text = tierMark + (string.IsNullOrEmpty(entry.category) ? "未分类" : entry.category)
            + "\n<size=15><color=#8f97a3>" + (string.IsNullOrEmpty(entry.name) ? entry.packageId : entry.name) + "</color></size>";
        label.alignment = TextAnchor.MiddleLeft;
        var btn = MakeButton(rt, null, null);
        btn.onClick.AddListener(() => Select(index));
        _itemButtons.Add(btn);
    }

    private Button MakeButton(RectTransform rt, string label, UnityEngine.Events.UnityAction onClick)
    {
        var img = rt.gameObject.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        img.color = Panel;
        var btn = rt.gameObject.GetComponent<Button>();
        if (btn == null) btn = rt.gameObject.AddComponent<Button>();
        if (!string.IsNullOrEmpty(label))
        {
            var t = CreateText("Label", rt, 24, TextMain,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.alignment = TextAnchor.MiddleCenter;
            t.text = label;
        }
        if (onClick != null) btn.onClick.AddListener(onClick);
        return btn;
    }

    private static RectTransform CreateRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    private static Text CreateText(string name, Transform parent, int size, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = CreateRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        var txt = rt.gameObject.AddComponent<Text>();
        txt.font = LegacySkin();
        txt.fontSize = size;
        txt.color = color;
        return txt;
    }

    /// <summary>放进 VerticalLayoutGroup 的自适应高度文本：宽度换行、高度随内容。</summary>
    private static Text CreateAutoText(string name, Transform parent, int size, Color color)
    {
        var rt = CreateRect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        var txt = rt.gameObject.AddComponent<Text>();
        txt.font = LegacySkin();
        txt.fontSize = size;
        txt.color = color;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.alignment = TextAnchor.UpperLeft;
        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return txt;
    }

    private static Font _font;
    private static Font LegacySkin()
    {
        if (_font == null)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }

    /// <summary>Reload() 末尾由 Select 驱动 AddItem；此钩子按抽离谱系分组重建按钮列表。</summary>
    private void RebuildList()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            AddItem(_entries[i], i);
        }
    }

    /// <summary>谱系分组视图：中间件层一组，其余按抽离血缘聚成产品线；组内母体在前、抽离品缩进跟随。</summary>
    private void RebuildLineageList()
    {
        foreach (var group in ShowcaseSorter.LineageGroups(_entries))
        {
            var headRt = CreateRect("Group_" + group.rootId, _listRoot,
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            headRt.gameObject.AddComponent<LayoutElement>().minHeight = 34f;
            var head = CreateText("Label", headRt, 20, new Color(0.45f, 0.62f, 0.85f),
                Vector2.zero, Vector2.one, new Vector2(12f, -4f), new Vector2(-12f, -4f));
            head.text = group.entries.Count > 1
                ? "▍" + group.title + " <size=14><color=#8f97a3>产品线 · " + group.entries.Count + " 包</color></size>"
                : "▍" + group.title;
            head.alignment = TextAnchor.MiddleLeft;

            foreach (var e in group.entries)
            {
                int myIndex = _entries.IndexOf(e);
                if (myIndex < 0) continue;
                bool indented = !string.IsNullOrEmpty(group.rootId) && e.packageId != group.rootId;
                AddItem(e, myIndex, indented);
            }
        }
    }

    private static string TierLabel(string tier)
    {
        switch (tier)
        {
            case ShowcaseSorter.TierMiddleware: return "中间件层";
            case ShowcaseSorter.TierCapability: return "能力层";
            case ShowcaseSorter.TierTool: return "工具层";
            case ShowcaseSorter.TierProduct: return "产品层";
            default: return "组件";
        }
    }
}
