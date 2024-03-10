using System.Collections.Concurrent;
using FrooxEngine;

namespace Outflow;


/// <summary>
/// Encodes FullBatches and queues reliable messages to be sent when processing is complete
/// </summary>
public class FullBatcher : IDisposable
{
    /// <summary>
    /// Whether any FullBatch messages are being encoded
    /// </summary>
    public bool IsProcessing => batcherTasks.Count > 0;

    /// <summary>
    /// True if the batcher is no longer valid for use
    /// </summary>
    public bool Disposed { get; private set; }
    
    private readonly ConcurrentQueue<FullBatch> batcherTasks = new();
    private readonly ConcurrentQueue<RawOutMessage> readyBatches = new();
    private readonly ConcurrentQueue<RawOutMessage> reliableQueue = new();
    private readonly AutoResetEvent reset = new(false);
    private readonly Thread processThread;
    private readonly Session session;


    
    /// <summary>
    /// Instantiates a batcher for a given Session
    /// </summary>
    /// <param name="curSession">The session to operate on</param>
    public FullBatcher(Session curSession)
    {
        processThread = new(Process);
        processThread.Start();
        session = curSession;
    }



    /// <summary>
    /// Queues a FullBatch to be encoded, implicitly sets IsProcessing
    /// </summary>
    /// <param name="batch">The patch to process</param>
    public void QueueFullEncode(FullBatch batch)
    {
        batcherTasks.Enqueue(batch);
        reset.Set();
        Outflow.Debug($"Queued FullBatch for #{batch.SenderStateVersion}\nFullBatch queue count: {batcherTasks.Count}");
    }



    /// <summary>
    /// Tries to queue a reliable message. If no work is being done, then queueing will be skipped
    /// </summary>
    /// <param name="reliable">The message to encode</param>
    /// <returns></returns>
    public bool TryQueueMessage(SyncMessage reliable)
    {
        if (reliable is FullBatch full)
        {
            QueueFullEncode(full);
        }
        else if (IsProcessing && reliable is not StreamMessage)
        {
            reliableQueue.Enqueue(reliable.Encode());
            Outflow.Debug($"Work is processing, queued #{reliable.SenderStateVersion}: {reliable.GetType().Name}{(reliable is ControlMessage msg ? $",{msg.ControlMessageType}" : "")}\nQueue has {reliableQueue.Count}");
            reliable.Dispose();
        }
        else
        {
            return false;
        }
        
        return true;

    }



    private void Process()
    {
        while (!Disposed)
        {
            reset.WaitOne(); // Wait for signal to start encoding
            if (Disposed)
                break;
            

            DateTime last = DateTime.Now;
            while (batcherTasks.TryPeek(out FullBatch batch) && !Disposed) // Only peek to keep it in the queue until processing is done
            {
                DateTime beforeEncode = DateTime.Now;
                RawOutMessage encoded = batch.Encode();
                TimeSpan afterEncode = DateTime.Now - beforeEncode;

                
                Outflow.Debug($"Encode took {afterEncode.TotalMilliseconds}ms");


                batch.Dispose();
                batcherTasks.TryDequeue(out FullBatch fb); // Now actually de-queue the batch. If the queue becomes empty then IsProcessing returns false as it should


                if (fb != batch)
                {
                    Outflow.Debug($"Message queue was modified after peek");
                    continue;
                }


                readyBatches.Enqueue(encoded); // Load 'er up partner
            }
            double totalMillis = (DateTime.Now - last).TotalMilliseconds;
            Outflow.Debug($"FullBatches done processing in {totalMillis}ms");
        }
    }



    /// <summary>
    /// Attempts to flush the queued messages. Only flushes when work is being done and messages are queued
    /// </summary>
    /// <returns>How many messages were flushed. -2 if processing is going on, -1 if there are simply no messages to flush</returns>
    public int TryFlushQueuedMessages()
    {
        if (IsProcessing)
        {
            Outflow.Debug($"Returning -2 since processing is going on");
            return -2;
        }


        if (reliableQueue.Count == 0 && readyBatches.Count == 0)
        {
            Outflow.Debug($"Returning -1 since no messages are in queue");
            return -1;
        }


        Outflow.Debug($"Ready full batches: {readyBatches.Count}");
        Outflow.Debug($"Ready reliable messages: {reliableQueue.Count}");
        

        int flushed = 0;
        DateTime last = DateTime.Now;


        while (readyBatches.TryDequeue(out RawOutMessage full)) // Transmit all fulls
        {
            Outflow.Debug("Flushing encoded FullBatch");
            session.NetworkManager.TransmitData(full);
            flushed++;
        }

        while (reliableQueue.TryDequeue(out RawOutMessage queued)) // Transmit all queued reliable messages afterwards
        {
            Outflow.Debug($"Flushing reliable message");
            session.NetworkManager.TransmitData(queued);
            flushed++;
        }


        double totalMillis = (DateTime.Now - last).TotalMilliseconds;
        Outflow.Debug($"Full queue: {readyBatches.Count}\nMessage queue: {reliableQueue.Count}\nReal FullBatch queue: {batcherTasks.Count}\nTook {totalMillis}ms");
        return flushed;
    }



    /// <summary>
    /// Disposes of the batcher and stops all processing
    /// </summary>
    public void Dispose()
    {
        Disposed = true;
        reset.Set();
    }



    /// <summary>
    /// If somehow the batcher is orphaned, dispose of it properly
    /// </summary>
    ~FullBatcher()
    {
        Dispose();
    }
}