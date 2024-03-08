using System.Collections.Concurrent;
using FrooxEngine;

namespace Outflow;

public class FullBatcher : IDisposable
{
    private readonly ConcurrentQueue<FullBatch> batcherTasks = new();
    private readonly ConcurrentQueue<SyncMessage> deltas = new();
    private readonly AutoResetEvent reset = new(false);
    private readonly Thread processThread;
    public bool IsProcessing {get; private set; }
    public bool Disposed { get; private set; }
    private readonly Session session;



    public FullBatcher(Session curSession)
    {
        processThread = new(Process);
        processThread.Start();
        session = curSession;
    }



    public void QueueEncode(FullBatch batch)
    {
        batcherTasks.Enqueue(batch);
        reset.Set();
    }



    public bool TryQueueMessage(SyncMessage delta)
    {
        if (IsProcessing)
            deltas.Enqueue(delta);
        else
            return false;
        
        return true;

    }



    public void Process()
    {
        while (true && !Disposed)
        {
            reset.WaitOne();
            IsProcessing = true;
            while (batcherTasks.TryDequeue(out FullBatch batch) && !Disposed)
            {
                session.NetworkManager.TransmitData(batch.Encode());
                batch.Dispose();
            }
            IsProcessing = false;
        }
    }



    public int FlushQueued(DeltaBatch lastDelta)
    {
        int flushed = 0;
        if (deltas.Count == 0)
            return flushed;
        
        while (deltas.TryDequeue(out SyncMessage queued))
        {
            if (queued is DeltaBatch dt)
            {
                DeltaBatch newDt = new(lastDelta.SenderStateVersion, lastDelta.SenderSyncTick, queued.Sender, dt);
                newDt.SetSenderTime(lastDelta.SenderTime);
                queued.Targets.ForEach(newDt.Targets.Add);
                session.NetworkManager.TransmitData(newDt.Encode());
                queued.Dispose();
                newDt.Dispose();
            }
            else
            {
                session.NetworkManager.TransmitData(queued.Encode());
                queued.Dispose();
            }
            flushed++;
        }
        return flushed;
    }



    public void Dispose()
    {
        Disposed = true;
    }
}