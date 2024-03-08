Outflow overview:

# What causes lag when a user joins?

* The encoding loop gets stopped up by the outgoing full batch.
    - Full batch is enqueued and then JoinStartDelta control is enqueued
        - These may not always be directly after eachother, other packets may sneak in between
    - Full batch can be quite large, which halts encoding loop until it's sent. All queued messages then flush out.
    - This causes everyone in the session to stop moving and is quite disruptive

# How can we solve this?

* Move the full batch to process outside of the encoding loop

1) How to ensure order of messages reliably?
    - Detect full batch, queue all subsequent delta batches and JoinStartDeltas until processing is done
        - Deltas signaled to queue if full batch processing jobs exist

```mermaid
---
title: Packet encoding
---
flowchart LR
    New(New Packet) --> IsFull{Is full batch?}
    IsFull ---|Yes| FullQueue[(FullBatch Queue)] --> CheckTasks{ProcessingFulls == true?}
    IsFull -->|No| CheckTasks
    
    CheckTasks ---|Yes| StoreDelta[Store Delta] --x Queue[(Delta Queue)]
    CheckTasks -->|No| DeltasQueued{Deltas queued?}
    DeltasQueued -->|Yes| Process[Process all queued]
    DeltasQueued -->|No| New


    Process -.-|Take all| Queue
    Process --> New
```

```mermaid
---
title: Processing FullBatches
---
flowchart LR
FullQueue[(FullBatch Queue)]


FullQueue -.-|Take next| SendFull(Encode FullBatch) --> Empty{Are all FullBatches done?}
Empty -->|No| SendFull
Empty --x|Yes| Wait[Wait for FullBatches] --o StopProcessingFulls[ProcessingFulls = false]
Wait -.->|New batch gotten| ProcessingFulls[ProcessingFulls = true] --> SendFull
```