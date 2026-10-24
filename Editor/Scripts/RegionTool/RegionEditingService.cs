using System;
using System.Collections.Generic;
using UnityEditor;

namespace MyVerseXRSDK.Editor
{
    /// <summary>保存/重新封签的编辑用例；远端成功不赋予覆盖后续本地编辑的权限。</summary>
    internal sealed class RegionEditingService
    {
        private readonly IRegionDataUploader uploader;
        private readonly Action<LocalRegionDataList> persist;

        internal RegionEditingService(IRegionDataUploader uploader, Action<LocalRegionDataList> persist = null)
        { this.uploader = uploader; this.persist = persist ?? PersistAsset; }

        internal void Save(EditorApiContext context, LocalRegionDataList target, LocalRegionData draft,
            bool isNew, int index, Action<bool, string> completed)
        {
            if (context == null || target == null || draft == null || string.IsNullOrWhiteSpace(draft.id))
            { completed(false, "保存目标、环境或条目无效"); return; }
            var existing = target.GetSet(context.Environment);
            if ((!isNew && (existing == null || index < 0 || index >= existing.entries.Count)) ||
                (isNew && target.ContainsId(context.Environment, draft.id)))
            { completed(false, "条目下标已失效或id已存在"); return; }
            var revision = new EditorAssetRevision(target);
            var snapshot = draft.Clone();
            bool finished = false;
            void Finish(bool uploaded, string error)
            {
                if (finished) return;
                finished = true;
                if (!uploaded) { completed(false, error); return; }
                if (!revision.Matches())
                { completed(false, "上传期间资产已修改或删除；远端可能已接受，本地未覆盖，请检查后重试"); return; }
                try
                {
                    Undo.RecordObject(target, "保存地块条目");
                    var set = target.GetOrCreateSet(context.Environment);
                    if (isNew) set.entries.Add(snapshot);
                    else set.entries[index] = snapshot;
                    RegionDataSeal.Reseal(set);
                    persist(target);
                }
                catch (Exception failure) { completed(false, "本地保存失败：" + failure.Message); return; }
                completed(true, null);
            }
            if (!context.IsRemote) { Finish(true, null); return; }
            try { uploader.Upload(context, snapshot, () => Finish(true, null), error => Finish(false, error)); }
            catch (Exception error) { Finish(false, "发起上传失败：" + error.Message); }
        }

        internal void Reseal(EditorApiContext context, LocalRegionDataList target, Action<bool, string> completed)
        {
            var set = target != null && context != null ? target.GetSet(context.Environment) : null;
            if (set == null) { completed(false, "指定环境的地块组不存在"); return; }
            var revision = new EditorAssetRevision(target);
            var snapshot = new List<LocalRegionData>(set.entries.Count);
            foreach (var entry in set.entries)
            {
                if (entry == null) { completed(false, "地块组包含空条目，不能封签"); return; }
                snapshot.Add(entry.Clone());
            }
            int next = 0;
            bool finished = false;
            bool pumping = false;
            bool requested = false;
            void Finish(bool success, string error)
            {
                if (finished) return;
                finished = true;
                completed(success, error);
            }
            void Advance()
            {
                requested = true;
                if (pumping || finished) return;
                pumping = true;
                try
                {
                    // 同步测试端口和异步HTTP共用状态机，避免同步完成时递归耗尽栈。
                    while (requested && !finished)
                    {
                        requested = false;
                        if (!revision.Matches())
                        { Finish(false, "封签期间资产已修改或删除，签名未写入；部分条目可能已上传"); break; }
                        if (!context.IsRemote || next == snapshot.Count)
                        {
                            Undo.RecordObject(target, "重新封签地块");
                            RegionDataSeal.Reseal(set);
                            persist(target);
                            Finish(true, null);
                            break;
                        }
                        bool replied = false;
                        uploader.Upload(context, snapshot[next], () =>
                        {
                            if (replied || finished) return;
                            replied = true;
                            next++;
                            Advance();
                        }, error =>
                        {
                            if (replied || finished) return;
                            replied = true;
                            Finish(false, "重新封签上传失败，签名未写入：" + error);
                        });
                    }
                }
                catch (Exception error) { Finish(false, "重新封签失败：" + error.Message); }
                finally { pumping = false; }
            }
            Advance();
        }

        private static void PersistAsset(LocalRegionDataList target)
        {
            EditorUtility.SetDirty(target);
            // 只保存本次目标，不能顺带保存用户其他尚未提交的资产编辑。
            if (AssetDatabase.Contains(target)) AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}
