using System;

namespace CameraControl.Devices
{
    /// <summary>
    /// .NET 8+ 的 BitConverter 没有 GetBytes(byte) 单参重载(实际运行时有,ref pack 遗漏),
    /// 迁移 net8 后原代码中 GetBytes((byte)x)/GetBytes((sbyte)x) 调用点产生 CS0121 歧义,在此补齐。
    /// </summary>
    public static class BitConverterExtensions
    {
        public static byte[] GetBytes8(this byte value) => new[] { value };

        public static byte[] GetBytes8(this sbyte value) => new[] { unchecked((byte)value) };
    }
}
