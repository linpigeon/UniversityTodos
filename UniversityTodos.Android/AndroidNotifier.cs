using System;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using UniversityTodos.Services;

namespace UniversityTodos.Android;

/// <summary>Android 系统通知：通知栏、横幅与点击回到应用，和桌面 Toast 共用同一套通知抽象。</summary>
internal sealed class AndroidNotifier : INotifier
{
    internal const string ChannelId = "universitytodos.reminders";
    private const string ChannelName = "作业与课程提醒";
    private static int _nextId;

    public AndroidNotifier()
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            var manager = (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
            {
                Description = "作业截止、课程开始和每日逾期汇总",
            };
            channel.SetShowBadge(true);
            manager?.CreateNotificationChannel(channel);
        }
        catch (Exception)
        {
            // 通道创建失败会在 Show 时返回更具体的原因。
        }
    }

    public bool IsAvailable =>
        HasPermission() && NotificationsEnabled();

    public string? UnavailableReason
    {
        get
        {
            if (!HasPermission()) return "尚未授予通知权限，请在系统设置中允许通知。";
            if (!NotificationsEnabled()) return "系统设置已关闭本应用的通知。";
            return null;
        }
    }

    public string? Show(AppNotification notification)
    {
        if (!HasPermission()) return UnavailableReason;
        if (!NotificationsEnabled()) return UnavailableReason;

        try
        {
            var context = Application.Context;
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager is null) return "系统通知服务不可用。";

            var id = Interlocked.Increment(ref _nextId);
            var launch = new Intent(context, typeof(MainActivity));
            launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pending = PendingIntent.GetActivity(
                context, id, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Notification.Builder(context, ChannelId)
                : CreateLegacyBuilder(context);
            builder
                .SetContentTitle(notification.Title)
                .SetContentText(notification.Body)
                .SetStyle(new Notification.BigTextStyle().BigText(notification.Body))
                .SetSmallIcon(Resource.Drawable.Icon)
                .SetContentIntent(pending)
                .SetAutoCancel(true)
                .SetShowWhen(true)
                .SetWhen(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .SetCategory(Notification.CategoryReminder);

            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                builder.SetPriority((int)NotificationPriority.High);

            if (!string.IsNullOrWhiteSpace(notification.Attribution))
                builder.SetSubText(notification.Attribution);

            manager.Notify(id, builder.Build());
            return null;
        }
        catch (Exception ex)
        {
            return $"发送系统通知失败：{ex.Message}";
        }
    }

    private static bool HasPermission()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;

        return Application.Context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
               == Permission.Granted;
    }

    private static bool NotificationsEnabled()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(24)) return true;

        var manager = (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
        return manager?.AreNotificationsEnabled() != false;
    }

#pragma warning disable CA1422
    private static Notification.Builder CreateLegacyBuilder(Context context) => new(context);
#pragma warning restore CA1422
}
