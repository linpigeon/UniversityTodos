using System;

namespace UniversityTodos.Services;

/// <summary>
/// 学习通页面地址。集中在一处，避免「任务点请求」与「界面直达按钮」各拼一份后悄悄分叉。
/// </summary>
public static class ChaoxingUrls
{
    /// <summary>
    /// 课程首页（<c>stucoursemiddle</c>）。
    /// <para>
    /// courseId / clazzId / personId 三元组缺一不可且不能混用；
    /// 任一 id 不成立时返回 null，宁可禁用入口也不拼一个会打开空白页（或别的课）的地址。
    /// </para>
    /// </summary>
    public static string? CoursePage(long courseId, long clazzId, long personId)
    {
        if (courseId <= 0 || clazzId <= 0 || personId <= 0) return null;

        return FormattableString.Invariant(
            $"https://mooc1-2.chaoxing.com/visit/stucoursemiddle?courseid={courseId}&clazzid={clazzId}&vc=1&cpi={personId}");
    }
}
