using System;
using System.Collections.Generic;
using System.Linq;
using AutoModSubscriber.Disable;
using AutoModSubscriber.Protocol;
using AutoModSubscriber.Subscribe;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace AutoModSubscriber.UI;

/// <summary>
/// Mod 不一致弹窗。挂到 SceneTree.Root，模态阻塞背后输入。
///
/// 双区块：
///   1. missingOnLocal -&gt; 自动订阅
///   2. missingOnHost  -&gt; 勾选禁用
///
/// 不调用 OS.Execute 也不退游戏；所有"完成"提示都是文案追加。
/// </summary>
public partial class AutoSubscribeDialog : Control
{
    private const float DefaultWidth = 720;
    private const float DefaultHeight = 560;

    private readonly List<ModRow> _subscribeRows = new();
    private readonly List<DisableCheckboxRow> _disableRows = new();

    private Label _hintLabel = null!;
    private Button _subscribeAllBtn = null!;
    private Button _disableSelectedBtn = null!;
    private Godot.Timer _pollTimer = null!;

    private bool _subscribeInProgress;

    public static AutoSubscribeDialog Build(ConnectionFailureExtraInfo extra)
    {
        var dlg = new AutoSubscribeDialog
        {
            Name = "AutoSubscribeDialog",
            MouseFilter = MouseFilterEnum.Stop,
        };
        dlg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dlg.BuildTree(extra);
        return dlg;
    }

    private void BuildTree(ConnectionFailureExtraInfo extra)
    {
        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.6f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(DefaultWidth, DefaultHeight),
        };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -DefaultWidth / 2f;
        panel.OffsetTop = -DefaultHeight / 2f;
        panel.OffsetRight = DefaultWidth / 2f;
        panel.OffsetBottom = DefaultHeight / 2f;
        AddChild(panel);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.AddChild(root);

        AddTitle(root, DialogStrings.Title);

        AddSection(root, DialogStrings.SectionMissingOnLocal);
        BuildMissingOnLocalSection(root, extra.missingModsOnLocal);

        AddSection(root, DialogStrings.SectionMissingOnHost);
        BuildMissingOnHostSection(root, extra.missingModsOnHost);

        _hintLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 48),
        };
        root.AddChild(_hintLabel);
        UpdateInitialHint(extra);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var closeBtn = new Button { Text = DialogStrings.BtnClose };
        closeBtn.Pressed += OnClose;
        footer.AddChild(closeBtn);
        root.AddChild(footer);

        _pollTimer = new Godot.Timer
        {
            WaitTime = 0.25f,
            Autostart = false,
            OneShot = false,
        };
        _pollTimer.Timeout += OnPoll;
        AddChild(_pollTimer);
    }

    private static void AddTitle(VBoxContainer parent, string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        parent.AddChild(label);
        parent.AddChild(new HSeparator());
    }

    private static void AddSection(VBoxContainer parent, string text)
    {
        var label = new Label { Text = text };
        parent.AddChild(label);
    }

    private void BuildMissingOnLocalSection(VBoxContainer root, List<string>? missing)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 160),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(vbox);

        if (missing == null || missing.Count == 0)
        {
            vbox.AddChild(new Label { Text = DialogStrings.EmptySection });
        }
        else
        {
            foreach (var name in missing)
            {
                ParseIdVersion(name, out var id, out _);
                ModWorkshopMap.TryGet(id, out var fileId);
                var row = ModRow.Build(name, id, fileId);
                vbox.AddChild(row);
                _subscribeRows.Add(row);
            }
        }

        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        btnRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _subscribeAllBtn = new Button { Text = DialogStrings.BtnSubscribeAll };
        _subscribeAllBtn.Pressed += OnSubscribeAll;
        _subscribeAllBtn.Disabled = _subscribeRows.Count == 0
                                 || _subscribeRows.All(r => r.FileId == 0);
        btnRow.AddChild(_subscribeAllBtn);
        root.AddChild(btnRow);
    }

    private void BuildMissingOnHostSection(VBoxContainer root, List<string>? missing)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 140),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(vbox);

        if (missing == null || missing.Count == 0)
        {
            vbox.AddChild(new Label { Text = DialogStrings.EmptySection });
        }
        else
        {
            foreach (var name in missing)
            {
                ParseIdVersion(name, out var id, out _);
                var row = DisableCheckboxRow.Build(name, id);
                vbox.AddChild(row);
                _disableRows.Add(row);
            }
        }

        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        btnRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _disableSelectedBtn = new Button { Text = DialogStrings.BtnDisableSelected };
        _disableSelectedBtn.Pressed += OnDisableSelected;
        _disableSelectedBtn.Disabled = _disableRows.Count == 0;
        btnRow.AddChild(_disableSelectedBtn);
        root.AddChild(btnRow);
    }

    private void UpdateInitialHint(ConnectionFailureExtraInfo extra)
    {
        if (!WorkshopSubscriber.Instance.IsSteamAvailable)
        {
            _hintLabel.Text = DialogStrings.HintSteamUnavailable;
            if (_subscribeAllBtn != null) _subscribeAllBtn.Disabled = true;
            return;
        }

        bool anyMissingLocal = extra.missingModsOnLocal != null && extra.missingModsOnLocal.Count > 0;
        if (!anyMissingLocal)
        {
            _hintLabel.Text = "";
            return;
        }

        if (!ModWorkshopMap.HostHasMod)
        {
            _hintLabel.Text = DialogStrings.HintHostNotInstalled;
            return;
        }

        // host 装了本 mod。看缺失项里有几个能解析出 fileId
        int resolvable = 0;
        if (extra.missingModsOnLocal != null)
        {
            foreach (var raw in extra.missingModsOnLocal)
            {
                int dash = raw.LastIndexOf('-');
                string id = dash > 0 ? raw.Substring(0, dash) : raw;
                if (ModWorkshopMap.TryGet(id, out var fid) && fid != 0)
                    resolvable++;
            }
        }
        if (resolvable == 0)
        {
            _hintLabel.Text = DialogStrings.HintHostHasModNoWorkshop;
        }
        else
        {
            _hintLabel.Text = "";
        }
    }

    private void OnSubscribeAll()
    {
        if (_subscribeInProgress) return;

        var items = _subscribeRows
            .Where(r => r.FileId != 0)
            .Select(r => (r.ManifestId, r.FileId))
            .ToList();

        if (items.Count == 0) return;

        _subscribeInProgress = true;
        _subscribeAllBtn.Disabled = true;

        var jobs = WorkshopSubscriber.Instance.Submit(items);

        // 持续轮询直到全部 terminal
        _pollTimer.Start();

        _ = WaitAndFinishAsync(jobs);
    }

    private async System.Threading.Tasks.Task WaitAndFinishAsync(IReadOnlyList<SubscribeJob> jobs)
    {
        try
        {
            await WorkshopSubscriber.Instance.WaitAll(jobs);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} WaitAll failed: {ex}");
        }

        Callable.From(OnSubscribeFinished).CallDeferred();
    }

    private void OnSubscribeFinished()
    {
        _pollTimer.Stop();
        RefreshSubscribeRows();
        _subscribeInProgress = false;

        int succ = 0, fail = 0;
        foreach (var row in _subscribeRows)
        {
            var job = WorkshopSubscriber.Instance.Get(row.FileId);
            if (job == null) continue;
            if (job.State == SubscribeJobState.Installed) succ++;
            else if (job.IsTerminal) fail++;
        }
        _hintLabel.Text = string.Format(DialogStrings.HintSubscribeDone, succ, fail);
    }

    private void OnPoll()
    {
        try { WorkshopSubscriber.Instance.Poll(); }
        catch (Exception ex) { GD.PrintErr($"{ModuleInit.LogTag} Poll error: {ex}"); }

        RefreshSubscribeRows();
    }

    private void RefreshSubscribeRows()
    {
        foreach (var row in _subscribeRows)
        {
            var job = row.FileId == 0 ? null : WorkshopSubscriber.Instance.Get(row.FileId);
            row.Refresh(job);
        }
    }

    private void OnDisableSelected()
    {
        var ids = _disableRows.Where(r => r.IsChecked).Select(r => r.ManifestId).ToList();
        if (ids.Count == 0) return;

        _disableSelectedBtn.Disabled = true;

        var result = ModDisableApplier.Apply(ids);
        string append = string.Format(DialogStrings.HintDisableDone,
            result.Succeeded.Count, result.Failed.Count);

        _hintLabel.Text = string.IsNullOrEmpty(_hintLabel.Text)
            ? append
            : _hintLabel.Text + "\n" + append;
    }

    private void OnClose()
    {
        QueueFree();
    }

    /// <summary>
    /// 把 "ModId-1.2.3" 解析为 id="ModId", version="1.2.3"。
    /// 没有 '-' 时整段视为 id。取最后一个 '-' 作为分隔，因为 id 本身不含 '-'。
    /// </summary>
    private static void ParseIdVersion(string raw, out string id, out string version)
    {
        if (string.IsNullOrEmpty(raw))
        {
            id = ""; version = "";
            return;
        }
        int dash = raw.LastIndexOf('-');
        if (dash <= 0 || dash == raw.Length - 1)
        {
            id = raw; version = "";
        }
        else
        {
            id = raw.Substring(0, dash);
            version = raw.Substring(dash + 1);
        }
    }
}