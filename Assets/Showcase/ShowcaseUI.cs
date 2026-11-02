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

        // 右栏：详情
        var right = CreateRect("Right", transform,
            new Vector2(0.34f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        var rightBg = right.gameObject.AddComponent<Image>();
        rightBg.color = Bg;

        _detailTitle = CreateText("DetailTitle", right, 40, TextMain,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(24f, -72f), new Vector2(-24f, -20f));
        _detailCategory = CreateText("DetailCategory", right, 22, TextDim,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(24f, -106f), new Vector2(-24f, -76f));
        _detailSummary = CreateText("DetailSummary", right, 24, TextMain,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(24f, -230f), new Vector2(-24f, -114f));
        _detailSummary.horizontalOverflow = HorizontalWrapMode.Wrap;
        _detailDeps = CreateText("DetailDeps", right, 18, TextDim,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(24f, -268f), new Vector2(-24f, -238f));

        var videoNode = CreateRect("Video", right,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-480f, 84f), new Vector2(480f, 444f));
        _videoImage = videoNode.gameObject.AddComponent<RawImage>();
        _videoImage.color = new Color(0f, 0f, 0f, 1f);

        var playBtnRt = CreateRect("PlayBtn", right,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-90f, 20f), new Vector2(90f, 64f));
        MakeButton(playBtnRt, "播放演示", OnPlayClicked);

        _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        _videoPlayer.playOnAwake = false;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        _videoRT = new RenderTexture(960, 360, 0, RenderTextureFormat.ARGB32);
        _videoRT.Create();
        _videoPlayer.targetTexture = _videoRT;
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
        RebuildList();
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

        var closure = ShowcaseSorter.DependencyClosure(entry.packageId, _entries);
        closure.Remove(entry.packageId);
        _detailDeps.text = _cycleWarning + (closure.Count == 0
            ? "依赖：无（独立包）"
            : "依赖闭包：" + string.Join("、", new List<string>(closure)));

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
            _videoPlayer.url = "file://" + _currentVideoPath.Replace('\\', '/');
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
        _videoPlayer.Prepare();
        while (!_videoPlayer.isPrepared) yield return null;
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

    private void AddItem(ShowcaseEntry entry, int index)
    {
        var rt = CreateRect("Item_" + entry.packageId, _listRoot,
            new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 64f;
        var label = CreateText("Label", rt, 22, TextMain,
            Vector2.zero, Vector2.one, new Vector2(12f, -6f), new Vector2(-12f, -6f));
        label.text = (string.IsNullOrEmpty(entry.category) ? "未分类" : entry.category)
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

    private static Font _font;
    private static Font LegacySkin()
    {
        if (_font == null)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }

    /// <summary>Reload() 末尾由 Select 驱动 AddItem；此钩子在数据刷新后重建按钮列表。</summary>
    private void RebuildList()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            AddItem(_entries[i], i);
        }
    }
}
