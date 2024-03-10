# What causes lag when a user joins?

* The encoding loop gets stopped up by the outgoing full batch.
    - Full batch is enqueued and then JoinStartDelta control is enqueued
    - Full batch can be quite large, which halts encoding loop until it's sent. All queued messages then flush out.
    - This causes everyone in the session to stop moving and is quite disruptive

# How can we solve this?

* Move the full batch to process outside of the encoding loop
* How to ensure order of messages reliably?
    - Detect full batch, queue all subsequent reliable messages until encoding is done
    - Reliable messages

```mermaid
---
title: Packet encoding
---
%%{ init: { 'flowchart': { 'curve': 'monotoneX' } } }%%
graph LR
    subgraph Queues
        QueuedReliable[(Queued Reliable\npackets)]
        QueuedFulls[(Queued FullBatch\npackets)]
    end

    subgraph FullBatch Processing Loop
        StartProcessing[Start Processing\nIsProcessing = true]
        ProcessFull[Process FullBatch]
        FullQueueEmpty{FullBatch queue\nempty?}
        Wait[Wait for new FullBatches]

        StartProcessing --> ProcessFull
        ProcessFull -.-|Takes one| QueuedFulls
        linkStyle 1 stroke:#E69E50,stroke-width:5px
        ProcessFull --> FullQueueEmpty

        FullQueueEmpty -->|Yes| Wait
        %%linkStyle 3 stroke:#59EB5C
        FullQueueEmpty-->|No| StartProcessing
        %%linkStyle 4 stroke:#FF7676
    end

    

    New((New Reliable Packet))
    MessagesQueued{Are messages\nqueued to send?}
    Flush[Flush queue]
    ShouldQueue{Should packet queue?}
    SetProcessing(IsProcessing = true)

    QueueFull[Queue FullBatch]
    QueueReliable[Queue Reliable]
    Next[Next packet]


    New --> MessagesQueued
    MessagesQueued -->|Yes| Flush --> ShouldQueue
    linkStyle 6,7 stroke:#59EB5C

    MessagesQueued -->|No| ShouldQueue
    linkStyle 8 stroke:#FF7676

    Conditions>Packets queue when:\nIsProcessing is true\nAND\nThe packet is not a stream\nOR\nThe packet is a FullBatch\n] -.- ShouldQueue

    Flush -.-|Takes all| QueuedReliable
    linkStyle 10 stroke:#59EB5C

    ShouldQueue -->|Is FullBatch| QueueFull
    linkStyle 11 stroke:#61D1FA

    ShouldQueue -->|FullBatches are Processing\n&\nIs not StreamMessage| QueueReliable
    linkStyle 12 stroke:#F8F770
    
    QueueReliable --> SetProcessing --> Next
    linkStyle 13,14 stroke:#F8F770

    ShouldQueue -->|Is StreamMessage| Next
    linkStyle 15 stroke:#BA64F2

    QueueFull --> Next
    linkStyle 16 stroke:#61D1FA

    QueueFull -.-x|Inserts| QueuedFulls
    linkStyle 17 stroke:#61D1FA

    QueueReliable -.-x|Inserts| QueuedReliable
    linkStyle 18 stroke:#F8F770
```