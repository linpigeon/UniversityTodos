using System;

namespace UniversityTodos.Services;

/// <summary>
/// 数据文件格式版本与「只读保护」。
/// <para>
/// 场景：用户先用了更新版本（写入了更高 <c>SchemaVersion</c> 的数据），又降级回旧版本。
/// 旧版本不认识新字段，若照常保存就会用默认值/缺字段的结构**覆盖掉新数据**。
/// 这里在读取时记下「见到过比当前更高版本的文件」，之后本进程一律拒绝写盘，
/// 并在界面上提示用户升级回去——宁可本次不能改，也不能悄悄损坏数据。
/// </para>
/// </summary>
public static class DataCompatibility
{
    /// <summary>当前程序理解的最高数据格式版本。改变持久化结构时递增。</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly object Gate = new();
    private static int _newerVersion;
    private static string _newerFile = string.Empty;

    /// <summary>是否检测到更高版本的数据文件（检测到后本次运行进入只读）。</summary>
    public static bool IsReadOnly
    {
        get { lock (Gate) return _newerVersion > CurrentSchemaVersion; }
    }

    /// <summary>触发只读的文件与版本（给用户看）。</summary>
    public static string? ReadOnlyReason
    {
        get
        {
            lock (Gate)
            {
                if (_newerVersion <= CurrentSchemaVersion) return null;
                return $"{_newerFile} 的数据版本为 {_newerVersion}，高于当前程序支持的 {CurrentSchemaVersion}。"
                       + "为避免覆盖新数据，本次以只读模式运行；请升级到更新版本后再修改。";
            }
        }
    }

    /// <summary>读取文件后调用：记下是否见过更高版本。</summary>
    public static void Observe(int fileVersion, string path)
    {
        if (fileVersion <= CurrentSchemaVersion) return;
        lock (Gate)
        {
            if (fileVersion <= _newerVersion) return;
            _newerVersion = fileVersion;
            _newerFile = path;
        }
    }

    /// <summary>供自检复位（生产路径不会调用）。</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _newerVersion = 0;
            _newerFile = string.Empty;
        }
    }
}
