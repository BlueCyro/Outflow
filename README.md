# 🌊 Outflow: Keep those packets coming!

Outflow is a mod for [Resonite](https://resonite.com) via [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) that aims to mitigate or outright eliminate interruptions in voice and player movements when new users join particularly large sessions.

## Usage
Simply drop the mod into your `rml_mods` folder within the Resonite directory and you're done! This mod should work on both Headless clients and normal clients. Outflow is only required by the host of the world to work for everyone in the session.

# What causes lag when a user joins?

* The encoding loop gets stopped up by what's called a 'FullBatch'
    - A FullBatch is a message that contains the entirety of the session, these are quite large
    - These batches take quite a lot of time to encode (Up to multiple seconds in some cases!)
    - This stops any messages from being sent until a FullBatch is completely encoded and transmitted
    - In sum, everyone in the session stops moving/talking, and is quite disruptive


# How do we solve this?

* Move the FullBatch to process outside of the encoding loop
    - While the FullBatch is encoding, save all other messages (aside from stream messages)
    - When the FullBatch is done, send it and then send all other packets behind it
    - This ensures that streams (aka the stuff that carries your voice and movement data) don't get interrupted


# How does it work?

I'm glad you didn't ask! Here's a massive MermaidJS diagram for your viewing pleasure:

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


# Why isn't this in the game by default?

I get this question a lot about some of my mods, so I want to go a little bit into detail about why it seems like modders can do all of these amazing things when the YDMS Team can't.

**Mods do not care.**

What I mean by this, is that they don't care about the philosophy behind why something is built. Imagine you have an office building and you want to run a long network cable to the other side. If you own the building, you can contract a bunch of workers to carefully route the wires in such a way that is out of the way, and is easily accessible if they need to run more networking to other areas of the building. As a mod-maker, you don't own this building. You can't hire a bunch of workers so what you end up doing is either routing a bunch of cables all over the place where people can trip. You use far more cabling than you need, drill holes in the walls to get it to the next room, or completely tear them down to get the cable where you want it to go. Imagine taking a bulldozer to your bedroom to connect to a router located in your kitchen.

All this is to say that just because a mod can do it, doesn't mean it's the way it should be done. Modding isn't bad (it's actually really good for a platform's longevity it turns out), but it's not the way a game should be developed officially. The YDMS Team work very hard at giving us a stable foundation to base our wacky inventions off of.

You can either use a bulldozer, or recognize why a wall is there in the first place.