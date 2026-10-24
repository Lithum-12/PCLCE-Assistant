using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.UI.Controls.SvgIcon;

namespace PCL.Assistant;

/// <summary>
/// 助手气泡里的实时状态条：在想什么、正在读哪个文件、工具调用成没成。
/// 只做展示、不进消息存储，所以重建气泡时状态自然消失，持久化的摘要由回答末尾那行痕迹承担。
/// </summary>
internal sealed class AssistantStatusPanel : StackPanel
{
    private const double IdleOpacity = 0.4d;
    private readonly Dictionary<int, Row> _rows = [];
    private readonly DispatcherTimer _timer;
    private Row? _thinking;
    private int _phase;
    private int _nextToken;

    public AssistantStatusPanel()
    {
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Visibility = Visibility.Collapsed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _timer.Tick += (_, _) => Animate();
    }

    /// <summary>可见性变化时通知宿主重排正文的上边距。</summary>
    public Action? OnLayoutChanged { get; set; }

    public void ShowThinking(string text)
    {
        if (_thinking is null)
            _thinking = AddRow("sparkles", text, active: true);
        else
            _thinking.Base = text;
        _thinking.Active = true;
        _thinking.Icon.Icon = "sparkles";
        _thinking.Text.Text = text;
        Refresh();
    }

    public void HideThinking()
    {
        if (_thinking is null) return;
        Remove(_thinking);
        _thinking = null;
        Refresh();
    }

    public int BeginTool(string icon, string label)
    {
        var row = AddRow(icon, label, active: true);
        Refresh();
        return row.Token;
    }

    public void EndTool(int token, bool ok, string note)
    {
        if (!_rows.TryGetValue(token, out var row)) return;
        row.Active = false;
        row.Icon.Opacity = 1d;
        row.Icon.Icon = ok ? "circle-check" : "triangle-alert";
        row.Text.Text = note.Length == 0 ? row.Base : $"{row.Base} · {note}";
        Refresh();
    }

    private Row AddRow(string icon, string text, bool active)
    {
        var svg = new SvgIcon
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Icon = icon
        };
        svg.SetResourceReference(SvgIcon.IconBrushProperty, "ColorBrush4");
        var label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush4");
        var line = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 5)
        };
        line.Children.Add(svg);
        line.Children.Add(label);
        Children.Add(line);

        var row = new Row(_nextToken++, svg, label, line) { Base = text, Active = active };
        _rows[row.Token] = row;
        return row;
    }

    private void Remove(Row row)
    {
        _rows.Remove(row.Token);
        Children.Remove(row.Line);
    }

    private void Animate()
    {
        _phase++;
        var busy = false;
        foreach (var row in _rows.Values)
        {
            if (!row.Active) continue;
            busy = true;
            // 图标呼吸 + 尾巴上的点，合起来就是"这件事还在做"
            row.Icon.Opacity = IdleOpacity + 0.6d * Math.Abs(Math.Sin(_phase * 0.4d));
            row.Text.Text = row.Base + new string('.', _phase % 4);
        }

        if (!busy) _timer.Stop();
    }

    private void Refresh()
    {
        Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_rows.Count > 0 && !_timer.IsEnabled) _timer.Start();
        OnLayoutChanged?.Invoke();
    }

    private sealed class Row(int token, SvgIcon icon, TextBlock text, StackPanel line)
    {
        public int Token { get; } = token;
        public SvgIcon Icon { get; } = icon;
        public TextBlock Text { get; } = text;
        public StackPanel Line { get; } = line;
        public string Base { get; set; } = "";
        public bool Active { get; set; }
    }
}
