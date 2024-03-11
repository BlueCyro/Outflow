using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Reflection;
using System.Collections.Concurrent;
using System.Reflection.Emit;

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
        harmony.PatchAll();
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member



    /// <summary>
    /// Patches for FrooxEngine.Session
    /// </summary>
    [HarmonyPatch(typeof(Session))]
    public static class Session_Patches
    {
        /// <summary>
        /// Transpiler for <see cref="Session.EnqueueForTransmission(SyncMessage, bool)"/> to add StreamMessages to their own queue and skip inserting them the normal queue
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="iLGen"></param>
        /// <returns></returns>
        [HarmonyTranspiler]
        [HarmonyPatch("EnqueueForTransmission")]
        public static IEnumerable<CodeInstruction> EnqueueForTransmission_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iLGen)
        {
            yield return new(OpCodes.Ldarg_0); // Load the Session instance to pass to the enqueue method
            yield return new(OpCodes.Ldarg_1); // Load the SyncMessage to pass to the enqueue method
            yield return new(OpCodes.Call, ((Delegate)SessionHelpers.EnqueueStreamForTransmission).Method); // Call the enqueue method
            

            Label targetLabel = iLGen.DefineLabel();
            List<CodeInstruction> codes = instructions.ToList();
            
            
            bool foundJump = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(typeof(EventWaitHandle).GetMethod("Set")))
                {
                    codes[i + 2].labels.Add(targetLabel); // Assign a label to the 'if' statement that appears right after streams are normally encoded
                    foundJump = true;
                    Msg("Successfully found jump point");
                    break;
                }
            }


            if (!foundJump)
                throw new KeyNotFoundException("Could not find jump point, aborting!");


            // Jump straight to the 'if' statement and skip enqueueing into the normal message queue if we have a StreamMessage. Ensures stats are still incremented properly
            yield return new(OpCodes.Brtrue_S, targetLabel);
            
            // Return the rest of the function, unchanged
            foreach (var code in codes)
            {
                yield return code;
            }


            Msg("Successfully patched Session.EnqueueForTransmission()");
        }



        /// <summary>
        /// Prefixes Session.Run() to start a thread to process exclusively SyncMessages
        /// </summary>
        /// <param name="__instance">The session to process StreamMessages for</param>
        [HarmonyPrefix]
        [HarmonyPatch("Run")]
        public static void Run_Prefix(Session __instance)
        {
            // Private so reflection is required, not really a big deal to do it the easy way since this isn't a hot piece of code
            MethodInfo runThread =
                typeof(Session)
                .GetMethod("RunThreadLoop", BindingFlags.Instance | BindingFlags.NonPublic);


            AutoResetEvent ev = new(false);
            ConcurrentQueue<SyncMessage> queue = new();


            // Start a new thread to process exclusively StreamMessages
            Thread streamThread = new(() => runThread.Invoke(__instance, [() => SessionHelpers.StreamLoop(__instance, ev, queue)]))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "StreamMessage Encoding"
            };
            streamThread.Start();


            // Add the event and the queue to a dictionary so it can be accessed from other methods
            __instance.AddStreamQueue(ev, queue);
            Msg("Starting StreamMessage processing on a separate thread");
        }



        /// <summary>
        /// Applies a Transpiler patch on <see cref="Session.Dispose"/> to properly shut down the StreamMessage processing thread
        /// </summary>
        /// <param name="instructions"></param>
        [HarmonyTranspiler]
        [HarmonyPatch("Dispose")]
        public static IEnumerable<CodeInstruction> Dispose_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo disposeCall = typeof(Session).GetProperty("IsDisposed").GetSetMethod(true);

            foreach (var code in instructions)
            {
                if (code.Calls(disposeCall))
                {
                    yield return code; // Emit the original IsDisposed setter
                    yield return new(OpCodes.Ldarg_0); // Load the Session instance to pass to the disposer method
                    yield return new(OpCodes.Call, ((Delegate)SessionHelpers.DisposeStreamMessageProcessor).Method); // Emit a call to the disposer method
                }
                else
                {
                    yield return code;
                }
            }
        }



        /// <summary>
        /// Postfixes the getter for <see cref="Session.MessagesToTransmitCount"/> to include the StreamMessage queue in the statistic to remain accurate
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        [HarmonyPostfix]
        [HarmonyPatch("MessagesToTransmitCount", MethodType.Getter)]
        public static void MessagesToTransmitCount_Postfix(Session __instance, ref int __result)
        {
            if (SessionHelpers.SessionStreamQueue.TryGetValue(__instance, out var data))
            {
                __result += data.streamMessagesToSend.Count;
            }
        }
    }
}