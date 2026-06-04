# Third Party Notices

本 SDK 包含或依赖以下第三方组件。各组件的版权与许可条款见下方逐项列出。

---

## 嵌入式 DLL

### Google.Protobuf

- **位置**：`Runtime/Plugins/pbLib/Google.Protobuf.dll`
- **来源**：https://github.com/protocolbuffers/protobuf
- **许可**：BSD-3-Clause

```
Copyright 2008 Google Inc.  All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

    * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
    * Neither the name of Google Inc. nor the names of its
contributors may be used to endorse or promote products derived from
this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### ICSharpCode.SharpZipLib

- **位置**：`Runtime/Plugins/SharpZipLib/ICSharpCode.SharpZipLib.dll`
- **来源**：https://github.com/icsharpcode/SharpZipLib
- **许可**：MIT

```
Copyright © 2000-2018 SharpZipLib Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
```

### zlib.net

- **位置**：`Runtime/Plugins/SharpZipLib/zlib.net.dll`
- **来源**：https://www.componentace.com/zlib_.NET.htm（zlib 算法的 .NET 移植）
- **许可**：zlib License

```
Copyright (C) 1995-2017 Jean-loup Gailly and Mark Adler

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from
the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### System.Buffers / System.Memory / System.Runtime.CompilerServices.Unsafe

- **位置**：`Runtime/Plugins/pbLib/System.Buffers.dll`、`System.Memory.dll`、`System.Runtime.CompilerServices.Unsafe.dll`
- **来源**：https://github.com/dotnet/runtime（.NET Foundation）
- **许可**：MIT

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
```

---

## UPM 依赖（运行时由 Package Manager 拉取）

下列依赖通过 `package.json` 的 `dependencies` 字段引入，遵循其各自的许可：

| 包名 | 许可 | 来源 |
|---|---|---|
| `com.unity.webrtc` | Unity Companion License | https://docs.unity3d.com/Packages/com.unity.webrtc@3.0 |
| `com.unity.render-pipelines.universal` | Unity Companion License | https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0 |

---

如发现遗漏或错误，请联系 support@myverse.com。
