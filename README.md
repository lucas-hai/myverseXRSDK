# MyVerse XR SDK (`com.myverse.xrsdk`)

多人 XR 协作 SDK：房间同步、空间对齐、WebRTC 推流（WHIP）、导播切镜头。

- Unity 2022.3 LTS+
- URP 14.0.11+（必需）
- 目标平台：PICO 4 / 4U（OpenXR）、Android

## 安装（Unity Package Manager · Git URL）

Window → Package Manager → 左上角 `+` → **Add package from git URL**，填入：

```
https://github.com/lucas-hai/myverseXRSDK.git#v3.0.1
```

`#v3.0.1` 是版本 tag，建议始终带上以锁定版本；省略则拉默认分支最新提交。

也可写进 `Packages/manifest.json`：

```json
"com.myverse.xrsdk": "https://github.com/lucas-hai/myverseXRSDK.git#v3.0.1"
```

### 依赖说明

安装时 Unity 会自动解析以下依赖（无需手动添加）：

- `com.unity.webrtc` `3.0.0-pre.8`
- `com.unity.nuget.newtonsoft-json` `3.0.2`
- `com.unity.render-pipelines.universal` `14.0.11`

> 私有仓库需在本机配置好 Git 凭据（SSH key 或 HTTPS token），Unity 才能拉取。

## 示例

Package Manager 选中本包 → Samples → 导入 **Demo**（SDK 全模块联合示例）。

完整 API 手册见 `Documentation~/index.md`。
