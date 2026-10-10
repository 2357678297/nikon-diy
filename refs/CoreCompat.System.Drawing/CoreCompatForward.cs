using System.Runtime.CompilerServices;

// TypeForward shim:Accord 3.8.2-alpha 引用 CoreCompat.System.Drawing 的 Bitmap/Image,
// 统一转发到 System.Drawing.Common(net8 的 System.Drawing 实现)。
[assembly: TypeForwardedTo(typeof(System.Drawing.Bitmap))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Image))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Graphics))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Imaging.BitmapData))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Rectangle))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Point))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Size))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Color))]
