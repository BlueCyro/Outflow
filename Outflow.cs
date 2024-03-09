using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Core;
using MonoMod.Utils;
using System.Reflection;
using System.Collections.Concurrent;

namespace Outflow;

public class Outflow : ResoniteMod
{
    public override string Name => "Outflow";
    public override string Author => "Cyro";
    public override string Version => "1.0.0";
    public override string Link => "https://github.com/RileyGuy/Outflow";
    public static ModConfiguration? Config;

    public override void OnEngineInit()
    {
        Harmony harmony = new("net.Cyro.Outflow");
        Config = GetConfiguration();
        Config?.Save(true);
        MethodInfo encodeLoopInfo = typeof(Session).GetMethod("EncodeLoop", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo patchInfo = ((Delegate)Session_Patches.EncodeLoop_Prefix).Method;
        harmony.Patch(encodeLoopInfo, prefix: new(patchInfo));
    }



    public static class Session_Patches
    {
        public static bool EncodeLoop_Prefix(Session __instance, ref AutoResetEvent ___encodingThreadEvent, ref SpinQueue<SyncMessage> ___messagesToTransmit)
        {
            Type seshType = typeof(Session);
            var setDeltas = seshType.GetProperty("TotalSentDeltas").GetSetMethod(true).CreateDelegate<Action<object, int>>();
            var setFulls = seshType.GetProperty("TotalSentFulls").GetSetMethod(true).CreateDelegate<Action<object, int>>();
            var setConfirms = seshType.GetProperty("TotalSentConfirmations").GetSetMethod(true).CreateDelegate<Action<object, int>>();
            var setControls = seshType.GetProperty("TotalSentControls").GetSetMethod(true).CreateDelegate<Action<object, int>>();
            var setStreams = seshType.GetProperty("TotalSentStreams").GetSetMethod(true).CreateDelegate<Action<object, int>>();
            ConcurrentDictionary<Task, byte> fullBatchTasks = new();
            Queue<DeltaBatch> heldDeltas = new();
            FullBatcher batcher = new(__instance);

            
            
            Msg($"Replaced EncodeLoop successfully!");
            while (true)
            {
                ___encodingThreadEvent.WaitOne();
                if (__instance.IsDisposed)
                {
                    ___encodingThreadEvent.Dispose();
                    batcher.Dispose();
                    break;
                }
                else
                {
                    while (___messagesToTransmit.TryDequeue(out SyncMessage val))
                    {
                        if (val.Targets.Count > 0)
                        {
                            switch (val)
                            {
                                case DeltaBatch dtb:
                                    setDeltas(__instance, __instance.TotalSentDeltas + 1); // We've basically sent them... just not yet :)
                                    
                                    if (batcher.TryQueueMessage(dtb))
                                    {
                                        Debug("Queueing delta for later since full batch is processing");
                                        continue;
                                    }
                                    else
                                    {
                                        DateTime last = DateTime.Now;
                                        int flushed = batcher.FlushQueued(dtb);
                                        double totalMillis = (DateTime.Now - last).TotalMilliseconds;
                                        if (flushed > 0)
                                            Debug($"Flushed all queued deltas in {totalMillis}ms");

                                    }
                                    break;
                                case FullBatch fb:
                                    setFulls(__instance, __instance.TotalSentFulls + 1);
                                    Debug("FullBatch queued");
                                    batcher.QueueEncode(fb);
                                    continue;
                                case ConfirmationMessage _:
                                    setConfirms(__instance, __instance.TotalSentConfirmations + 1);
                                    break;
                                case ControlMessage ctm:
                                    setControls(__instance, __instance.TotalSentControls + 1);
                                    if (ctm.ControlMessageType == ControlMessage.Message.JoinStartDelta && batcher.TryQueueMessage(ctm))
                                    {
                                        Debug($"Queued JoinStartDelta");
                                        continue;
                                    }
                                    Debug($"JoinStartDelta sent normally");
                                    break;
                                case StreamMessage stm:
                                    setStreams(__instance, __instance.TotalSentStreams + 1);
                                    Debug($"Stream sent, sender version: {stm.SenderStateVersion}");
                                    break;
                            }
                            __instance.NetworkManager.TransmitData(val.Encode());
                        }
                        val.Dispose();
                    }
                }
            }
            return false;
        }
    }
}
