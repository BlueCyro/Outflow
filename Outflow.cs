using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Core;
using MonoMod.Utils;
using System.Reflection;
using System.Collections.Concurrent;

namespace Outflow;


#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class Outflow : ResoniteMod
{
    public override string Name => "Outflow";
    public override string Author => "Cyro";
    public override string Version => typeof(Outflow).Assembly.GetName().Version.ToString();
    public override string Link => "https://github.com/RileyGuy/Outflow";
    public static ModConfiguration? Config;

    public override void OnEngineInit()
    {
        Harmony harmony = new("net.Cyro.Outflow");
        Config = GetConfiguration();
        Config?.Save(true);
        
        // Manual patch
        MethodInfo encodeLoopInfo = typeof(Session).GetMethod("EncodeLoop", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo patchInfo = ((Delegate)Session_Patches.EncodeLoop_Prefix).Method;

        harmony.Patch(encodeLoopInfo, prefix: new(patchInfo));
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member




    /// <summary>
    /// Patches for FrooxEngine.Session
    /// </summary>
    public static class Session_Patches
    {
        /// <summary>
        /// Replaces the EncodeLoop method with a prefix
        /// </summary>
        /// <param name="__instance">Session instance to work on</param>
        /// <param name="___encodingThreadEvent">Event to trigger an encode cycle</param>
        /// <param name="___messagesToTransmit">Queue of messages to transmit</param>
        /// <returns>Whether to run the original function</returns>
        public static bool EncodeLoop_Prefix(Session __instance, ref AutoResetEvent ___encodingThreadEvent, ref SpinQueue<SyncMessage> ___messagesToTransmit)
        {
            // Since this method is no longer coming from the class itself, reflection is required for all of the property setters to properly increment the stats
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
                ___encodingThreadEvent.WaitOne(); // Wait for next encoding cycle


                if (__instance.IsDisposed)
                {
                    ___encodingThreadEvent.Dispose();
                    batcher.Dispose();
                    break;
                }


                while (___messagesToTransmit.TryDequeue(out SyncMessage val)) // De-queue messages to send
                {
                    DateTime last = DateTime.Now;
                    int flushed = batcher.TryFlushQueuedMessages(); // Try to flush queued messages. Only flushes if no FullBatches are cooking and messages are ready to be de-queued
                    double totalMillis = (DateTime.Now - last).TotalMilliseconds;
                    

                    if (val.Targets.Count > 0)
                    {
                        switch (val)
                        {
                            case DeltaBatch dtb:
                                setDeltas(__instance, __instance.TotalSentDeltas + 1);
                                break;
                            case FullBatch fb:
                                setFulls(__instance, __instance.TotalSentFulls + 1);
                                break;
                            case ConfirmationMessage cfm:
                                setConfirms(__instance, __instance.TotalSentConfirmations + 1);
                                break;
                            case ControlMessage ctm:
                                setControls(__instance, __instance.TotalSentControls + 1);
                                break;
                            case StreamMessage stm:
                                setStreams(__instance, __instance.TotalSentStreams + 1);
                                Debug($"Stream sent, sender version: {stm.SenderStateVersion}");
                                break;
                        }


                        if (batcher.TryQueueMessage(val)) // Catch all reliable messages if a FullBatch is cooking, leave streams
                            continue;
                        

                        __instance.NetworkManager.TransmitData(val.Encode());
                    }
                    val.Dispose();
                }
            }
            return false;
        }
    }
}
