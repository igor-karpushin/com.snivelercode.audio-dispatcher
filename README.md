# Audio Dispatcher for Unity DOTS

**Version:** 1.0.0  
**Unity Version:** 2022.3+ (LTS recommended)  
**Dependencies:** `com.unity.entities`, `com.unity.burst`, `com.unity.collections`

## Overview

**Audio Dispatcher** is a high-performance, thread-safe bridge between Unity's Data-Oriented Technology Stack (DOTS/ECS) and the classic `AudioSource` system.

In pure ECS, triggering audio is difficult because `AudioSource` (a managed class) cannot be accessed directly from Burst-compiled jobs. This package solves that problem using a lock-free `NativeQueue` architecture and a highly optimized object pool.

### Key Features

*   **⚡ Burst Compatible:** Trigger sound events directly from `IJobEntity` or `ISystem`.
*   **🚀 Zero GC Allocations:** Uses a pre-allocated Object Pool to play sounds. No `Instantiate` or `Destroy` at runtime.
*   **🎛️ Audio Mixer Support:** Full control over Output Groups (Master, SFX, Music, UI).
*   **🔊 3D Spatial Audio:** Configurable Spatial Blend (2D/3D), Min/Max Distance, and Rolloff curves.
*   **🛠️ ID Generation:** Auto-generates C# `const int` IDs for your audio clips to prevent string-based errors.

---

## Installation

### Method 1: Via Unity Asset Store (Recommended)
1.  Open your project in Unity.
2.  Go to **Window > Package Manager**.
3.  Select **My Assets** from the dropdown menu in the top-left corner.
4.  Search for **Audio Dispatcher**.
5.  Click **Download**, then click **Import**.
6.  Ensure all files are selected and click **Import** again.
    *   *The package will be installed into `Assets/SnivelerCode/AudioDispatcher`.*
    *   *Unity will automatically detect the `package.json` and install dependencies (Burst, Collections, etc.).*

### Method 2: Manual Installation (Advanced)
If you have the raw package files (e.g., from a repository backup):
1.  Open Unity Package Manager.
2.  Click **+ > Add package from disk...**
3.  Select the `package.json` file inside the `com.snivelercode.audio-dispatcher` folder.

---

## Quick Start

1.  **Create Configuration:**
    Right-click in Project view -> `Create > SnivelerCode > Audio Configuration`.
2.  **Add Sounds:**
    Add your `AudioClips` to the list. Assign names, volumes, and Mixer Groups.
3.  **Generate IDs:**
    Click the green **"Generate C# Constants"** button in the inspector. This creates `AudioID.cs`.
4.  **Scene Setup:**
    Create a GameObject in your sub-scene. Add the `AudioSettingsAuthoring` component and assign your Configuration asset.
5.  **Play Sound (Code):**

```csharp
// Inside a Burst-compiled System
partial struct MyGameSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // 1. Get the Audio Singleton
        var audioSys = SystemAPI.GetSingletonRW<NativeAudioSystem.Singleton>();
        
        // 2. Queue an event
        audioSys.ValueRW.Writer.Enqueue(new NativeAudioSystem.AudioEvent
        {
            SoundId = AudioID.EXPLOSION, // Auto-generated ID
            Position = new float3(0, 0, 0),
            Volume = 1.0f,
            Pitch = 1.0f
        });
    }
}