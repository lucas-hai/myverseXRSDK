using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 地块数据防篡改封签（方案一：HMAC-SHA256，密钥混淆内置于 SDK）。
    /// 目标：防第三方绕过地块编辑器直接改本地资产（有意或无意）导致本地与平台注册数据不一致；
    /// 不防逆向攻击——客户端内置密钥理论可提取，本产品（局域网形态）不追求该等级。
    /// 用地块编辑器修改必然经过上传门禁（需该环境已验证凭据），数据保持与平台一致，不构成绕过。
    ///
    /// 规范化格式 v2（升级服务端签名方案时保持不变，仅替换签名生产方与校验算法）：
    ///   payload = "v2|env=&lt;int&gt;" + 排序后的条目片段（每段 "|id=&lt;len&gt;:&lt;id&gt;,l=&lt;bits&gt;,w=&lt;bits&gt;,px..rz=&lt;bits&gt;"）
    ///   - float 取 IEEE754 位模式 hex（不走十进制格式化——"F6" 等格式在不同 BCL/运行时间有差异，
    ///     如 -0f 在 .NET Framework 格式化无负号而 .NET Core 3.0+ 保留负号，编辑器签名/设备验签会错配）；
    ///   - id 带长度前缀，防 id 含定界符时的 payload 歧义（null 与空串同编码——两者运行时都匹配不到，语义等价）；
    ///   - 按**整段片段**排序（Ordinal）：排序键含全部字段，同 id 条目也有确定次序（List.Sort 不稳定，
    ///     仅按 id 排序时同 id 条目次序平台相关 → 签名不一致）。
    /// </summary>
    internal static class RegionDataSeal
    {
        // HMAC 密钥（XOR 混淆存放，避免二进制里明文可搜；GetKey 还原）
        private const byte k_Mask = 0x5C;
        private static readonly byte[] k_ObfKey =
        {
            0x3E, 0x6B, 0x0F, 0x35, 0x29, 0x74, 0x18, 0x62,
            0x51, 0x0D, 0x3A, 0x66, 0x2F, 0x70, 0x1C, 0x08,
            0x45, 0x33, 0x7E, 0x12, 0x5B, 0x27, 0x69, 0x04,
            0x58, 0x1F, 0x73, 0x3C, 0x0A, 0x61, 0x26, 0x77,
        };

        private static byte[] GetKey()
        {
            var key = new byte[k_ObfKey.Length];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(k_ObfKey[i] ^ k_Mask);
            return key;
        }

        /// <summary>对环境组算封签（hex 小写）。entries 为 null 视为空组。</summary>
        internal static string ComputeSignature(SdkEnvironment env, List<LocalRegionData> entries)
        {
            var payload = BuildCanonicalPayload(env, entries);
            using (var hmac = new HMACSHA256(GetKey()))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 数据变更（保存/删除/重传完成）后重写组封签——Offline 组恒无签名（本地直存语义），
        /// 其余环境重算 HMAC。封签规则唯一出口，调用方不要自行给 signature 赋值。
        /// </summary>
        internal static void Reseal(EnvironmentTileSet set)
        {
            if (set == null) return;
            set.signature = set.env == SdkEnvironment.Offline
                ? null
                : ComputeSignature(set.env, set.entries);
        }

        /// <summary>
        /// 校验环境组封签。Offline 组免验恒真（本地直存语义）；
        /// 其余环境签名缺失或不匹配均为失败（缺失若视为通过，删掉签名即可绕过）。
        /// </summary>
        internal static bool Verify(EnvironmentTileSet set)
        {
            if (set == null) return false;
            if (set.env == SdkEnvironment.Offline) return true;
            if (string.IsNullOrEmpty(set.signature)) return false;
            var expected = ComputeSignature(set.env, set.entries);
            return string.Equals(expected, set.signature, StringComparison.Ordinal);
        }

        private static string BuildCanonicalPayload(SdkEnvironment env, List<LocalRegionData> entries)
        {
            var sb = new StringBuilder(256);
            sb.Append("v2|env=").Append((int)env);
            if (entries != null && entries.Count > 0)
            {
                // 每条目先生成完整片段，再按片段整体排序：排序键覆盖全部字段，与资产存储顺序解耦且无同键歧义
                var fragments = new List<string>(entries.Count);
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    var id = e.id ?? string.Empty;
                    fragments.Add($"id={id.Length}:{id}" +
                                  $",l={F(e.len)},w={F(e.width)}" +
                                  $",px={F(e.position.x)},py={F(e.position.y)},pz={F(e.position.z)}" +
                                  $",rx={F(e.rotation.x)},ry={F(e.rotation.y)},rz={F(e.rotation.z)}");
                }
                fragments.Sort(StringComparer.Ordinal);
                foreach (var fragment in fragments)
                    sb.Append('|').Append(fragment);
            }
            return sb.ToString();
        }

        // float 规范化：IEEE754 位模式 hex（GetBytes+ToInt32 同机自洽、跨端序得到相同数值），
        // 完全绕开十进制格式化的跨运行时差异
        private static string F(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("x8");
    }
}
