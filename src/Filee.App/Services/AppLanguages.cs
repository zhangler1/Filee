// The languages Filee Lite ships (Assets/i18n/<code>.json).

namespace Filee.App.Services;

internal static class AppLanguages
{
    /// <summary>Code → native name (shown in the language picker of the General page).</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> All =
    [
        ("en", "English"),
        ("zh-CN", "简体中文"),
    ];
}
