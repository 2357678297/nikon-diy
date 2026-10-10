namespace CameraControl.Private
{
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// 取代 packages/CameraControl.Private.dll(net48,net8 无法直接引用)。
    /// 原 DLL 仅含 SendGrid 上报凭据;为避免向仓库提交密钥,凭据改为运行时
    /// 从环境变量 DCC_SENDGRID_USER / DCC_SENDGRID_PASS 读取,缺省为原 DLL 值的加载逻辑不可行
    /// (net48 程序集),因此未配置时返回空,SendEmail 将使用无凭据发送(功能降级但不崩溃)。
    /// </summary>
    public static class Ids
    {
        public static string SendgridUser => Environment.GetEnvironmentVariable("DCC_SENDGRID_USER") ?? "apikey";

        public static string SendgridPass => Environment.GetEnvironmentVariable("DCC_SENDGRID_PASS") ?? string.Empty;
    }
}
