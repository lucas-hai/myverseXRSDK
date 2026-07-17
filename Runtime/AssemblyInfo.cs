using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MyVerse.XRSDK.Tests")]
[assembly: InternalsVisibleTo("MyVerse.XRSDK.Tests.PlayMode")]
// 包内 Editor 程序集需要调 internal 的地块封签工具（RegionDataSeal）
[assembly: InternalsVisibleTo("MVXRSDK.Editor")]
