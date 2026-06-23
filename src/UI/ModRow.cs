using System;
using AutoModSubscriber.Subscribe;
using Godot;

namespace AutoModSubscriber.UI;

/// <summary>
/// 区块 1 的单行：mod 图标占位 + 名字 + 状态文字 + 进度条。
/// 通过纯代码构造 Control 树。
/// </summary>
public partial class ModRow : HBoxContainer
{
    public string ManifestId { get; private set; } = "";
    public ulong FileId { get; private set; }

    private Label _name = null!;
    private Label _status = null!;
    private ProgressBar _bar = null!;
    private Button _openBtn = null!;

    public static ModRow Build(string displayName, string manifestId, ulong fileId)
    {
        var row = new ModRow
        {
            Name = $"Row_{manifestId}",
            ManifestId = manifestId,
            FileId = fileId,
        };
        row.CustomMinimumSize = new Vector2(0, 28);

        row._name = new Label
        {
            Text = displayName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        row.AddChild(row._name);

        row._bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(160, 18),
            MaxValue = 100,
            ShowPercentage = false,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row._bar.Visible = false;
        row.AddChild(row._bar);

        row._status = new Label
        {
            CustomMinimumSize = new Vector2(120, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.AddChild(row._status);

        row._openBtn = new Button
        {
            Text = DialogStrings.BtnOpenWorkshop,
            Visible = false,
        };
        row._openBtn.Pressed += row.OnOpenWorkshopPressed;
        row.AddChild(row._openBtn);

        row.SetStatus(SubscribeJobState.Pending, 0, 0, fileId == 0);
        return row;
    }

    public void Refresh(SubscribeJob? job)
    {
        if (job == null)
        {
            SetStatus(SubscribeJobState.Pending, 0, 0, FileId == 0);
            return;
        }
        SetStatus(job.State, job.BytesDownloaded, job.BytesTotal, false);
    }

    private void SetStatus(SubscribeJobState state, ulong done, ulong total, bool noWorkshopId)
    {
        if (noWorkshopId)
        {
            _status.Text = DialogStrings.StateNoWorkshopId;
            _bar.Visible = false;
            _openBtn.Visible = true;
            return;
        }

        _status.Text = DialogStrings.FormatStatus(state);

        bool showBar = state == SubscribeJobState.Downloading
                    || state == SubscribeJobState.WaitingInstall;
        _bar.Visible = showBar;
        if (showBar && total > 0)
        {
            _bar.MaxValue = total;
            _bar.Value = Math.Min(done, total);
        }
        else if (state == SubscribeJobState.Installed)
        {
            _bar.Visible = true;
            _bar.MaxValue = 100;
            _bar.Value = 100;
        }

        _openBtn.Visible = state == SubscribeJobState.Failed
                        || state == SubscribeJobState.TimedOut;
    }

    private void OnOpenWorkshopPressed()
    {
        try
        {
            string url = $"https://steamcommunity.com/workshop/browse/?appid=2868840&searchtext={Uri.EscapeDataString(ManifestId)}";
            OS.ShellOpen(url);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{ModuleInit.LogTag} OpenWorkshop failed: {ex}");
        }
    }
}