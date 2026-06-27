using Godot;

namespace AutoModSubscriber.UI;

/// <summary>
/// 区块 2 的单行：checkbox + manifest id + version。
/// </summary>
public partial class DisableCheckboxRow : HBoxContainer
{
    public string ManifestId { get; private set; } = "";

    private CheckBox _cb = null!;

    public bool IsChecked => _cb.ButtonPressed;

    public static DisableCheckboxRow Build(string displayName, string manifestId)
    {
        var row = new DisableCheckboxRow
        {
            Name = $"DisableRow_{manifestId}",
            ManifestId = manifestId,
        };
        row.CustomMinimumSize = new Vector2(0, 24);

        row._cb = new CheckBox
        {
            ButtonPressed = true,
        };
        row.AddChild(row._cb);

        var label = new Label
        {
            Text = displayName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        row.AddChild(label);

        return row;
    }
}