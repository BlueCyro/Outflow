using FrooxEngine;
using System.Collections.Concurrent;

namespace Outflow;

/// <summary>
/// Helper methods for interacting with a <see cref="Session"/>'s StreamMessage queue
/// </summary>
public static class SessionHelpers
{
    /// <summary>
    /// Correlation between sessions and StreamMessage processing data
    /// </summary>
    public static ConcurrentDictionary<Session, (AutoResetEvent streamSendEvent, ConcurrentQueue<SyncMessage> streamMessagesToSend)> SessionStreamQueue = new();



    /// <summary>
    /// Enqueues a StreamMessage into that session's StreamMessage queue
    /// </summary>
    /// <param name="session">The session to enqueue a StreamMessage for</param>
    /// <param name="msg">The message to enqueue</param>
    /// <returns>Whether the message was a stream</returns>
    public static bool EnqueueStreamForTransmission(Session session, SyncMessage msg)
    {
        bool isStream = msg is StreamMessage;


        if (isStream && SessionStreamQueue.TryGetValue(session, out var data))
        {
            data.streamMessagesToSend.Enqueue(msg);
            data.streamSendEvent.Set();
        }
        

        #if DEBUG
        Outflow.Debug($"Encoded StreamMessage with state #{msg.SenderStateVersion}");
        #endif

        return isStream;
    }



    /// <summary>
    /// The processing loop for processing StreamMessages exclusively
    /// </summary>
    /// <param name="session">The session to operate on</param>
    /// <param name="ev">The event to wait on</param>
    /// <param name="streamMessagesToSend">The queue to operate on</param>
    public static void StreamLoop(Session session, AutoResetEvent ev, ConcurrentQueue<SyncMessage> streamMessagesToSend)
    {
        var setStreamCount = (Action<int>) // Cache setter for TotalSentStreams
            typeof(Session)
            .GetProperty("TotalSentStreams")
            .GetSetMethod(true)
            .CreateDelegate(typeof(Action<int>), session);


        Outflow.Msg($"StreamMessage processing successfully initiated!");


        while (true)
        {
            ev.WaitOne();
            if (session.IsDisposed) // Break on disposal
                break;


            while (streamMessagesToSend.TryDequeue(out SyncMessage result))
            {
                if (result.Targets.Count > 0)
                {
                    setStreamCount(session.TotalSentStreams + 1);
                    session.NetworkManager.TransmitData(result.Encode());


                    #if DEBUG
                    Outflow.Debug($"Successfully processed StreamMessage #{result.SenderStateVersion}");
                    #endif
                }


                result.Dispose();
            }
        }


        ev.Dispose();
        Outflow.Msg($"StreamMessage processing loop terminated for {session.World.Name}");
    }



    /// <summary>
    /// Removes a StreamMessage queue from the dictionary for a given session
    /// </summary>
    /// <param name="session">The session to remove a queue from</param>
    public static void RemoveStreamQueue(this Session session)
    {
        if (SessionStreamQueue.ContainsKey(session))
        {
            SessionStreamQueue.TryRemove(session, out var _);


            #if DEBUG
            Outflow.Debug($"Removed StreamMessage queue");
            #endif
        }
    }



    /// <summary>
    /// Adds a StreamMessage queue to the dictionary for a given session
    /// </summary>
    /// <param name="session">The session to add the queue for</param>
    /// <param name="ev">The event the queue processor will wait on</param>
    /// <param name="queue">The queue the processor will operate with</param>
    public static void AddStreamQueue(this Session session, AutoResetEvent ev, ConcurrentQueue<SyncMessage> queue)
    {
        SessionStreamQueue.TryAdd(session, (ev, queue));


        #if DEBUG
        Outflow.Debug($"Added StreamMessage queue");
        #endif
    }



    /// <summary>
    /// Only called when the Session is disposed, disposes of the StreamMessage processor
    /// </summary>
    /// <param name="session">The session to operate on</param>
    public static void DisposeStreamMessageProcessor(Session session)
    {
        if (SessionStreamQueue.TryGetValue(session, out var data))
            data.streamSendEvent.Set();
        
        Outflow.Msg("Properly shut down StreamMessage processing thread");
        session.RemoveStreamQueue();
    }
}