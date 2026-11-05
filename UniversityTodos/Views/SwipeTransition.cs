using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace UniversityTodos.Views;

/// <summary>移动端手势后的短距离位移动画，终值始终回到 0，不改变布局。</summary>
internal static class SwipeTransition
{
    public static void Play(Control target, double fromX, int durationMs = 180)
    {
        var transform = new TranslateTransform(fromX, 0);
        transform.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                Easing = new CubicEaseOut(),
            },
        ];
        target.RenderTransform = transform;

        // 等控件完成本轮可见性切换和布局后再设置终值，确保过渡确实触发。
        Dispatcher.UIThread.Post(() => transform.X = 0, DispatcherPriority.Loaded);
    }
}
