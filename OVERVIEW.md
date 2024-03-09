Outflow overview:

# What causes lag when a user joins?

* The encoding loop gets stopped up by the outgoing full batch.
    - Full batch is enqueued and then JoinStartDelta control is enqueued
        - These may not always be directly after eachother, other packets may sneak in between
    - Full batch can be quite large, which halts encoding loop until it's sent. All queued messages then flush out.
    - This causes everyone in the session to stop moving and is quite disruptive

# How can we solve this?

* Move the full batch to process outside of the encoding loop

* How to ensure order of messages reliably?
    - Detect full batch, queue all subsequent delta batches and JoinStartDeltas until processing is done
        - Deltas signaled to queue if full batch processing jobs exist

```mermaid
---
title: Packet encoding
---
flowchart LR

    New((New Reliable Packet)) --> IsFull{Is full batch?}
    IsFull -->|Yes| FullQueue[(FullBatch Queue)] -->|Send new batch| Wait --> CheckTasks{IsProcessing == true?}
    linkStyle 1,2,3 stroke:#59EB5C
    
    IsFull -->|No| CheckTasks
    linkStyle 4 stroke:#FF7676

    FullQueue -.-|Take next| SendFull(Encode FullBatch) --> IsEmpty{Are all FullBatches done?}
    linkStyle 6 stroke:#F8F770

    subgraph Processing FullBatches
        IsEmpty -->|No| SendFull
        IsEmpty --x|Yes| Wait[Wait for FullBatches] --o StopProcessingFulls[IsProcessing = false]
        Wait -.->|New batch gotten| ProcessingFulls[IsProcessing = true] --> SendFull
        linkStyle 10 stroke:#59EB5C
        linkStyle 11 stroke:#F8F770
    end
    
    CheckTasks ---|Yes| StoreDelta[Store Reliable Message] --x Queue[(Reliable Message Queue)]
    CheckTasks -->|No| DeltasQueued{Messages queued?}
    DeltasQueued -->|Yes| Process[Process all queued] --> Next[Next Packet]
    DeltasQueued -->|No| Next


    Process -.-|Take all| Queue
```