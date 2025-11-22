# Lone EFT DMA Radar - Complete Code Walkthrough

**Author**: Claude Code
**Date**: 2025-11-20
**Purpose**: Educational walkthrough of how the EFT DMA radar works internally

---

## ⚠️ DISCLAIMER

This document provides a technical analysis of software designed to provide an unfair advantage in Escape from Tarkov by reading game memory externally. This type of software:

- **Violates** the Escape from Tarkov Terms of Service
- Provides **unfair competitive advantage** ("cheating")
- Could result in **permanent account bans**
- May have **legal implications** depending on jurisdiction

**This analysis is provided for educational/security research purposes only.**

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Hardware Setup](#2-hardware-setup)
3. [Application Entry Point (App.xaml.cs)](#3-application-entry-point-appxamlcs)
4. [DMA Hardware Initialization (MemoryInterface.cs & MemDMA.cs)](#4-dma-hardware-initialization)
5. [Finding Game Objects (Unity Integration)](#5-finding-game-objects-unity-integration)

---

## 1. Project Overview

### What is Lone-EFT-DMA-Radar?

This is an **external radar tool** for Escape from Tarkov that runs on a **second computer**. It uses a special **DMA (Direct Memory Access) hardware card** plugged into your gaming PC's motherboard. This card lets the second computer read the game's memory over PCIe, showing:

- **Real-time player positions** on a 2D map (where enemies are, where they're looking)
- **Loot locations** (valuable items, quest items)
- **Extraction points** (exits and their status)
- **Grenades and explosives**
- **Optional web interface** (view radar on your phone/tablet)

### Why is it hard to detect?

Unlike traditional cheats that inject code into the game, this:
- Runs on a **separate physical computer**
- Uses **hardware-level memory reading** (PCIe DMA card)
- Never modifies game memory
- No software installed on gaming PC

### Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Framework** | .NET 10.0 (C#) | Main application framework |
| **UI Framework** | WPF (Windows Presentation Foundation) | Desktop UI |
| **Rendering** | SkiaSharp | 2D graphics rendering for radar |
| **DMA Library** | VmmSharpEx (MemProcFS wrapper) | PCIe DMA memory access |
| **Database** | LiteDB | Local data caching |
| **Web Framework** | ASP.NET Core + SignalR | Web radar real-time communication |
| **Updates** | Velopack | Auto-update system |

---

## 2. Hardware Setup

### Hardware Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        GAMING PC                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │         EscapeFromTarkov.exe (Game Process)          │  │
│  │                                                       │  │
│  │  - Player positions stored in RAM                    │  │
│  │  - Loot data stored in RAM                           │  │
│  │  - Game state stored in RAM                          │  │
│  └──────────────────────┬───────────────────────────────┘  │
│                         ↓                                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │              System RAM (Memory)                     │  │
│  │      Address: 0x00007FF8A2B40000 ← Player 1 pos     │  │
│  │      Address: 0x00007FF8A2B40100 ← Player 2 pos     │  │
│  │      Address: 0x00007FF8A2B40200 ← Loot item        │  │
│  └──────────────────────┬───────────────────────────────┘  │
│                         ↓                                   │
│                    ┌─────────┐                              │
│                    │ CPU/RAM │                              │
│                    │  Bus    │                              │
│                    └────┬────┘                              │
│                         ↓                                   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │          PCIe Slot (DMA Card Installed)             │   │
│  │  ┌───────────────────────────────────────────────┐  │   │
│  │  │  FPGA DMA Card (Captain 75T / Screamer)       │  │   │
│  │  │  - Pretends to be a sound card or USB device │  │   │
│  │  │  - Reads memory through PCIe bus              │  │   │
│  │  └─────────────────────┬─────────────────────────┘  │   │
│  └────────────────────────┼─────────────────────────────┘   │
│                           │                                 │
│                           │ USB Cable                       │
└───────────────────────────┼─────────────────────────────────┘
                            ↓
┌───────────────────────────────────────────────────────────┐
│                      RADAR PC                             │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  Lone-EFT-DMA-Radar.exe                             │ │
│  │                                                      │ │
│  │  1. Sends read commands to DMA card via USB         │ │
│  │  2. DMA card reads Gaming PC's RAM                  │ │
│  │  3. Parses player/loot data from memory             │ │
│  │  4. Displays on 2D radar map                        │ │
│  └─────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
```

### Key Hardware Components

| Component | Purpose |
|-----------|---------|
| **FPGA DMA Card** | Hardware device that reads memory over PCIe |
| **USB Cable** | Connects DMA card to Radar PC |
| **Gaming PC** | Runs the game (victim) |
| **Radar PC** | Runs this software (reads memory) |

### The DMA Card

The DMA card is an **FPGA (Field-Programmable Gate Array)** device that:
- Is installed in a PCIe slot on the Gaming PC
- Has custom firmware (from [pcileech-fpga](https://github.com/ufrisk/pcileech-fpga))
- **Pretends to be a legitimate device** (sound card, network card, etc.) to avoid detection
- Connects via USB to the Radar PC
- Can read ANY memory from the Gaming PC without the OS knowing

**This is the same hardware referenced in CLAUDE.md** - the Captain 75T card being customized to look like a CMI8738 audio card!

---

## 3. Application Entry Point (App.xaml.cs)

**File**: [App.xaml.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\App.xaml.cs)

This file is where **everything starts** when you run `Lone-EFT-DMA-Radar.exe`.

### Key Sections

#### Lines 29-46: Global Imports

```csharp
global using SDK;
global using SkiaSharp;
global using System.Buffers;
// ... etc
```

**What this does:**
- These are **global usings** (C# 10 feature)
- Makes these namespaces available in **every file** without needing to write `using SDK;` at the top of each file
- `SDK` = The game offsets (hardcoded memory addresses)
- `SkiaSharp` = Graphics library for drawing the radar

---

#### Lines 71-72: Singleton Mutex

```csharp
private const string MUTEX_ID = "0f908ff7-e614-6a93-60a3-cee36c9cea91";
private static readonly Mutex _mutex;
```

**What this does:**
- **Mutex** = "Mutual Exclusion" lock
- Ensures only **one instance** of the radar can run at a time
- If you try to run it twice, line 105-106 will throw an error:
  ```csharp
  if (!singleton)
      throw new InvalidOperationException("The Application Is Already Running!");
  ```

---

#### Lines 79-80: Configuration Path

```csharp
public static DirectoryInfo ConfigPath { get; } =
    new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lone-EFT-DMA"));
```

**What this does:**
- Stores config at: `C:\Users\YourName\AppData\Roaming\Lone-EFT-DMA\`
- This is where `Config-EFT.json` lives (loot filters, UI settings, watchlist, etc.)

---

#### Lines 99-119: Static Constructor (Runs FIRST)

```csharp
static App()
{
    VelopackApp.Build().Run();              // Line 103: Check for updates
    _mutex = new Mutex(true, MUTEX_ID, out bool singleton);  // Line 104: Singleton check
    if (!singleton)                         // Line 105-106: Exit if already running
        throw new InvalidOperationException("The Application Is Already Running!");

    Config = EftDmaConfig.Load();           // Line 109: Load config from JSON
    ServiceProvider = BuildServiceProvider(); // Line 110: Setup dependency injection
    HttpClientFactory = ServiceProvider.GetRequiredService<IHttpClientFactory>(); // Line 111
    SetHighPerformanceMode();               // Line 112: Boost process priority
}
```

**Execution order:**
1. **Update check** (Velopack checks GitHub for new releases)
2. **Singleton check** (prevent running twice)
3. **Load config** (read `Config-EFT.json`)
4. **Setup services** (HTTP clients, APIs)
5. **Performance mode** (boost process priority to "High", set 5ms timer resolution)

---

#### Lines 121-136: OnStartup (Runs SECOND)

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    using var loading = new LoadingWindow();           // Line 126: Show loading screen
    await ConfigureProgramAsync(loadingWindow: loading); // Line 127: Initialize everything
    MainWindow = new MainWindow();                     // Line 128: Create main window
    MainWindow.Show();                                 // Line 129: Show radar UI
}
```

**What happens:**
1. Shows a loading screen
2. Calls `ConfigureProgramAsync()` (this is the important part!)
3. Opens the main radar window

---

#### Lines 155-176: ConfigureProgramAsync (Initializes EVERYTHING)

```csharp
private async Task ConfigureProgramAsync(LoadingWindow loadingWindow)
{
    _ = Task.Run(CheckForUpdatesAsync);              // Line 158: Background update check
    var tarkovDataManager = TarkovDataManager.ModuleInitAsync();  // Line 159: Load item prices, boss names
    var eftMapManager = EftMapManager.ModuleInitAsync();          // Line 160: Load map SVG files
    var memoryInterface = MemoryInterface.ModuleInitAsync();      // Line 161: ⚠️ INITIALIZE DMA HARDWARE
    // ...
    await Task.WhenAll(tarkovDataManager, eftMapManager, memoryInterface, misc); // Line 173: Wait for all
}
```

**This runs 4 tasks in parallel:**

| Task | What It Does | File |
|------|-------------|------|
| **TarkovDataManager** | Loads boss names, loot prices from API | TarkovDataManager.cs |
| **EftMapManager** | Loads map SVG files (Customs, Factory, etc.) | EftMapManager.cs |
| **MemoryInterface** | **🚨 Initializes DMA card (connects to hardware)** | MemoryInterface.cs |
| **misc** | Dark mode setup, caching | - |

---

#### Lines 200-212: SetHighPerformanceMode()

```csharp
private static void SetHighPerformanceMode()
{
    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; // Line 203: Boost priority
    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS |
                            EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                            EXECUTION_STATE.ES_DISPLAY_REQUIRED);  // Line 204-205: Prevent sleep

    var highPerformanceGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    PowerSetActiveScheme(IntPtr.Zero, ref highPerformanceGuid);  // Line 207: Windows "High Performance" power plan

    const uint timerResolutionMs = 5;
    TimeBeginPeriod(timerResolutionMs);  // Line 210: Set timer resolution to 5ms (for precise timing)
}
```

**Why this matters:**
- **High priority** = OS gives more CPU time to the radar
- **Prevent sleep** = Computer won't go to sleep while radar is running
- **5ms timer resolution** = More precise `Thread.Sleep()` calls (important for 125Hz player updates)

---

### Startup Flow Summary

```
User double-clicks Lone-EFT-DMA-Radar.exe
  ↓
[Static Constructor] App()
  ├─> Check for updates (Velopack/GitHub)
  ├─> Prevent duplicate instances (Mutex)
  ├─> Load Config-EFT.json
  ├─> Setup dependency injection
  └─> Boost process priority to "High"
  ↓
[OnStartup] Show loading screen
  ↓
[ConfigureProgramAsync] Initialize in parallel:
  ├─> TarkovDataManager (load boss names, loot data)
  ├─> EftMapManager (load map SVG files)
  ├─> MemoryInterface ⚠️ CONNECT TO DMA HARDWARE
  └─> Misc (dark mode, caching)
  ↓
[MainWindow] Show radar UI
  ↓
User sees the radar interface
```

---

## 4. DMA Hardware Initialization

### MemoryInterface.cs - Singleton Wrapper

**File**: [MemoryInterface.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\DMA\MemoryInterface.cs)

This is a simple **singleton wrapper** around the actual DMA engine:

```csharp
internal static class MemoryInterface
{
    public static async Task ModuleInitAsync()
    {
        await Task.Run(() =>
        {
            Memory ??= new MemDMA();  // Create DMA engine if not already created
            Debug.WriteLine("DMA Initialized!");
        });
    }

    /// <summary>
    /// Singleton Instance for use in this assembly.
    /// </summary>
    public static MemDMA Memory { get; private set; }
}
```

**Global usage:**
- Because of `global using static LoneEftDmaRadar.DMA.MemoryInterface;` (line 29)
- You can call `Memory.ReadValue()` from anywhere in the code
- It's a **singleton** - only one instance exists

---

### MemDMA.cs - The DMA Engine

**File**: [MemDMA.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\DMA\MemDMA.cs)

This 682-line file is where **all memory reading happens**.

---

#### Lines 84-163: Constructor - Hardware Initialization

```csharp
internal MemDMA()
{
    FpgaAlgo fpgaAlgo = App.Config.DMA.FpgaAlgo;  // Line 86: Get FPGA algorithm from config
    bool useMemMap = App.Config.DMA.MemMapEnabled; // Line 87: Memory map caching
```

**What's happening:**
- Reads your DMA settings from `Config-EFT.json`
- `FpgaAlgo` = The algorithm your DMA card uses (connects to `CLAUDE.md`!)
  - `Auto` (-1) = Let MemProcFS decide
  - `AsyncNormal` (0) = Default
  - `AsyncTiny` (1) = **"Tiny PCIe" algorithm** (reduces detection)
  - `OldNormal` (2) / `OldTiny` (3) = Legacy modes

---

#### Lines 94-99: DMA Device Arguments

```csharp
string[] initArgs = new[] {
    "-norefresh",           // Don't auto-refresh process list
    "-device",
    fpgaAlgo is FpgaAlgo.Auto ?
        "fpga" : $"fpga://algo={(int)fpgaAlgo}",  // Line 97-98: Select FPGA algorithm
    "-waitinitialize"       // Wait for DMA card to respond
};
```

**This connects to your FPGA card!**
- `-device fpga` = Connect to any FPGA DMA device
- `-device fpga://algo=1` = Use Tiny algorithm (stealth mode)
- These arguments are passed to `vmm.dll` (MemProcFS library)

---

#### Lines 108-125: Initialize VMM (MemProcFS)

```csharp
_vmm = new Vmm(args: initArgs)
{
    EnableMemoryWriting = false  // Line 110: READ-ONLY (never writes to game memory)
};
```

**Critical safety feature:**
- `EnableMemoryWriting = false` means this radar **NEVER writes** to game memory
- It's purely passive (read-only)
- This is important because memory writes are easier to detect

---

#### Lines 103-121: Memory Map Caching

```csharp
if (useMemMap)
{
    if (!File.Exists(_mmap))  // Line 105: Check if mmap.txt exists
    {
        _vmm.GetMemoryMap(
            applyMap: true,
            outputFile: _mmap);  // Line 112-114: Generate memory map file
    }
    else
    {
        var mapArgs = new[] { "-memmap", _mmap };
        initArgs = initArgs.Concat(mapArgs).ToArray();  // Line 118-119: Use cached map
    }
}
```

**What's a memory map?**
- **Memory map** = A file (`mmap.txt`) that stores which memory addresses are valid
- First run: Scans entire RAM to find readable pages (slow)
- Future runs: Reads from cached file (fast!)
- **Why this matters:** Reduces PCIe traffic = harder to detect

Location: `C:\Users\YourName\AppData\Roaming\Lone-EFT-DMA\mmap.txt`

---

#### Lines 126-127: Auto-Refresh Timers

```csharp
_vmm.RegisterAutoRefresh(RefreshOption.MemoryPartial, TimeSpan.FromMilliseconds(300)); // Line 126
_vmm.RegisterAutoRefresh(RefreshOption.TlbPartial, TimeSpan.FromSeconds(2));          // Line 127
```

**What this does:**
- Every **300ms**: Refresh memory cache (finds new allocated memory)
- Every **2 seconds**: Refresh TLB (Translation Lookaside Buffer - virtual→physical address mapping)
- This keeps the DMA working even as the game allocates/frees memory

---

#### Lines 145-148: Start Memory Thread

```csharp
new Thread(MemoryPrimaryWorker)
{
    IsBackground = true  // Line 147: Background thread (won't keep app alive if main thread exits)
}.Start();
```

**This spawns a dedicated thread** that runs the main loop.

---

#### Lines 168-192: MemoryPrimaryWorker - The Main Loop

```csharp
private void MemoryPrimaryWorker()
{
    while (true)  // Line 173: INFINITE LOOP
    {
        try
        {
            while (true) // Line 177: Main Loop
            {
                RunStartupLoop();    // Line 179: Wait for EscapeFromTarkov.exe
                OnProcessStarted();  // Line 180: Fire event
                RunGameLoop();       // Line 181: Read memory while in raid
                OnProcessStopped();  // Line 182: Fire event
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FATAL ERROR on Memory Thread: {ex}");
            OnProcessStopped();
            Thread.Sleep(1000);  // Line 189: Wait 1 second, then retry
        }
    }
}
```

**This is an infinite state machine:**

```
┌──────────────────────────────────────┐
│  Wait for EscapeFromTarkov.exe       │ ← RunStartupLoop()
└────────────┬─────────────────────────┘
             ↓
┌──────────────────────────────────────┐
│  Game found! Fire ProcessStarted     │ ← OnProcessStarted()
└────────────┬─────────────────────────┘
             ↓
┌──────────────────────────────────────┐
│  Read memory while in raid           │ ← RunGameLoop()
│  (Players, loot, exits, etc.)        │
└────────────┬─────────────────────────┘
             ↓
┌──────────────────────────────────────┐
│  Game closed! Fire ProcessStopped    │ ← OnProcessStopped()
└────────────┬─────────────────────────┘
             ↓
             Back to top (wait for game again)
```

---

#### Lines 202-226: RunStartupLoop - Wait for Game

```csharp
private void RunStartupLoop()
{
    while (true) // Line 205: Keep trying until game is found
    {
        try
        {
            _vmm.ForceFullRefresh();  // Line 209: Refresh process list
            ResourceJanitor.Run();    // Line 210: Clean up memory
            LoadProcess();            // Line 211: ⚠️ Find EscapeFromTarkov.exe PID
            LoadModules();            // Line 212: ⚠️ Get UnityPlayer.dll base address
            this.Starting = true;
            OnProcessStarting();
            this.Ready = true;
            break;  // Line 217: Success! Exit startup loop
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Process Startup [FAIL]: {ex}");
            Thread.Sleep(1000);  // Line 223: Wait 1 second, retry
        }
    }
}
```

**What's happening:**
1. **Refresh process list** - Ask DMA card for all running processes
2. **Find game** - Look for `EscapeFromTarkov.exe` in process list
3. **Get module base** - Find where `UnityPlayer.dll` is loaded in memory
4. If any step fails → wait 1 second → retry

---

#### Lines 296-312: LoadProcess & LoadModules

```csharp
private void LoadProcess()
{
    if (!_vmm.PidGetFromName(GAME_PROCESS_NAME, out uint pid))  // Line 299
        throw new InvalidOperationException($"Unable to find '{GAME_PROCESS_NAME}'");
    _pid = pid;  // Line 301: Store process ID (e.g., 12345)
}

private void LoadModules()
{
    var unityBase = _vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");  // Line 309
    unityBase.ThrowIfInvalidVirtualAddress(nameof(unityBase));
    UnityBase = unityBase;  // Line 311: Store UnityPlayer.dll base address (e.g., 0x00007FF8A0000000)
}
```

**Why UnityPlayer.dll?**
- EFT uses **Unity Engine**
- `UnityPlayer.dll` contains Unity's core functionality
- All game objects, transforms, etc. are managed by Unity
- We need this base address to find game objects later

**Example:**
```
UnityPlayer.dll loaded at: 0x00007FF8A0000000
GameObjectManager offset:  +0x1234ABC
Full address:              = 0x00007FF8A1234ABC
```

---

#### Lines 232-271: RunGameLoop - The Actual Radar Loop

```csharp
private void RunGameLoop()
{
    while (true)
    {
        try
        {
            using (var game = Game = LocalGameWorld.CreateGameInstance())  // Line 238: Create game instance
            {
                OnRaidStarted();       // Line 240: Fire RaidStarted event
                game.Start();          // Line 241: Start worker threads
                while (game.InRaid)    // Line 242: Loop while in raid
                {
                    if (_restartRadar)
                    {
                        _restartRadar = false;
                        break;  // Line 248: User requested restart
                    }
                    game.Refresh();    // Line 250: ⚠️ UPDATE ALL PLAYER POSITIONS, LOOT, ETC.
                    Thread.Sleep(133); // Line 251: ~7.5 Hz (133ms = 7.5 updates/sec)
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            break;  // Line 258: Game closed
        }
        finally
        {
            OnRaidStopped();  // Line 267: Fire RaidStopped event
        }
    }
}
```

**This is the main radar loop:**
- **Line 238:** Create `LocalGameWorld` (in-raid game instance)
- **Line 241:** `game.Start()` spawns 3 worker threads:
  - Thread 1: 125Hz (8ms) - Player positions
  - Thread 2: 20Hz (50ms) - Loot, exits
  - Thread 3: 33Hz (30ms) - Grenades
- **Line 250:** `game.Refresh()` updates the main loop (checks if still in raid, etc.)
- **Line 251:** `Thread.Sleep(133)` = ~7.5 Hz main loop

---

### Memory Read Methods

#### ReadValue&lt;T&gt; - Read a single value

```csharp
public T ReadValue<T>(ulong addr, bool useCache = true)
{
    var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
    if (!_vmm.MemReadValue<T>(_pid, addr, out var result, flags))
        throw new VmmException("Memory Read Failed!");
    return result;
}
```

**Example usage:**
```csharp
Vector3 playerPos = Memory.ReadValue<Vector3>(0x00007FF8A2B40000);
// Reads 12 bytes (3 floats) from address 0x00007FF8A2B40000
// Returns: Vector3 { X = 125.5f, Y = 10.2f, Z = -50.3f }
```

---

#### ReadPtr - Read a pointer (8 bytes, return the address it points to)

```csharp
public ulong ReadPtr(ulong addr, bool useCache = true)
{
    var pointer = ReadValue<VmmPointer>(addr, useCache);
    pointer.ThrowIfInvalid();  // Check if pointer is valid
    return pointer;  // Return the address it points to
}
```

**Example:**
```csharp
ulong playerBase = Memory.ReadPtr(0x00007FF8A2000000);
// Reads 8 bytes from 0x00007FF8A2000000
// Returns: 0x00007FF8A2B40000 (address of player object)
```

---

#### ReadPtrChain - Follow multiple pointers

```csharp
public ulong ReadPtrChain(ulong addr, bool useCache, params Span<uint> offsets)
{
    ulong pointer = addr;
    foreach (var offset in offsets)
    {
        pointer = ReadPtr(checked(pointer + offset), useCache);
    }
    return pointer;
}
```

**Example:**
```csharp
// Follow pointer chain: Base → +0x30 → +0x18 → +0x28
ulong finalAddress = Memory.ReadPtrChain(
    0x00007FF8A0000000,   // Start address
    true,                 // Use cache
    0x30, 0x18, 0x28      // Offsets
);
```

**Visual representation:**
```
Address 0x00007FF8A0000000: [Pointer to 0xAAAAAAAA]
Address 0xAAAAAAAA + 0x30:  [Pointer to 0xBBBBBBBB]
Address 0xBBBBBBBB + 0x18:  [Pointer to 0xCCCCCCCC]
Address 0xCCCCCCCC + 0x28:  [Pointer to 0xDDDDDDDD]
Return: 0xDDDDDDDD
```

---

#### ReadUtf8String / ReadUnicodeString - Read strings

```csharp
public string ReadUtf8String(ulong addr, int cb, bool useCache = true)
{
    return _vmm.MemReadString(_pid, addr, cb, Encoding.UTF8, flags);
}

public string ReadUnicodeString(ulong addr, int cb = 128, bool useCache = true)
{
    return _vmm.MemReadString(_pid, addr + 0x14, cb, Encoding.Unicode, flags);
    // Note: +0x14 offset (Unity strings have 20-byte header)
}
```

**Example:**
```csharp
string playerName = Memory.ReadUnicodeString(0x00007FF8A3000000);
// Returns: "xXx_ProGamer_xXx"
```

---

#### CreateScatter - Batch reads (IMPORTANT!)

```csharp
public VmmScatter CreateScatter(VmmFlags flags = VmmFlags.NONE) =>
    _vmm.CreateScatter(_pid, flags);
```

**What's scatter/gather?**
- Instead of 100 individual DMA reads (slow)
- Queue up 100 reads, execute once (fast!)

**Example:**
```csharp
var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);

// Queue 10 reads
for (int i = 0; i < 10; i++)
{
    scatter.Prepare<Vector3>(playerAddress + (ulong)(i * 0x100), out var result);
}

// Execute ALL reads in one DMA transaction
scatter.Execute();

// Now all results are available
```

**Why this matters:**
- **Without scatter:** 10 reads = 10 DMA transactions = slow + detectable
- **With scatter:** 10 reads = 1 DMA transaction = fast + stealthy

---

#### FindSignature - Pattern Scanning

```csharp
public ulong FindSignature(string signature)
{
    if (!_vmm.Map_GetModuleFromName(_pid, "UnityPlayer.dll", out var info))
        throw new VmmException("Failed to get process information.");
    return _vmm.FindSignature(_pid, signature, info.vaBase, info.vaBase + info.cbImageSize);
}
```

**What's signature scanning?**
- Some offsets change with every game update
- Use byte patterns to find them dynamically

**Example:**
```csharp
// Find GameObjectManager pointer
ulong gomPtr = Memory.FindSignature("48 8B 05 ?? ?? ?? ?? 48 8B 48 ?? 48 85 C9");
// Scans UnityPlayer.dll for this byte pattern
// ?? = wildcard (any byte)
```

---

### Summary of MemDMA.cs

| Section | What It Does |
|---------|-------------|
| **Constructor** | Initialize DMA hardware (connect to FPGA card) |
| **MemoryPrimaryWorker** | Infinite loop: Wait for game → Read memory → Repeat |
| **RunStartupLoop** | Find `EscapeFromTarkov.exe` and get `UnityPlayer.dll` base |
| **RunGameLoop** | Create game instance, read memory while in raid |
| **Read Methods** | `ReadValue`, `ReadPtr`, `ReadPtrChain`, `ReadString` |
| **CreateScatter** | Batch reads for performance |
| **FindSignature** | Dynamic offset scanning |

---

## 5. Finding Game Objects (Unity Integration)

### How Unity Stores Game Objects

EFT uses **Unity Engine**, so the radar must understand Unity's internal structures to find players, loot, etc.

Unity stores all GameObjects in a **central registry** called the **GameObjectManager**.

---

### LocalGameWorld.cs - The Game Instance

**File**: [LocalGameWorld.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\LocalGameWorld.cs)

This class represents **one raid instance**. When you enter a raid, `LocalGameWorld.CreateGameInstance()` is called.

---

#### Lines 135-156: CreateGameInstance - Wait for Raid to Start

```csharp
public static LocalGameWorld CreateGameInstance()
{
    while (true)
    {
        ResourceJanitor.Run();
        Memory.ThrowIfProcessNotRunning();
        try
        {
            var instance = GetLocalGameWorld();
            Debug.WriteLine("Raid has started!");
            return instance;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR Instantiating Game Instance: {ex}");
        }
        finally
        {
            Thread.Sleep(1000);
        }
    }
}
```

**What's happening:**
- Infinite loop trying to find the game world
- If not in raid yet → exception → wait 1 second → retry
- Once in raid → `GetLocalGameWorld()` succeeds → return instance

---

#### Lines 167-180: GetLocalGameWorld - Find the Game World

```csharp
private static LocalGameWorld GetLocalGameWorld()
{
    try
    {
        /// Get LocalGameWorld
        var gom = GameObjectManager.Get(Memory.UnityBase);  // Line 172: Get GameObject Manager
        var localGameWorld = gom.GetGameWorld(out string map);  // Line 173: Find "GameWorld" object
        return new LocalGameWorld(localGameWorld, map);  // Line 174: Create instance
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("ERROR Getting LocalGameWorld", ex);
    }
}
```

**Two critical steps:**
1. **Get GameObjectManager** (Unity's central registry)
2. **Find "GameWorld" GameObject** (the in-raid world instance)

---

### GameObjectManager.cs - Unity's GameObject Registry

**File**: [GameObjectManager.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\Unity\Structures\GameObjectManager.cs)

#### Structure Definition

```csharp
[StructLayout(LayoutKind.Explicit)]
public readonly struct GameObjectManager
{
    [FieldOffset(0x20)]
    public readonly ulong LastActiveNode; // 0x20
    [FieldOffset(0x28)]
    public readonly ulong ActiveNodes; // 0x28
}
```

**What this struct represents:**
- **ActiveNodes** (offset 0x28): Pointer to **first node** in linked list
- **LastActiveNode** (offset 0x20): Pointer to **last node** in linked list

---

#### Lines 20-35: Get - Retrieve GameObjectManager

```csharp
public static GameObjectManager Get(ulong unityBase)
{
    try
    {
        var gomPtr = Memory.ReadPtr(unityBase + UnitySDK.ShuffledOffsets.GameObjectManager, false);
        return Memory.ReadValue<GameObjectManager>(gomPtr, false);
    }
    catch (Exception ex)
    {
        throw new Exception("ERROR Loading Game Object Manager", ex);
    }
}
```

**Visual representation:**
```
UnityPlayer.dll Base:    0x00007FF8A0000000
+ GameObjectManager Offset: +0x????????  (this offset changes with each Unity version)
= GameObjectManager Pointer: 0x00007FF8A1234ABC

Read GameObjectManager struct:
{
    LastActiveNode: 0x00007FF8B0FFFFFF,  // Last object in list
    ActiveNodes:    0x00007FF8B0000000   // First object in list
}
```

---

#### Lines 72-113: GetGameWorld - Walk Linked List to Find "GameWorld"

```csharp
public ulong GetGameWorld(out string map)
{
    map = default;
    var currentObject = Memory.ReadValue<LinkedListObject>(ActiveNodes);
    var lastObject = Memory.ReadValue<LinkedListObject>(LastActiveNode);

    if (currentObject.ThisObject != 0x0)
    {
        while (currentObject.ThisObject != 0x0 && currentObject.ThisObject != lastObject.ThisObject)
        {
            var objectNamePtr = Memory.ReadPtr(currentObject.ThisObject + UnitySDK.ShuffledOffsets.GameObject_NameOffset);
            var objectNameStr = Memory.ReadUtf8String(objectNamePtr, 64);

            if (objectNameStr.Equals("GameWorld", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var localGameWorld = Memory.ReadPtrChain(currentObject.ThisObject, false, UnitySDK.ShuffledOffsets.GameWorldChain);
                    /// Get Selected Map
                    var mapPtr = Memory.ReadValue<ulong>(localGameWorld + Offsets.GameWorld.Location, false);
                    if (mapPtr == 0x0) // Offline Mode
                    {
                        var localPlayer = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.MainPlayer, false);
                        mapPtr = Memory.ReadPtr(localPlayer + Offsets.Player.Location, false);
                    }

                    map = Memory.ReadUnicodeString(mapPtr, 128, false);
                    Debug.WriteLine("Detected Map " + map);
                    if (!StaticGameData.MapNames.ContainsKey(map)) // Also makes sure we're not in the hideout
                        throw new ArgumentException("Invalid Map ID!");
                    return localGameWorld;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Invalid GameWorld Instance: {ex}");
                }
            }

            currentObject = Memory.ReadValue<LinkedListObject>(currentObject.NextObjectLink); // Read next object
        }
    }
    return 0x0;
}
```

**What's happening:**
1. **Start at first node** (`ActiveNodes`)
2. **Read LinkedListObject** structure:
   ```csharp
   struct LinkedListObject
   {
       ulong PreviousObjectLink; // 0x0  - Pointer to previous node
       ulong NextObjectLink;     // 0x8  - Pointer to next node
       ulong ThisObject;         // 0x10 - Pointer to GameObject
   }
   ```
3. **Read GameObject name** from `ThisObject + GameObject_NameOffset`
4. **If name == "GameWorld"** → Found it!
5. **Follow pointer chain** to get `LocalGameWorld` C# class instance
6. **Read map ID** from `LocalGameWorld.Location`
7. **Return** `LocalGameWorld` address + map name

---

### Visual Representation: Linked List Traversal

```
ActiveNodes → LinkedListObject #1
              ├─ PreviousObjectLink: 0x0 (first node)
              ├─ NextObjectLink: 0x00007FF8B0000020
              └─ ThisObject: 0x00007FF8C0000000 → GameObject
                                                    └─ Name: "Camera"
                 ↓
              LinkedListObject #2
              ├─ PreviousObjectLink: 0x00007FF8B0000000
              ├─ NextObjectLink: 0x00007FF8B0000040
              └─ ThisObject: 0x00007FF8C0001000 → GameObject
                                                    └─ Name: "Main UI"
                 ↓
              LinkedListObject #3
              ├─ PreviousObjectLink: 0x00007FF8B0000020
              ├─ NextObjectLink: 0x00007FF8B0000060
              └─ ThisObject: 0x00007FF8C0002000 → GameObject
                                                    └─ Name: "GameWorld" ← FOUND IT!
                 ↓
              (continues...)
```

---

### Visual Representation: Pointer Chain to LocalGameWorld

Once we find the "GameWorld" GameObject, we need to get the **LocalGameWorld C# class instance**:

```csharp
var localGameWorld = Memory.ReadPtrChain(
    currentObject.ThisObject,
    false,
    UnitySDK.ShuffledOffsets.GameWorldChain  // e.g., [0x30, 0x18, 0x28]
);
```

**Pointer chain explanation:**
```
GameObject "GameWorld": 0x00007FF8C0002000
  +0x30 → Component Array: 0x00007FF8C0003000
    +0x18 → MonoBehaviour: 0x00007FF8C0004000
      +0x28 → LocalGameWorld Instance: 0x00007FF8D0000000 ← THIS IS WHAT WE WANT!
```

**Why pointer chains?**
- Unity GameObjects have **components** attached
- Components are stored in an array
- LocalGameWorld is a **MonoBehaviour component** (C# script)
- We need to follow pointers to find the actual C# class instance

---

### LocalGameWorld Constructor - Setup Game Instance

**Lines 80-120 in LocalGameWorld.cs:**

```csharp
private LocalGameWorld(ulong localGameWorld, string mapID)
{
    try
    {
        Base = localGameWorld;
        MapID = mapID;

        // Create 3 worker threads
        _t1 = new WorkerThread()
        {
            Name = "Realtime Worker",
            ThreadPriority = ThreadPriority.AboveNormal,
            SleepDuration = TimeSpan.FromMilliseconds(8),  // 125Hz
            SleepMode = WorkerThreadSleepMode.DynamicSleep
        };
        _t1.PerformWork += RealtimeWorker_PerformWork;

        _t2 = new WorkerThread()
        {
            Name = "Slow Worker",
            ThreadPriority = ThreadPriority.BelowNormal,
            SleepDuration = TimeSpan.FromMilliseconds(50)  // 20Hz
        };
        _t2.PerformWork += SlowWorker_PerformWork;

        _t3 = new WorkerThread()
        {
            Name = "Explosives Worker",
            SleepDuration = TimeSpan.FromMilliseconds(30),  // 33Hz
            SleepMode = WorkerThreadSleepMode.DynamicSleep
        };
        _t3.PerformWork += ExplosivesWorker_PerformWork;

        // Read player list
        var rgtPlayersAddr = Memory.ReadPtr(localGameWorld + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
        _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(_rgtPlayers.GetPlayerCount(), 1, nameof(_rgtPlayers));

        // Setup managers
        Loot = new(localGameWorld);
        _exfilManager = new(localGameWorld, _rgtPlayers.LocalPlayer.IsPmc);
        _explosivesManager = new(localGameWorld);
    }
    catch
    {
        Dispose();
        throw;
    }
}
```

**What's being created:**

| Component | What It Does | Update Rate |
|-----------|-------------|-------------|
| **RegisteredPlayers** | List of all players in raid | Managed by workers |
| **LootManager** | All loot items on map | 20Hz (T2) |
| **ExitManager** | All extraction points | 20Hz (T2) |
| **ExplosivesManager** | All grenades/mines | 33Hz (T3) |
| **Worker Thread T1** | Update player positions | 125Hz (8ms) |
| **Worker Thread T2** | Update loot/exits | 20Hz (50ms) |
| **Worker Thread T3** | Update grenades | 33Hz (30ms) |

---

### Reading the Player List

**Line 108 in LocalGameWorld.cs:**

```csharp
var rgtPlayersAddr = Memory.ReadPtr(
    localGameWorld + Offsets.ClientLocalGameWorld.RegisteredPlayers,
    false
);
```

**From SDK.cs (Line 16):**
```csharp
public const uint RegisteredPlayers = 0x168; // System.Collections.Generic.List<IPlayer>
```

**What's happening:**
```
LocalGameWorld Base:     0x00007FF8D0000000
+ RegisteredPlayers:     +0x168
= RegisteredPlayers Ptr: 0x00007FF8D0000168 → List<IPlayer> (C# List object)
```

This `List<IPlayer>` contains **ALL players in the raid** (you, teammates, enemies, AI bots).

---

### Worker Threads - Continuous Updates

#### Thread 1: Realtime Worker (125Hz - Lines 251-267)

```csharp
private void RealtimeWorker_PerformWork(object sender, WorkerThreadArgs e)
{
    var players = _rgtPlayers.Where(x => x.IsActive && x.IsAlive);

    if (!players.Any()) // No players - Throttle
    {
        Thread.Sleep(1);
        return;
    }

    using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
    foreach (var player in players)
    {
        player.OnRealtimeLoop(scatter);  // Queue position/rotation reads
    }
    scatter.Execute();  // Execute all reads in ONE DMA transaction
}
```

**Why 125Hz for positions?**
- Players move fast in FPS games
- 125Hz = 8ms updates = smooth tracking
- Uses **scatter/gather** to batch all player reads into ONE DMA transaction

---

#### Thread 2: Slow Worker (20Hz - Lines 277-288)

```csharp
private void SlowWorker_PerformWork(object sender, WorkerThreadArgs e)
{
    var ct = e.CancellationToken;
    ValidatePlayerTransforms(); // Check for transform anomalies
    _exfilManager.Refresh();    // Update exit statuses
    Loot.Refresh(ct);           // Update loot positions/items
}
```

**Why 20Hz for loot?**
- Loot doesn't move (static positions)
- 50ms updates = good enough for display
- Lower frequency = less DMA traffic

---

#### Thread 3: Explosives Worker (33Hz)

- Updates grenade positions at 33Hz (30ms)
- Grenades move fast but not as critical as player positions

---

### Complete Flow Diagram

```
[MemDMA.RunGameLoop]
    ↓
LocalGameWorld.CreateGameInstance()
    ↓
GameObjectManager.Get(UnityBase)
    ├─ Read pointer at UnityBase + GameObjectManager offset
    └─ Get GameObjectManager struct { ActiveNodes, LastActiveNode }
    ↓
GameObjectManager.GetGameWorld()
    ├─ Walk linked list from ActiveNodes
    ├─ For each node:
    │   ├─ Read GameObject name
    │   └─ If name == "GameWorld", found it!
    ├─ Follow pointer chain to get LocalGameWorld instance
    ├─ Read map ID from LocalGameWorld.Location
    └─ Return LocalGameWorld address + map ID
    ↓
new LocalGameWorld(address, mapID)
    ├─ Read RegisteredPlayers list (all players in raid)
    ├─ Create LootManager (all loot items)
    ├─ Create ExitManager (all extraction points)
    ├─ Create ExplosivesManager (all grenades)
    └─ Setup 3 worker threads (8ms, 50ms, 30ms)
    ↓
LocalGameWorld.Start()
    ├─ Start Thread 1 (Realtime) → Update player positions at 125Hz
    ├─ Start Thread 2 (Slow) → Update loot/exits at 20Hz
    └─ Start Thread 3 (Explosives) → Update grenades at 33Hz
    ↓
[Radar displays everything on map]
```

---

### Key Offsets (from SDK.cs)

These offsets **break with every EFT patch** and must be manually updated:

| Offset | Value | What It Points To |
|--------|-------|-------------------|
| `ClientLocalGameWorld.RegisteredPlayers` | `0x168` | `List<IPlayer>` (all players) |
| `ClientLocalGameWorld.MainPlayer` | `0x1D0` | Your player |
| `ClientLocalGameWorld.LootList` | `0x140` | `List<LootItem>` (all loot) |
| `ClientLocalGameWorld.ExfilController` | `0x30` | Extraction manager |
| `ClientLocalGameWorld.Grenades` | `0x230` | Grenade dictionary |
| `GameWorld.Location` | `0xA8` | Map ID string |
| `Player.MovementContext` | `0x60` | Position/rotation data |
| `Player.Profile` | `0x8C0` | Player info (name, account ID) |

**How offsets are found:**
- Use tools like **Cheat Engine**, **IDA Pro**, **Ghidra**, or **x64dbg**
- Reverse engineer the game's binary
- Find class layouts in memory
- Update `SDK.cs` with new offsets after each game patch

---

## Summary

### What We've Covered

1. **Project Overview**: External radar using DMA hardware on separate PC
2. **Hardware Setup**: FPGA DMA card reads memory over PCIe, sends to Radar PC via USB
3. **App.xaml.cs**: Entry point, initialization, dependency injection, performance mode
4. **MemDMA.cs**: DMA engine - connects to hardware, reads memory, infinite loop waiting for game
5. **LocalGameWorld.cs & GameObjectManager.cs**: Finding game objects in Unity's memory

### Key Concepts

- **DMA (Direct Memory Access)**: Hardware-level memory reading from separate PC
- **FPGA Algorithm**: Configurable read patterns (Tiny = stealthy, Normal = fast)
- **Memory Map Caching**: Cached list of valid memory pages for faster reads
- **Scatter/Gather**: Batch multiple reads into one DMA transaction
- **Pointer Chains**: Following pointers (A→B→C→D) to find final object
- **Linked Lists**: Unity stores GameObjects in doubly-linked list
- **Worker Threads**: 3 threads updating at different rates (125Hz, 20Hz, 33Hz)
- **Offsets**: Hardcoded memory addresses that break with game updates

### What's Next

In the next sections, we'll cover:
- **Player System**: How player data is extracted (position, rotation, name, type)
- **Loot System**: How items are found and filtered by value
- **Exit System**: How extraction points are tracked
- **UI Rendering**: How the radar is drawn with SkiaSharp
- **Web Radar**: Real-time web interface with SignalR

## 6. Player System - Extracting Player Data

The player system is how the radar tracks all players in the raid - their positions, rotations, names, and types (PMC, Scav, Boss, etc.).

### Player Class Hierarchy

```
AbstractPlayer (abstract base class)
├─> LocalPlayer (your player)
├─> ClientPlayer (other human PMCs - networked players)
│   └─> BtrPlayer (BTR vehicle operator)
└─> ObservedPlayer (AI bots, player scavs)
```

**Why multiple classes?**
- EFT uses different C# classes internally for different player types
- Each has different memory layouts
- The radar needs to handle all types correctly

---

### RegisteredPlayers.cs - Player List Manager

**File**: [RegisteredPlayers.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\RegisteredPlayers.cs)

This class manages the **list of all players** in the raid.

#### Constructor - Get Your Player (Lines 52-59)

```csharp
public RegisteredPlayers(ulong baseAddr, LocalGameWorld game)
{
    Base = baseAddr;  // RegisteredPlayers list address
    _game = game;
    var mainPlayer = Memory.ReadPtr(_game + Offsets.ClientLocalGameWorld.MainPlayer, false);
    var localPlayer = new LocalPlayer(mainPlayer);  // Create your player object
    _players[localPlayer] = LocalPlayer = localPlayer;  // Add to dictionary
}
```

**What's happening:**
1. Read `MainPlayer` pointer from `LocalGameWorld` (offset `0x1D0`)
2. Create `LocalPlayer` instance (your player)
3. Add to `_players` dictionary (key = player address, value = player object)

---

#### Refresh - Update Player List (Lines 66-87)

```csharp
public void Refresh()
{
    try
    {
        using var playersList = MonoList<ulong>.Create(this, false); // Read C# List<IPlayer>
        using var registered = playersList.Where(x => x != 0x0).ToPooledSet();

        /// Allocate New Players
        foreach (var playerBase in registered)  // For each player address
        {
            if (playerBase == LocalPlayer) // Skip LocalPlayer, already allocated
                continue;
            AbstractPlayer.Allocate(_players, playerBase);  // Add new player
        }

        /// Update Existing Players incl LocalPlayer
        UpdateExistingPlayers(registered);  // Update all players
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"CRITICAL ERROR - RegisteredPlayers Loop FAILED: {ex}");
    }
}
```

**Flow:**
1. **Read C# List** - `MonoList<ulong>.Create(this, false)` reads the `List<IPlayer>` from game memory
   - This is a Unity C# collection in memory
   - Contains pointers to all player objects
2. **Filter nulls** - `Where(x => x != 0x0)` removes invalid pointers
3. **Allocate new players** - For each address not in `_players`, create new player object
4. **Update existing players** - Update positions, check if alive/dead

---

#### UpdateExistingPlayers - Batch Update (Lines 105-116)

```csharp
private void UpdateExistingPlayers(ISet<ulong> registered)
{
    var allPlayers = _players.Values;
    if (allPlayers.Count == 0)
        return;
    using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);  // Create scatter
    foreach (var player in allPlayers)
    {
        player.OnRegRefresh(scatter, registered);  // Queue reads for each player
    }
    scatter.Execute();  // Execute ALL reads in one DMA transaction
}
```

**Why scatter/gather here?**
- If there are 10 players, this queues 10 "check if dead" reads
- Executes in **ONE** DMA transaction instead of 10 separate reads
- Much faster and less detectable

---

### AbstractPlayer.cs - Base Player Class

**File**: [AbstractPlayer.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\Player\AbstractPlayer.cs)

This is the **base class** for all player types. Contains common properties and methods.

#### Allocate - Create New Player (Lines 126-152)

```csharp
public static void Allocate(ConcurrentDictionary<ulong, AbstractPlayer> regPlayers, ulong playerBase)
{
    try
    {
        _ = regPlayers.GetOrAdd(
            playerBase,
            addr => AllocateInternal(addr));  // Create player if not exists
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"ERROR during Player Allocation for player @ 0x{playerBase.ToString("X")}: {ex}");
    }
}

private static AbstractPlayer AllocateInternal(ulong playerBase)
{
    AbstractPlayer player;
    var className = ObjectClass.ReadName(playerBase, 64);  // Read C# class name
    var isClientPlayer = className == "ClientPlayer" || className == "LocalPlayer";

    if (isClientPlayer)
        player = new ClientPlayer(playerBase);  // Human PMC
    else
        player = new ObservedPlayer(playerBase);  // AI or Player Scav
    Debug.WriteLine($"Player '{player.Name}' allocated.");
    return player;
}
```

**How it determines player type:**
1. **Read C# class name** from object header (Unity metadata)
2. If class name is `"ClientPlayer"` or `"LocalPlayer"` → **ClientPlayer** (human PMC)
3. Otherwise → **ObservedPlayer** (AI bot or player scav)

**What's `ObjectClass.ReadName`?**
- Unity stores class name in object header
- Format: `[vtable ptr] [class metadata] [class name pointer]`
- Reads the class name string (e.g., "ClientPlayer", "ObservedPlayer")

---

#### Properties - Player Data (Lines 165-280)

```csharp
/// <summary>
/// Player Class Base Address
/// </summary>
public ulong Base { get; }

/// <summary>
/// True if the Player is Active (in the player list).
/// </summary>
public bool IsActive { get; private set; }

/// <summary>
/// Type of player unit.
/// </summary>
public virtual PlayerType Type { get; protected set; }

private Vector2 _rotation;
/// <summary>
/// Player's Rotation in Local Game World.
/// </summary>
public Vector2 Rotation
{
    get => _rotation;
    private set
    {
        _rotation = value;
        float mapRotation = value.X; // Cache value
        mapRotation -= 90f;  // Apply 90-degree correction for map display
        while (mapRotation < 0f)
            mapRotation += 360f;
        MapRotation = mapRotation;
    }
}

/// <summary>
/// Corpse field value.
/// </summary>
public ulong? Corpse { get; private set; }

/// <summary>
/// Player name.
/// </summary>
public virtual string Name { get; set; }

/// <summary>
/// Account UUID for Human Controlled Players.
/// </summary>
public virtual string AccountID { get; }

/// <summary>
/// Player's Faction.
/// </summary>
public virtual Enums.EPlayerSide PlayerSide { get; protected set; }

/// <summary>
/// Player is Human-Controlled.
/// </summary>
public virtual bool IsHuman { get; }
```

**Key properties:**
- **Base** - Memory address of player object
- **IsActive** - True if in raid (false if dead/extracted)
- **Type** - PMC, Scav, Boss, Rogue, etc. (enum)
- **Rotation** - View direction (X = yaw 0-360°, Y = pitch -90° to 90°)
- **Corpse** - If not null, player is dead (address of corpse object)
- **Name** - Player nickname or AI name
- **AccountID** - EFT account UUID (for humans)
- **PlayerSide** - USEC, BEAR, or Scav (enum)
- **IsHuman** - True for human players, false for AI

---

#### Boolean Getters - Player State (Lines 281-370)

```csharp
/// <summary>
/// Player is AI-Controlled.
/// </summary>
public bool IsAI => !IsHuman;

/// <summary>
/// Player is a PMC Operator.
/// </summary>
public bool IsPmc => PlayerSide is Enums.EPlayerSide.Usec || PlayerSide is Enums.EPlayerSide.Bear;

/// <summary>
/// Player is a SCAV.
/// </summary>
public bool IsScav => PlayerSide is Enums.EPlayerSide.Savage;

/// <summary>
/// Player is alive (not dead).
/// </summary>
public bool IsAlive => Corpse is null;

/// <summary>
/// True if Player is Friendly to LocalPlayer.
/// </summary>
public bool IsFriendly =>
    this is LocalPlayer || Type is PlayerType.Teammate;

/// <summary>
/// True if player is Hostile to LocalPlayer.
/// </summary>
public bool IsHostile => !IsFriendly;
```

**Why so many boolean properties?**
- UI filtering (e.g., "show only hostile PMCs")
- Rendering logic (different colors for friendly/hostile)
- Performance (computed once, cached)

---

#### OnRealtimeLoop - Update Position/Rotation at 125Hz (Lines 469-507)

```csharp
public virtual void OnRealtimeLoop(VmmScatter scatter)
{
    scatter.PrepareReadValue<Vector2>(RotationAddress); // Queue rotation read
    scatter.PrepareReadArray<TrsX>(SkeletonRoot.VerticesAddr, SkeletonRoot.Count); // Queue skeleton vertices

    scatter.Completed += (sender, s) =>  // Callback when scatter completes
    {
        bool successRot = false;
        bool successPos = true;

        if (s.ReadValue<Vector2>(RotationAddress, out var rotation))  // Get rotation result
            successRot = SetRotation(rotation);  // Update rotation

        if (s.ReadArray<TrsX>(SkeletonRoot.VerticesAddr, SkeletonRoot.Count) is PooledMemory<TrsX> vertices)
        {
            using (vertices)
            {
                try
                {
                    _ = SkeletonRoot.UpdatePosition(vertices.Span);  // Update position from skeleton
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ERROR getting Player '{Name}' SkeletonRoot Position: {ex}");
                    var transform = new UnityTransform(SkeletonRoot.TransformInternal);  // Re-allocate transform
                    SkeletonRoot = transform;
                }
            }
        }

        IsError = !successRot || !successPos;  // Mark as error if read failed
    };
}
```

**What's happening:**
1. **Queue rotation read** - Player view angles (where they're looking)
2. **Queue skeleton vertices** - Bone positions (for ESP/wallhack - not used on radar, but available)
3. **Callback fires** when scatter.Execute() completes
4. **Update rotation** - Set player's rotation
5. **Update position** - Extract position from skeleton root transform

**What's `SkeletonRoot`?**
- Unity `Transform` object for player's skeleton root bone
- Contains position (Vector3) and rotation (Quaternion)
- Position is world coordinates (e.g., X=125.5, Y=10.2, Z=-50.3)

---

### ClientPlayer.cs - Human PMC Players

**File**: [ClientPlayer.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\Player\ClientPlayer.cs)

This class handles **human-controlled PMC players** (other players, not you).

#### Constructor - Initialize ClientPlayer (Lines 77-93)

```csharp
internal ClientPlayer(ulong playerBase) : base(playerBase)
{
    Profile = Memory.ReadPtr(this + Offsets.Player.Profile);  // Read Profile pointer
    Info = Memory.ReadPtr(Profile + Offsets.Profile.Info);  // Read PlayerInfo pointer
    CorpseAddr = this + Offsets.Player.Corpse;  // Corpse field address
    PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(Info + Offsets.PlayerInfo.Side);  // USEC/BEAR
    if (!Enum.IsDefined<Enums.EPlayerSide>(PlayerSide))
        throw new ArgumentOutOfRangeException(nameof(PlayerSide));

    GroupID = GetGroupNumber();  // Get squad/group number
    MovementContext = GetMovementContext();  // Get movement controller
    RotationAddress = ValidateRotationAddr(MovementContext + Offsets.MovementContext._rotation);

    /// Setup Transform
    var ti = Memory.ReadPtrChain(this, false, _transformInternalChain);  // Follow pointer chain to skeleton
    SkeletonRoot = new UnityTransform(ti);  // Create transform object
    _ = SkeletonRoot.UpdatePosition();  // Read initial position
}
```

**Pointer chain to skeleton (Line 123-131):**
```csharp
private static readonly uint[] _transformInternalChain =
[
    Offsets.Player._playerBody,         // +0x158 → PlayerBody
    Offsets.PlayerBody.SkeletonRootJoint,  // +??? → SkeletonRoot
    Offsets.DizSkinningSkeleton._values,   // +0x30 → Bones list
    MonoList<byte>.ArrOffset,              // +0x10 → Array pointer
    MonoList<byte>.ArrStartOffset + (uint)Bones.HumanBase * 0x8,  // +0x20 + (bone index * 8)
    0x10                                   // +0x10 → TransformInternal
];
```

**Visual representation:**
```
Player Base:           0x00007FF8D0000000
  +0x158 (PlayerBody): 0x00007FF8D1000000
    +0x30 (Skeleton):  0x00007FF8D2000000
      +0x30 (Bones):   0x00007FF8D3000000
        +0x10 (Array): 0x00007FF8D4000000
          +0x20+(bone*8): 0x00007FF8D4000020
            +0x10 (TransformInternal): 0x00007FF8D5000000 ← Skeleton Root Transform
```

---

#### GetGroupNumber - Squad Detection (Lines 98-109)

```csharp
private int GetGroupNumber()
{
    try
    {
        var groupIdPtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.GroupId);
        string groupId = Memory.ReadUnicodeString(groupIdPtr);  // Read group UUID
        return _groups.GetOrAdd(
            groupId,
            _ => Interlocked.Increment(ref _lastGroupNumber));  // Assign group number
    }
    catch { return -1; } // Solo player (no group)
}
```

**How squad detection works:**
- **GroupId** is a UUID string (e.g., "abc123-def456-...")
- **First time seeing UUID** → Assign group number (1, 2, 3, etc.)
- **Same UUID** → Same group number
- **Result**: Players with same `GroupID` are squadmates

**Example:**
```
Player "John" - GroupID UUID: "abc123" → Group 1
Player "Mike" - GroupID UUID: "abc123" → Group 1 (same squad!)
Player "Alex" - GroupID UUID: "def456" → Group 2 (different squad)
```

---

### ObservedPlayer.cs - AI Bots and Player Scavs

**File**: [ObservedPlayer.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\Player\ObservedPlayer.cs)

This class handles **AI bots** (Scavs, Bosses, Raiders) and **player-controlled scavs**.

#### Constructor - Initialize ObservedPlayer (Lines 155-200)

```csharp
internal ObservedPlayer(ulong playerBase) : base(playerBase)
{
    var localPlayer = Memory.LocalPlayer;
    ObservedPlayerController = Memory.ReadPtr(this + Offsets.ObservedPlayerView.ObservedPlayerController);
    // ... validation ...
    ObservedHealthController = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.HealthController);
    CorpseAddr = ObservedHealthController + Offsets.ObservedHealthController.PlayerCorpse;

    MovementContext = GetMovementContext();
    RotationAddress = ValidateRotationAddr(MovementContext + Offsets.ObservedPlayerStateContext.Rotation);

    /// Setup Transform
    var ti = Memory.ReadPtrChain(this, false, _transformInternalChain);
    SkeletonRoot = new UnityTransform(ti);
    _ = SkeletonRoot.UpdatePosition();

    bool isAI = Memory.ReadValue<bool>(this + Offsets.ObservedPlayerView.IsAI);  // AI flag
    IsHuman = !isAI;
    Profile = new PlayerProfile(this, GetAccountID());  // Create profile
    GroupID = isAI ? -1 : GetGroupNumber();  // Groups only for humans

    PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Side);
    if (IsScav)  // If scav...
    {
        if (isAI)  // AI Scav
        {
            var voicePtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.Voice);
            string voice = Memory.ReadUnicodeString(voicePtr);  // Voice line set (determines boss type)
            var role = GetAIRoleInfo(voice);  // Determine AI role (Killa, Reshala, etc.)
            Name = role.Name;
            Type = role.Type;
        }
        else  // Player Scav
        {
            int pscavNumber = Interlocked.Increment(ref _lastPscavNumber);  // Assign number
            Name = $"PScav{pscavNumber}";  // e.g., "PScav1", "PScav2"
            Type = GroupID != -1 && GroupID == localPlayer.GroupID ?
                PlayerType.Teammate : PlayerType.PScav;  // Teammate if same group
        }
    }
}
```

**Key differences from ClientPlayer:**
1. **IsAI flag** (offset `0x58`) determines if AI or human
2. **Voice line set** determines boss type (e.g., "bossBully" = Reshala)
3. **Player Scavs** get numbered names ("PScav1", "PScav2") because real names aren't available
4. **ObservedPlayerController** is different from ClientPlayer's structure

---

### PlayerProfile.cs - Player Stats & Info

**File**: [PlayerProfile.cs](c:\Users\bb\Documents\dma\lone\Lone-EFT-DMA-Radar\src\Tarkov\GameWorld\Player\Helpers\PlayerProfile.cs)

This class fetches **player stats from EFT APIs** (K/D, survival rate, level, etc.).

#### RefreshProfile - Parse API Data (Lines 50-112)

```csharp
private void RefreshProfile()
{
    // --- Nickname ---
    var nick = Data?.Info?.Nickname;
    if (!string.IsNullOrEmpty(nick))
        Name = nick;

    var stats = Data?.PmcStats;
    // --- Overall KD ---
    var items = stats?.Counters?.OverallCounters?.Items;
    if (items is not null)
    {
        var kills = items.FirstOrDefault(x => x.Key?.Contains("Kills") == true)?.Value;
        var deaths = items.FirstOrDefault(x => x.Key?.Contains("Deaths") == true)?.Value;
        if (kills is int k && deaths is int d)
            Overall_KD = d == 0 ? k : k / (float)d;  // Calculate K/D
    }

    // --- Raid Count ---
    var sessions = stats?.Counters?.OverallCounters?.Items?
        .FirstOrDefault(x => x.Key?.Contains("Sessions") == true)?.Value;
    if (sessions is int s)
        RaidCount = s;

    // --- Survival Rate ---
    var surv = Data?.PmcStats?.Counters?.OverallCounters?.Items?
        .FirstOrDefault(x => x.Key?.Contains("Survived") == true)?.Value;
    if (surv is int sc)
        SurvivedCount = sc;
    var rc = RaidCount ?? 0;
    SurvivedRate = rc == 0 ? 0f : SurvivedCount.GetValueOrDefault() / (float)rc * 100f;

    // --- Hours Played ---
    var totalTime = Data?.PmcStats?.Counters?.TotalInGameTime;
    if (totalTime.HasValue && totalTime.Value > 0)
        Hours = (int)Math.Round(totalTime.Value / 3600f);

    // --- Level ---
    var xp = Data?.Info?.Experience;
    if (xp.HasValue)
        Level = StaticGameData.XPTable
            .Where(x => x.Key > xp.Value)
            .Select(x => x.Value)
            .FirstOrDefault() - 1;  // Convert XP to level

    // --- Member Category (EOD, Unheard, etc.) ---
    var info = Data?.Info;
    if (info is not null)
        MemberCategory = (Enums.EMemberCategory)info.MemberCategory;

    // --- Account Type ("UH", "EOD", or "--") ---
    var mc = MemberCategory ?? Enums.EMemberCategory.Default;
    if ((mc & Enums.EMemberCategory.Unheard) == Enums.EMemberCategory.Unheard)
        Acct = "UH";  // Unheard Edition
    else if ((mc & Enums.EMemberCategory.UniqueId) == Enums.EMemberCategory.UniqueId)
        Acct = "EOD";  // Edge of Darkness
    else
        Acct = "--";  // Standard edition
}
```

**Where does this data come from?**
- **EFT Profile API** (eft-api.tech or official BSG API)
- Fetched by account ID in background
- Cached in LiteDB database
- **Not** read from game memory (would require server access)

---

#### FocusIfSus - Suspicious Player Detection (Lines 118-142)

```csharp
private void FocusIfSus()
{
    if (_player.Type is not PlayerType.PMC or PlayerType.PScav)
        return;
    if (Overall_KD is float kd && kd >= 15f) // KD >= 15 (very high)
    {
        _player.IsFocused = true;
    }
    else if (Hours is int hrs && hrs < 30) // Less than 30 hours (new account)
    {
        _player.IsFocused = true;
    }
    else if (SurvivedRate is float sr && sr >= 65f) // 65%+ survival rate
    {
        _player.IsFocused = true;
    }
    else if (Overall_KD is float kd2 && kd2 >= 10f && SurvivedRate is float sr2 && sr2 < 35f) // High KD + low SR (KD dropping)
    {
        _player.IsFocused = true;
    }
    else if (Hours is int hrs2 && hrs2 >= 1000 && SurvivedRate is float sr3 && sr3 < 25f) // 1000+ hours + low SR
    {
        _player.IsFocused = true;
    }
}
```

**Why focus suspicious players?**
- **High K/D (15+)** = Possible cheater or very skilled player
- **Low hours (<30)** = New account (ban evasion?)
- **High survival rate (65%+)** = Unnaturally high
- **K/D dropping** = Player intentionally dying to lower stats
- **IsFocused** = Highlighted on radar for user attention

---

#### RunTwitchLookupAsync - Streamer Detection (Lines 176-186)

```csharp
private async Task RunTwitchLookupAsync(string nickname)
{
    string twitchLogin = await TwitchService.LookupAsync(nickname);  // Search Twitch for nickname
    if (twitchLogin is not null)
    {
        TwitchChannelURL = $"https://twitch.tv/{twitchLogin}";
        _player.UpdateAlerts($"TTV @ {TwitchChannelURL}");  // Alert user
        if (Type != PlayerType.SpecialPlayer)
            Type = PlayerType.Streamer; // Mark as streamer
    }
}
```

**Why detect streamers?**
- **Stream sniping awareness** - User knows they might appear on stream
- **Avoid killing streamers** (some users prefer not to appear on streams)
- Uses **TwitchLib API** to search for channels matching nickname

---

### Complete Player Update Flow

```
[LocalGameWorld.Refresh()] (Main loop - 7.5Hz)
    ↓
RegisteredPlayers.Refresh()
    ├─> Read List<IPlayer> from memory (MonoList)
    ├─> For each new player address:
    │     ├─> AbstractPlayer.Allocate()
    │     ├─> Read class name ("ClientPlayer" or "ObservedPlayer")
    │     ├─> Create ClientPlayer or ObservedPlayer
    │     └─> Initialize (read profile, skeleton, group ID)
    └─> UpdateExistingPlayers()
          ├─> Create scatter
          ├─> For each player: OnRegRefresh() (check if dead/exfil'd)
          └─> Execute scatter

[Worker Thread T1] (Realtime - 125Hz)
    ↓
RealtimeWorker_PerformWork()
    ├─> Get all alive/active players
    ├─> Create scatter
    ├─> For each player: OnRealtimeLoop()
    │     ├─> Queue rotation read
    │     └─> Queue skeleton vertices read
    ├─> Execute scatter
    └─> For each player:
          ├─> Update rotation (view direction)
          └─> Update position (from skeleton root)

[Background] PlayerProfile
    ├─> Fetch stats from EFT API (K/D, survival rate, level)
    ├─> Check if suspicious (FocusIfSus)
    └─> Check Twitch for streamer (RunTwitchLookupAsync)
```

---

### Key Offsets (from SDK.cs)

```csharp
// Player offsets
public const uint MovementContext = 0x60;   // Position/rotation controller
public const uint Profile = 0x8C0;          // Player profile (name, stats)
public const uint Corpse = 0x3E0;           // Corpse object (if dead)

// PlayerInfo offsets (ClientPlayer)
public const uint Side = 0x48;              // USEC/BEAR/Scav (int)
public const uint GroupId = 0x50;           // Squad UUID (string)

// ObservedPlayerView offsets (ObservedPlayer)
public const uint IsAI = 0x58;              // AI flag (bool)
public const uint Side = 0x20;              // USEC/BEAR/Scav (int)
public const uint AccountId = 0x78;         // EFT account UUID (string)
public const uint Voice = 0xC8;             // Voice line set (string)

// MovementContext offsets
public const uint _rotation = 0xC4;         // View rotation (Vector2)
```

---

### Summary of Player System

| Component | What It Does |
|-----------|-------------|
| **RegisteredPlayers** | Manages list of all players in raid |
| **AbstractPlayer** | Base class with common properties/methods |
| **ClientPlayer** | Human PMC players (networked) |
| **ObservedPlayer** | AI bots and player scavs |
| **LocalPlayer** | Your player (special case) |
| **PlayerProfile** | Fetches stats from API, detects streamers/suspicious players |
| **OnRealtimeLoop** | Updates position/rotation at 125Hz using scatter/gather |
| **OnRegRefresh** | Checks if player died/extracted |

---

## 7. Loot System - Tracking Items in the World

The loot system finds and tracks all items in the game world: loose items on the ground, items inside containers (boxes, bags), and items on dead bodies (corpses). It uses the same scatter/gather pattern we saw in the player system for efficient memory reading.

**Main files**:
- [LootManager.cs](src/Tarkov/GameWorld/Loot/Helpers/LootManager.cs) - Coordinates loot reading
- [LootItem.cs](src/Tarkov/GameWorld/Loot/LootItem.cs) - Base class for all loot
- [LootContainer.cs](src/Tarkov/GameWorld/Loot/LootContainer.cs) - Containers with nested items
- [LootCorpse.cs](src/Tarkov/GameWorld/Loot/LootCorpse.cs) - Dead player bodies

---

### LootManager.cs - Finding the Loot List

**File**: [LootManager.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\Tarkov\GameWorld\Loot\Helpers\LootManager.cs)

#### Constructor - Initialize (Lines 55-58)

```csharp
public LootManager(ulong localGameWorld)
{
    _lgw = localGameWorld;
}
```

The LootManager is created by LocalGameWorld and stores a reference to the game world address. This is used to find the loot list.

---

#### Getting the Loot List Pointer (Lines 118-119)

```csharp
var lootListAddr = Memory.ReadPtr(_lgw + Offsets.ClientLocalGameWorld.LootList);
```

**What's happening:**
- `_lgw` = LocalGameWorld base address (e.g., `0x12345000`)
- `Offsets.ClientLocalGameWorld.LootList` = offset to the loot list pointer (e.g., `0x140`)
- Reads the pointer at `LocalGameWorld + 0x140` which points to a Unity List of all loot items

**Why do we need this?**
Because Unity stores all loot items in a C# `List<>` collection. We need to find where that list is in memory so we can read all the items from it.

---

### The 4-Round Scatter/Gather Pattern

The `GetLoot()` method ([LootManager.cs:116-206](src/Tarkov/GameWorld/Loot/Helpers/LootManager.cs#L116-L206)) uses a multi-round scatter pattern similar to what we saw in the player system.

#### Round 1: Read List Structure (Lines 125-128)

```csharp
var round1 = map.AddRound();
round1.AddEntry<int>(lootListId, 0, lootListAddr, Offsets.UnityListBase.Count);
round1.AddEntry<ulong>(lootListId, 1, lootListAddr, Offsets.UnityListBase.Start);
```

**Reads**:
1. **Count** - How many loot items are in the list
2. **Start** - Pointer to the array of item pointers

**Why can't we read everything in one round?**
Because we don't know how many items exist until we read the count first. We need the count to know how many pointers to read in Round 2.

---

#### Round 2: Read Item Pointers (Lines 138-147)

```csharp
for (int i = 0; i < count; i++)
{
    round2.AddEntry<ulong>(lootListId, i, start + (ulong)(i * 0x8));
}
```

**What's happening:**
- For each item index (0 to count-1)
- Read the pointer at `start + (index * 8)` (8 bytes per pointer)
- This gives us the address of each `LootItem` object

**Why batch size 2048?** (Line 119)
```csharp
const int LOOTLIST_BATCHSIZE = 2048;
```

This limits how many loot items we read at once. If there are 5000 items, we'd read them in 3 batches (2048 + 2048 + 904).

**Why do we need batching?**
To prevent DMA timeouts. Reading 10,000+ pointers in one scatter operation could fail or take too long, so we chunk it.

---

#### Round 3: Read Loot Item Properties (Lines 153-159)

```csharp
for (int i = 0; i < count; i++)
{
    var lootItemAddr = results[round2][i].Result;
    round3.AddEntry<ulong>(lootItemId, 0, lootItemAddr, Offsets.LootItemBase.Item); // Item pointer
    round3.AddEntry<ulong>(lootItemId, 1, lootItemAddr, Offsets.LootItemBase.ItemOwner); // Container owner
    round3.AddEntry<ulong>(lootItemId, 2, lootItemAddr, Offsets.LootItemBase.Transform); // Position
}
```

For each loot item base, we read:
1. **Item** - Pointer to the actual item data (weapon, food, etc.)
2. **ItemOwner** - If this item is inside a container, this points to it
3. **Transform** - Unity transform for position in world

**Why do we need ItemOwner?**
To determine if this is a loose item on the ground, or an item inside a backpack/box. If `ItemOwner != 0`, it's inside a container.

---

#### Round 4: Read Item Details (Lines 171-176)

```csharp
round4.AddEntry<ulong>(itemId, 0, itemAddr, Offsets.LootItem.Template); // Template ID
round4.AddEntry<ulong>(itemId, 1, itemAddr, Offsets.LootItem.Grids); // Grids (for containers)
```

Finally, we read:
- **Template** - Pointer to the item template ID (e.g., "5449016a4bdc2d6f028b456f" for roubles)
- **Grids** - For containers (bags, boxes), this points to the grid structure containing nested items

**What's a template ID?**
It's a unique string identifier for each item type in the game. Examples:
- `"5449016a4bdc2d6f028b456f"` = Roubles
- `"5c0a840b86f7742ffa4f2482"` = Red Keycard
- `"590c678286f77426c9660122"` = Morphine

---

### Processing Loot Items

After all reads complete, `ProcessLootIndex()` ([LootManager.cs:211-271](src/Tarkov/GameWorld/Loot/Helpers/LootManager.cs#L211-L271)) determines what type of loot each item is.

#### Reading Template ID (Lines 217-221)

```csharp
var templateIdPtr = Memory.ReadPtr(templateAddr + Offsets.ItemTemplate.Id);
var templateIdWide = Memory.ReadWideString(templateIdPtr);
var templateId = templateIdWide.ToString();
```

This reads the template ID string from the item template object.

---

#### Checking if Item is a Corpse (Lines 225-234)

```csharp
var corpsePlayer = _registeredPlayers
    .Where(x => x.IsAlive == false && x.Corpse == lootItemAddr)
    .FirstOrDefault();

if (corpsePlayer is not null)
{
    corpse = new LootCorpse(lootItemAddr, corpsePlayer, transform, templateId);
    return;
}
```

**How do we identify corpses?**
We check if any dead player's `Corpse` property matches this loot item address. If it does, this is a dead body, not a regular container.

**Why separate corpses from containers?**
Because corpses have a linked player name and special rendering (shows player name instead of "Body").

---

#### Checking if Item is a Container (Lines 237-245)

```csharp
if (gridsAddr != 0)
{
    container = new LootContainer(lootItemAddr, transform, templateId, gridsAddr);
    return;
}
```

**How do we identify containers?**
If the `gridsAddr` (from Round 4) is not zero, this item has an internal grid, meaning it can hold other items. Examples:
- Backpacks
- Weapon cases
- Jackets on walls
- Dead scavs (treated as containers)
- Buried stashes

---

#### Default: Loose Item (Lines 248-249)

```csharp
lootItem = new LootItem(transform, templateId);
```

If it's not a corpse or container, it's a loose item on the ground (weapon, meds, keys, etc.).

---

### LootItem.cs - Base Class for All Loot

**File**: [LootItem.cs](src/Tarkov/GameWorld/Loot/LootItem.cs)

All loot inherits from `LootItem`.

#### Constructor with Market Data (Lines 46-57)

```csharp
public LootItem(ulong transform, string id)
{
    Transform = new Transform(transform, TransformType.Loot);
    ID = id;
    if (TarkovMarketManager.AllItems.TryGetValue(id, out var marketItem))
        Name = marketItem.ShortName;
    else
        Name = $"Item ({id})";
}
```

**What's happening:**
1. Creates a Unity Transform for the item's position
2. Stores the template ID
3. **Looks up the item in the Tarkov Market database** to get a readable name
   - If found: Use the short name (e.g., "GPU" instead of "Graphics Card")
   - If not found: Show the raw ID

**Why do we need TarkovMarketManager?**
Because template IDs are just strings like `"5449016a4bdc2d6f028b456f"`. The market manager translates these to readable names and prices.

**Where does TarkovMarketManager get its data?**
From external APIs (tarkov-market.com or similar) that track flea market prices and item names. This data is cached locally.

---

#### Price Calculation (Lines 86-109)

```csharp
public int Price
{
    get
    {
        if (!TarkovMarketManager.AllItems.TryGetValue(ID, out var marketItem))
            return 0;

        var basePrice = Program.Config.UI.ShowFleaPrices
            ? marketItem.BasePrice
            : marketItem.TraderPrice;

        if (marketItem.PricePerSlot && !marketItem.IsQuest)
        {
            var slotPrice = basePrice * Slots;
            return slotPrice;
        }

        return basePrice;
    }
}
```

**Two pricing modes**:
1. **Flea Market Price** - What players sell for (higher)
2. **Trader Price** - What vendors buy for (lower)

**Per-slot pricing**:
Some items (ammo, meds) are priced per inventory slot. A stack of 60 rounds would be worth more than 30 rounds.

**Example:**
- GPU: 500,000 roubles (fixed price)
- M995 ammo: 1,500 roubles per slot × 60 rounds = 90,000 roubles total

---

#### Filtering Properties (Lines 121-180)

```csharp
public bool IsMeds => Type is ItemType.Meds;
public bool IsFood => Type is ItemType.Food;
public bool IsWeapon => Type is ItemType.Weapon;
public bool IsRegularLoot => Type is ItemType.RegularLoot;
public bool IsValuableLoot => Type is ItemType.ValuableLoot;
public bool IsImportant => Type is ItemType.Important;
```

These properties are used by the UI to filter what loot to show. The `Type` is determined from the market data.

**Why filtering?**
A map can have 10,000+ loot items. Showing all of them would clutter the radar. Users can filter to show only:
- Quest items
- Items worth >100k roubles
- Weapons only
- Meds/food only

---

### Drawing Loot on Radar

The `Draw()` method ([LootItem.cs:241-283](src/Tarkov/GameWorld/Loot/LootItem.cs#L241-L283)) renders loot items.

#### Height-Based Arrow Indicator (Lines 262-277)

```csharp
if (heightDiff > 1.5f)
{
    // Draw UP arrow
    canvas.DrawLine(new SKPoint(x, y), new SKPoint(x - 3, y + 6), paint);
    canvas.DrawLine(new SKPoint(x, y), new SKPoint(x + 3, y + 6), paint);
}
else if (heightDiff < -1.5f)
{
    // Draw DOWN arrow
    canvas.DrawLine(new SKPoint(x, y), new SKPoint(x - 3, y - 6), paint);
    canvas.DrawLine(new SKPoint(x, y), new SKPoint(x + 3, y - 6), paint);
}
```

**What's this for?**
If loot is more than 1.5 meters above or below the local player:
- **UP arrow** (↑) - Item is above you (e.g., on a shelf, second floor)
- **DOWN arrow** (↓) - Item is below you (e.g., in a basement, underground cache)

**Why 1.5 meters?**
It's a reasonable threshold to indicate significant height difference without cluttering the UI for minor elevation changes.

**Visual example:**
```
You are on first floor (Y = 10.0)
GPU on second floor (Y = 13.5)
Height diff = 3.5m > 1.5m → Show UP arrow ↑
```

---

### LootContainer.cs - Containers with Nested Items

**File**: [LootContainer.cs](src/Tarkov/GameWorld/Loot/LootContainer.cs)

`LootContainer` extends LootItem and holds other items inside it.

#### Nested Item Storage (Line 66)

```csharp
public ConcurrentDictionary<ulong, LootItem> Loot { get; } = new();
```

This dictionary holds all items inside the container, keyed by their memory address.

**Example:**
```
Backpack @ 0x12345000
├─> GPU @ 0x12346000
├─> Morphine @ 0x12347000
└─> Roubles @ 0x12348000
```

---

#### Filtered Loot (Lines 72-74)

```csharp
public IEnumerable<LootItem> FilteredLoot => Loot.Values
    .Where(x => x.Filter.Enabled)
    .OrderByDescending(x => x.Price);
```

**What does this do?**
1. Gets all items from the container
2. **Filters** based on user settings (e.g., only show items worth > 50k roubles)
3. **Sorts** by price (most valuable first)

**Why do we need filtering?**
A backpack might have 50 items, but you only care about the GPU and LEDX, not the bandages and crackers.

**Example filter result:**
```
Backpack contains:
- GPU (500k) ✓ SHOW
- LEDX (800k) ✓ SHOW
- Bandage (5k) ✗ HIDE
- Crackers (2k) ✗ HIDE
```

---

### LootCorpse.cs - Dead Player Bodies

**File**: [LootCorpse.cs](src/Tarkov/GameWorld/Loot/LootCorpse.cs)

`LootCorpse` is a specialized container representing dead players.

#### Player Link (Line 38)

```csharp
public AbstractPlayer Player { get; private set; }
```

Stores a reference to the dead player.

---

#### Name Override (Line 42)

```csharp
public override string Name => Player?.Name ?? "Body";
```

**What's this for?**
Instead of showing "Backpack" or "Pockets", the corpse shows the player's name (e.g., "Reshala" or "PMC_Player").

If the player reference is lost, it falls back to "Body".

**Example:**
```
Regular container: "Backpack (3 items)"
Corpse: "Killa (12 items)" ← Shows boss name
```

---

### Complete Loot Update Flow

```
[Slow Worker Thread T2] (20Hz - every 50ms)
    ↓
LootManager.Refresh()
    ↓
GetLoot() - 4-round scatter pattern
    │
    ├─> Round 1: Read List<LootItem> structure
    │     ├─> Count (how many items)
    │     └─> Start (pointer to array)
    │
    ├─> Round 2: Read all item pointers
    │     └─> For each index: Read pointer at Start + (i * 8)
    │
    ├─> Round 3: Read item properties
    │     ├─> Item pointer (actual item data)
    │     ├─> ItemOwner (container check)
    │     └─> Transform (position)
    │
    └─> Round 4: Read item details
          ├─> Template ID (item type string)
          └─> Grids (container grids)
    ↓
ProcessLootIndex() - For each item:
    ├─> Read template ID string
    ├─> Check if corpse (match with dead player)
    │     └─> Create LootCorpse (links to player)
    ├─> Check if container (gridsAddr != 0)
    │     └─> Create LootContainer (can hold items)
    └─> Default: Create LootItem (loose item)
    ↓
[Radar displays filtered loot on map]
```

---

### Visual Representation: Loot Hierarchy

```
LocalGameWorld.LootList (Unity List<LootItem>)
│
├─> LootItem #1 (Loose Item)
│     └─> Template ID: "5449016a4bdc2d6f028b456f" (Roubles)
│
├─> LootContainer #2 (Backpack)
│     ├─> Template ID: "5c0a840b86f7742ffa4f2482" (Backpack)
│     └─> Nested Items:
│           ├─> GPU @ 0x12346000
│           ├─> Morphine @ 0x12347000
│           └─> Roubles @ 0x12348000
│
└─> LootCorpse #3 (Dead Player)
      ├─> Player: "Reshala" (AbstractPlayer reference)
      └─> Nested Items:
            ├─> Golden TT @ 0x12349000
            ├─> Bitcoin @ 0x1234A000
            └─> Ammo @ 0x1234B000
```

---

### Key Offsets (from SDK.cs)

```csharp
// LocalGameWorld offsets
public const uint LootList = 0x140;  // List<LootItem>

// LootItemBase offsets
public const uint Item = 0x10;       // Pointer to item data
public const uint ItemOwner = 0x18;  // Container owner (0 = loose item)
public const uint Transform = 0x20;  // Unity transform (position)

// LootItem offsets
public const uint Template = 0x30;   // Template ID string pointer
public const uint Grids = 0x78;      // Grids (for containers)

// ItemTemplate offsets
public const uint Id = 0x50;         // Template ID string
```

---

### Questions & Answers

**Q1: Why do we use 4 rounds instead of reading everything in one round?**

A: Because we don't know how many items exist until we read the list count (Round 1). We can't read item pointers without knowing the array start (Round 2). We can't read item properties without knowing each item's address (Round 3). Each round depends on data from the previous round.

**Q2: Why is there a separate LootCorpse class instead of just using LootContainer?**

A: Because corpses need special handling - they have a linked player for showing the player's name, and they behave differently in the UI (different color, different priority). It's cleaner to have a separate type than to add "if (isCorpse)" checks everywhere.

**Q3: What happens if an item's template ID isn't in the market database?**

A: The item is created with a fallback name showing the raw ID (e.g., "Item (5449016a4bdc2d6f028b456f)"), and its price will be 0. It will still appear on the radar if filters allow it.

**Q4: How does the loot system know when items are added or removed from containers?**

A: It doesn't track changes in real-time. The `Refresh()` method ([LootManager.cs:76-114](src/Tarkov/GameWorld/Loot/Helpers/LootManager.cs#L76-L114)) is called periodically by the Slow Worker Thread (every 50ms) and re-reads the entire loot list. If items were added/removed, the next refresh will see the changes.

**Q5: Why is loot only updated at 20Hz (50ms) while players are updated at 125Hz (8ms)?**

A: Because loot is mostly static (doesn't move). Players run around at high speeds and need frequent position updates for smooth tracking. Loot sits on shelves or in crates, so 20Hz is more than sufficient. Lower update frequency = less DMA traffic = harder to detect.

---

### Summary of Loot System

| Component | What It Does |
|-----------|-------------|
| **LootManager** | Coordinates loot reading with 4-round scatter pattern |
| **LootItem** | Base class for all loot with price calculation and rendering |
| **LootContainer** | Container with nested items (backpacks, boxes, stashes) |
| **LootCorpse** | Dead player body (links to AbstractPlayer) |
| **TarkovMarketManager** | Translates template IDs to names and prices |
| **Filtering** | User can filter by type, value, quest items |
| **Height Indicators** | Up/down arrows show vertical position relative to player |

---

## 8. Exit System - Extraction Points

The exit system tracks all extraction points on the map - where you can leave the raid. It's much simpler than the loot and player systems because extraction data is mostly **static** (loaded from configuration files) rather than read from game memory in real-time.

**Main files**:
- [ExitManager.cs](src/Tarkov/GameWorld/Exits/ExitManager.cs) - Manages list of exits for current map
- [Exfil.cs](src/Tarkov/GameWorld/Exits/Exfil.cs) - Individual extraction point
- [IExitPoint.cs](src/Tarkov/GameWorld/Exits/IExitPoint.cs) - Interface for all exit types

---

### ExitManager.cs - Managing Extraction Points

**File**: [ExitManager.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\Tarkov\GameWorld\Exits\ExitManager.cs)

#### Constructor - Load Exits for Current Map (Lines 38-57)

```csharp
public ExitManager(string mapId, bool isPMC)
{
    var list = new List<IExitPoint>();
    if (TarkovDataManager.MapData.TryGetValue(mapId, out var map))
    {
        var filteredExfils = isPMC ?
            map.Extracts.Where(x => x.IsShared || x.IsPmc) :
            map.Extracts.Where(x => !x.IsPmc);
        foreach (var exfil in filteredExfils)
        {
            list.Add(new Exfil(exfil));
        }
        foreach (var transit in map.Transits)
        {
            list.Add(new TransitPoint(transit));
        }
    }

    _exits = list;
}
```

**What's happening:**
1. **TarkovDataManager.MapData** contains static map data loaded from JSON files (exit positions, names, etc.)
2. **Filter exits based on PMC/Scav**:
   - If PMC: Show PMC-only exits + shared exits (e.g., "Emercom Checkpoint", "Interchange")
   - If Scav: Show Scav-only exits (different locations)
3. **Create Exfil objects** for each extraction point
4. **Add transit points** (train, car, boat extracts that move/require payment)

**Why do PMC and Scav have different exits?**
In EFT, PMCs and Scavs spawn on opposite sides of the map and have different extraction points. For example, on Customs:
- **PMC exits**: Crossroads, Smuggler's Boat, ZB-1011
- **Scav exits**: Old Gas Station, Trailer Park, Factory Far Corner

---

### Where Does Exit Data Come From?

Unlike players and loot, extraction points **don't come from game memory**. They come from **static configuration files**.

**Why not read from memory?**
Because exit positions never change (they're hardcoded in the map). It's more efficient to load them from a JSON file than to search through game memory.

**How is the data structured?**
The `TarkovDataManager` loads JSON files containing:
```json
{
  "customs": {
    "extracts": [
      {
        "name": "Crossroads",
        "position": { "x": 125.5, "y": 10.2, "z": -50.3 },
        "isPmc": true,
        "isShared": false
      },
      {
        "name": "Old Gas Station",
        "position": { "x": -100.2, "y": 8.5, "z": 200.1 },
        "isPmc": false,
        "isShared": false
      }
    ]
  }
}
```

---

### Exfil.cs - Individual Extraction Point

**File**: [Exfil.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\Tarkov\GameWorld\Exits\Exfil.cs)

#### Constructor - Initialize from Static Data (Lines 38-42)

```csharp
public Exfil(TarkovDataManager.ExtractElement extract)
{
    Name = extract.Name;
    _position = extract.Position.AsVector3();
}
```

**What's happening:**
- Stores the exit name (e.g., "Crossroads")
- Stores the 3D position from the JSON data

---

#### Exit Status (Lines 52, 102-107)

```csharp
public EStatus Status { get; set; } = EStatus.Open; // Default open for now

public enum EStatus
{
    Open,
    Pending,
    Closed
}
```

**What do these statuses mean?**
- **Open** (Green) - Exit is available right now
- **Pending** (Yellow) - Exit will open soon (e.g., "Car extract in 5 minutes")
- **Closed** (Red) - Exit is not available (won't show on radar)

**NOTE**: Currently defaults to `Open`. In a more advanced implementation, this would be read from game memory to show real-time exit status (e.g., whether you have the required key, whether the car has already left, etc.).

---

#### Drawing Exits on Radar (Lines 54-91)

```csharp
public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
{
    // Skip drawing if closed
    if (Status == EStatus.Closed)
        return;

    var heightDiff = Position.Y - localPlayer.Position.Y;

    var paint = Status switch
    {
        EStatus.Open => SKPaints.PaintExfilOpen,
        EStatus.Pending => SKPaints.PaintExfilPending,
        EStatus.Closed => SKPaints.PaintExfilClosed,
        _ => SKPaints.PaintExfilOpen
    };

    var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
    MouseoverPosition = new Vector2(point.X, point.Y);
    SKPaints.ShapeOutline.StrokeWidth = 2f;
    if (heightDiff > 1.85f) // exfil is above player
    {
        using var path = point.GetUpArrow(6.5f);
        canvas.DrawPath(path, SKPaints.ShapeOutline);
        canvas.DrawPath(path, paint);
    }
    else if (heightDiff < -1.85f) // exfil is below player
    {
        using var path = point.GetDownArrow(6.5f);
        canvas.DrawPath(path, SKPaints.ShapeOutline);
        canvas.DrawPath(path, paint);
    }
    else // exfil is level with player
    {
        float size = 4.75f * App.Config.UI.UIScale;
        canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
        canvas.DrawCircle(point, size, paint);
    }
}
```

**What's happening:**
1. **Skip if closed** - Don't draw closed exits
2. **Calculate height difference** - Is exit above/below player?
3. **Choose paint color** based on status:
   - Green = Open
   - Yellow = Pending
   - Red = Closed (not drawn)
4. **Convert world position to map position** - Transform 3D coordinates to 2D radar coordinates
5. **Draw based on height**:
   - **UP arrow (↑)** if exit is >1.85m above you (e.g., second floor)
   - **DOWN arrow (↓)** if exit is >1.85m below you (e.g., underground)
   - **Circle** if exit is roughly at your level

**Why 1.85 meters for exits vs 1.5 meters for loot?**
Slightly higher threshold for exits since vertical precision is less critical for exits than for loot.

---

#### Mouseover - Show Exit Name (Lines 93-98)

```csharp
public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
{
    var exfilName = Name;
    exfilName ??= "unknown";
    Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, exfilName);
}
```

**What's this for?**
When you hover your mouse over an exit marker on the radar, it shows the exit name (e.g., "Emercom Checkpoint").

---

### Visual Representation: Exit Rendering

```
Map: Customs
Local Player Position: (0, 10, 0) - Ground level

Exit #1: "Crossroads"
├─ Position: (125, 10, -50) - Same level
├─ Height diff: 0m
└─ Render: Green circle (open)

Exit #2: "ZB-1012"
├─ Position: (-200, 5, 100) - Underground bunker
├─ Height diff: -5m (below player)
└─ Render: Green down arrow ↓ (open, underground)

Exit #3: "RUAF Roadblock"
├─ Position: (50, 14, 200) - Elevated position
├─ Height diff: 4m (above player)
└─ Render: Green up arrow ↑ (open, elevated)

Exit #4: "Car Extract" (costs 3000₽)
├─ Status: Pending (waiting for car)
├─ Height diff: 0m
└─ Render: Yellow circle (pending)
```

---

### Transit Points

Looking at line 50-53 in ExitManager.cs:

```csharp
foreach (var transit in map.Transits)
{
    list.Add(new TransitPoint(transit));
}
```

**What are transit points?**
Special extraction points that:
- **Move** (trains, cars)
- **Require payment** (money, items)
- **Have timers** (arrive at specific times)

**Examples**:
- **Car extracts** - Costs 3000-7000₽, leaves after timer
- **Train on Reserve** - Arrives at specific times, stays for 2 minutes
- **Boat on Shoreline** - Requires payment, leaves after delay

**NOTE**: `TransitPoint` is a separate class (not shown in the files we read, but referenced in the code).

---

### Comparison: Exit System vs Loot/Player Systems

| Aspect | Exits | Players/Loot |
|--------|-------|--------------|
| **Data Source** | Static JSON files | Game memory (DMA) |
| **Update Frequency** | Once (on raid start) | 125Hz (players), 20Hz (loot) |
| **Position Changes** | Never | Constantly |
| **Complexity** | Simple (just render) | Complex (scatter/gather, multi-round reads) |
| **Detection Risk** | None (no memory access) | Medium (DMA traffic) |

**Why is the exit system so simple?**
Because exits don't move. Once you load the exit positions from the JSON file, you just render them on the map. No memory reading required (except for advanced features like real-time status).

---

### Summary of Exit System

| Component | What It Does |
|-----------|-------------|
| **ExitManager** | Loads exits for current map from JSON, filters by PMC/Scav |
| **Exfil** | Individual extraction point with position and status |
| **TransitPoint** | Special extracts (trains, cars) with timers/costs |
| **TarkovDataManager** | Provides static map data (exit positions) from JSON |
| **Status** | Open/Pending/Closed (currently defaults to Open) |
| **Height Indicators** | Up/down arrows for multi-level extracts |

---

## 9. Explosives System - Tracking Grenades and Tripwires

The explosives system tracks **active grenades** (thrown, mid-air) and **tripwires** (mines placed on the ground). This helps you avoid dying to grenades and alerts you to dangerous areas with mines.

**Main files**:
- [ExplosivesManager.cs](src/Tarkov/GameWorld/Explosives/ExplosivesManager.cs) - Manages collection of active explosives
- [Grenade.cs](src/Tarkov/GameWorld/Explosives/Grenade.cs) - Thrown grenades (mid-flight or bouncing)
- Tripwire.cs - Landmines and tripwires (referenced but not shown)

---

### ExplosivesManager.cs - Managing Active Explosives

**File**: [ExplosivesManager.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\Tarkov\GameWorld\Explosives\ExplosivesManager.cs)

#### Constructor - Initialize (Lines 39-42)

```csharp
public ExplosivesManager(ulong localGameWorld)
{
    _localGameWorld = localGameWorld;
}
```

Simple constructor that stores the LocalGameWorld address for reading grenade/tripwire lists.

---

#### Refresh - Update All Explosives (Lines 47-70)

```csharp
public void Refresh(CancellationToken ct)
{
    GetGrenades(ct);
    GetTripwires(ct);
    var explosives = _explosives.Values;
    if (explosives.Count == 0)
    {
        return;
    }
    using var scatter = Memory.CreateScatter(VmmSharpEx.Options.VmmFlags.NOCACHE);
    foreach (var explosive in explosives)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            explosive.OnRefresh(scatter);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error Refreshing Explosive @ 0x{explosive.Addr.ToString("X")}: {ex}");
        }
    }
    scatter.Execute();
}
```

**What's happening:**
1. **GetGrenades()** - Read list of active grenades from memory
2. **GetTripwires()** - Read list of active tripwires/mines from memory
3. **For each explosive** - Queue position update reads (scatter/gather)
4. **Execute scatter** - Update all positions in one DMA transaction

**When is this called?**
By the **Explosives Worker Thread (T3)** at 33Hz (every 30ms). See LocalGameWorld.cs:103-106.

---

### Reading Grenades from Memory

#### GetGrenades - Read Grenade List (Lines 72-98)

```csharp
private void GetGrenades(CancellationToken ct)
{
    try
    {
        var grenades = Memory.ReadPtr(_localGameWorld + Offsets.ClientLocalGameWorld.Grenades);
        var grenadesListPtr = Memory.ReadPtr(grenades + 0x18);
        using var grenadesList = MonoList<ulong>.Create(grenadesListPtr, false);
        foreach (var grenade in grenadesList)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _ = _explosives.GetOrAdd(
                    grenade,
                    addr => new Grenade(addr, _explosives));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error Processing Grenade @ 0x{grenade.ToString("X")}: {ex}");
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Grenades Error: {ex}");
    }
}
```

**Pointer chain:**
```
LocalGameWorld + Offsets.ClientLocalGameWorld.Grenades → Grenades Manager
  → +0x18 → List<Grenade> (Unity C# List)
    → Contains addresses of all "hot" grenades
```

**What's a "hot" grenade?**
A grenade that has been **thrown** and is currently active (mid-air, bouncing, or lying on ground before exploding).

**When does a grenade appear in this list?**
- When a player pulls the pin and throws it
- When it bounces on the ground
- **Until it explodes** (then removed from list)

---

### Reading Tripwires from Memory

#### GetTripwires - Read Tripwire List (Lines 100-128)

```csharp
private void GetTripwires(CancellationToken ct)
{
    try
    {
        var syncObjectsPtr = Memory.ReadPtrChain(_localGameWorld, true, _toSyncObjects);
        using var syncObjects = MonoList<ulong>.Create(syncObjectsPtr, true);
        foreach (var syncObject in syncObjects)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var type = (Enums.SynchronizableObjectType)Memory.ReadValue<int>(syncObject + Offsets.SynchronizableObject.Type);
                if (type is not Enums.SynchronizableObjectType.Tripwire)
                    continue;
                _ = _explosives.GetOrAdd(
                    syncObject,
                    addr => new Tripwire(addr));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error Processing SyncObject @ 0x{syncObject.ToString("X")}: {ex}");
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Sync Objects Error: {ex}");
    }
}
```

**What are synchronizable objects?**
Objects that need to be **synchronized across all clients** in the raid:
- Doors (open/closed state)
- Switches (on/off)
- Tripwires/mines
- Breach charges
- Shared containers

**How are tripwires identified?**
- Read the `Type` field from each synchronizable object
- If `Type == Tripwire` (enum value), it's a mine/tripwire
- Create a `Tripwire` object to track it

**Pointer chain (Line 35):**
```csharp
private static readonly uint[] _toSyncObjects = new[] {
    Offsets.ClientLocalGameWorld.SynchronizableObjectLogicProcessor,  // +0x??? → Processor
    Offsets.SynchronizableObjectLogicProcessor.SynchronizableObjects  // +0x??? → List<SyncObject>
};
```

---

### Grenade.cs - Individual Grenade

**File**: [Grenade.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\Tarkov\GameWorld\Explosives\Grenade.cs)

#### Constructor - Initialize Grenade (Lines 55-68)

```csharp
public Grenade(ulong baseAddr, ConcurrentDictionary<ulong, IExplosiveItem> parent)
{
    baseAddr.ThrowIfInvalidVirtualAddress(nameof(baseAddr));
    Addr = baseAddr;
    _parent = parent;
    var type = ObjectClass.ReadName(baseAddr, 64, false);
    if (type.Contains("SmokeGrenade"))
    {
        _isSmoke = true;
        return;
    }
    var ti = Memory.ReadPtrChain(baseAddr, false, UnitySDK.UnityOffsets.TransformChain);
    _transform = new UnityTransform(ti);
}
```

**What's happening:**
1. **Read class name** - Is it "SmokeGrenade" or regular grenade?
2. **If smoke grenade**:
   - Set `_isSmoke = true`
   - **Don't track position** (smoke grenades don't explode, not dangerous)
   - Return early (skip transform setup)
3. **If explosive grenade**:
   - Follow pointer chain to get Unity Transform
   - Store transform for position updates

**Why skip smoke grenades?**
Because they're not dangerous (no explosion/damage). Showing them on the radar would clutter the UI without providing useful information.

---

#### OnRefresh - Update Grenade Position (Lines 73-98)

```csharp
public void OnRefresh(VmmScatter scatter)
{
    if (_isSmoke)
    {
        // Smokes never leave the list, don't remove
        return;
    }
    scatter.PrepareReadValue<bool>(this + Offsets.Grenade.IsDestroyed);
    scatter.PrepareReadArray<UnityTransform.TrsX>(_transform.VerticesAddr, _transform.Count);
    scatter.Completed += (sender, x1) =>
    {
        if (x1.ReadValue<bool>(this + Offsets.Grenade.IsDestroyed, out bool destroyed) && destroyed)
        {
            // Remove from parent collection
            _ = _parent.TryRemove(Addr, out _);
            return;
        }
        if (x1.ReadArray<UnityTransform.TrsX>(_transform.VerticesAddr, _transform.Count) is PooledMemory<UnityTransform.TrsX> vertices)
        {
            using (vertices)
            {
                _ = _transform.UpdatePosition(vertices.Span);
            }
        }
    };
}
```

**What's happening:**
1. **Skip if smoke** - Smoke grenades don't need position updates
2. **Queue reads**:
   - `IsDestroyed` flag (has grenade exploded?)
   - Transform vertices (grenade position)
3. **Callback when scatter completes**:
   - **If destroyed** → Remove from `_explosives` dictionary (stop tracking)
   - **If not destroyed** → Update position from transform vertices

**Why check IsDestroyed?**
Because grenades explode after ~3-5 seconds. Once exploded, they're no longer dangerous and should be removed from the radar.

---

#### Draw - Render Grenade on Radar (Lines 104-113)

```csharp
public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
{
    if (_isSmoke)
        return;
    var circlePosition = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
    var size = 5f * App.Config.UI.UIScale;
    SKPaints.ShapeOutline.StrokeWidth = SKPaints.PaintExplosives.StrokeWidth + 2f * App.Config.UI.UIScale;
    canvas.DrawCircle(circlePosition, size, SKPaints.ShapeOutline); // Draw outline
    canvas.DrawCircle(circlePosition, size, SKPaints.PaintExplosives); // draw grenade marker
}
```

**What's happening:**
1. **Skip if smoke** - Don't draw smoke grenades
2. **Convert position** - World 3D → Map 2D → Zoomed coordinates
3. **Draw circle**:
   - Outline (black/dark border)
   - Fill (explosive color - probably red/orange)

**Size**: 5 pixels scaled by UI scale setting

---

### Visual Representation: Grenade Lifecycle

```
[Player throws grenade]
    ↓
Grenade appears in LocalGameWorld.Grenades list
    ↓
ExplosivesManager.GetGrenades() finds it
    ↓
Creates new Grenade(address)
    ├─> Checks class name
    ├─> If "SmokeGrenade" → _isSmoke = true → Don't track position
    └─> If explosive → Get Unity Transform for position tracking
    ↓
[Every 30ms - Explosives Worker Thread T3]
    ↓
Grenade.OnRefresh(scatter)
    ├─> Queue read: IsDestroyed flag
    ├─> Queue read: Transform vertices (position)
    └─> Callback:
          ├─> If destroyed → Remove from _explosives dictionary
          └─> If not destroyed → Update position
    ↓
Grenade.Draw(canvas)
    └─> Render red/orange circle on radar at grenade position
    ↓
[~3-5 seconds later]
    ↓
Grenade explodes
    ↓
IsDestroyed = true
    ↓
Removed from _explosives collection
    ↓
No longer appears on radar
```

---

### Update Frequency Comparison

| System | Update Rate | Why? |
|--------|------------|------|
| **Players** | 125Hz (8ms) | Fast movement, need smooth tracking |
| **Loot** | 20Hz (50ms) | Static objects, slower updates fine |
| **Explosives** | 33Hz (30ms) | Fast movement (grenades fly), but less critical than players |
| **Exits** | Once (on raid start) | Static positions, never change |

**Why 33Hz for grenades?**
- Grenades move fast (thrown, bouncing)
- Need responsive tracking for danger awareness
- But not as critical as player positions
- **33Hz is a good balance** (fast enough to track, not too much DMA traffic)

---

### Tripwires (Mines)

While we didn't see the `Tripwire.cs` file, we can infer from the code:

**What are tripwires?**
- Landmines placed on the ground (MON-50, Claymore, etc.)
- Tripwire traps in certain locations
- Breach charges placed by players

**How are they tracked?**
- Read from `SynchronizableObjects` list
- Filter by `Type == Tripwire`
- Track position (static - don't move)
- Render on radar as warning markers

**Why synchronizable objects?**
Because they need to be synchronized across all players:
- If Player A places a mine, Player B needs to see it
- Same with doors, switches, etc.

---

### Summary of Explosives System

| Component | What It Does |
|-----------|-------------|
| **ExplosivesManager** | Manages collection of active grenades and tripwires |
| **GetGrenades()** | Reads LocalGameWorld.Grenades list, creates Grenade objects |
| **GetTripwires()** | Reads SynchronizableObjects, filters for tripwires |
| **Grenade** | Individual thrown grenade with position tracking |
| **Tripwire** | Individual mine/trap (static position) |
| **OnRefresh()** | Updates positions, checks IsDestroyed flag, removes exploded grenades |
| **Worker Thread T3** | Updates explosives at 33Hz (every 30ms) |

---

## 10. UI Rendering System - Drawing the Radar

The UI rendering system uses **SkiaSharp** (a 2D graphics library) to draw everything on the radar. It runs at **60+ FPS** using hardware-accelerated OpenGL rendering.

**Main files**:
- [RadarViewModel.cs](src/UI/Radar/ViewModels/RadarViewModel.cs) - Main render loop and logic
- RadarTab.xaml.cs - WPF window containing the SkiaSharp canvas
- [Maps/EftMap.cs](src/UI/Radar/Maps/EftMap.cs) - Map rendering (SVG layers)
- [Skia/SKPaints.cs](src/UI/Skia/SKPaints.cs) - Predefined colors and brushes

---

### SkiaSharp vs WPF

**WPF (Windows Presentation Foundation)**:
- Microsoft's UI framework for Windows desktop apps
- Handles windows, controls, buttons, layout
- **NOT used for radar drawing** (too slow for 60 FPS)

**SkiaSharp**:
- Cross-platform 2D graphics library (Google's Skia engine)
- **Hardware-accelerated** OpenGL rendering
- Used by Chrome, Android, Flutter
- Perfect for high-FPS game overlays/radars

**Architecture**:
```
WPF Window (MainWindow.xaml)
  └─> RadarTab.xaml (WPF UserControl)
      └─> SKGLElement (SkiaSharp OpenGL canvas)
          └─> Radar_PaintSurface() (render loop) ← THIS IS WHERE EVERYTHING IS DRAWN
```

---

### The Main Render Loop

**File**: [RadarViewModel.cs](c:\Users\bb\Documents\dma\lone\eft-dma\src\UI\Radar\ViewModels\RadarViewModel.cs)

#### Radar_PaintSurface - The Render Loop (Lines 225-350+)

```csharp
private void Radar_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
{
    // Working vars
    var isStarting = Starting;
    var isReady = Ready;
    var inRaid = InRaid;
    var canvas = e.Surface.Canvas;
    // Begin draw
    try
    {
        Interlocked.Increment(ref _fps); // Increment FPS counter
        SetMapName();
        /// Check for map switch
        string mapID = MapID; // Cache ref
        if (!mapID.Equals(EftMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase)) // Map changed
        {
            EftMapManager.LoadMap(mapID);
        }
        canvas.Clear(); // Clear canvas
        if (inRaid && LocalPlayer is LocalPlayer localPlayer) // LocalPlayer is in a raid -> Begin Drawing...
        {
            var map = EftMapManager.Map; // Cache ref
            ArgumentNullException.ThrowIfNull(map, nameof(map));
            var closestToMouse = _mouseOverItem; // cache ref
            // Get LocalPlayer location
            var localPlayerPos = localPlayer.Position;
            var localPlayerMapPos = localPlayerPos.ToMapPos(map.Config);

            // Map Parameters (zoom, pan, position)
            EftMapParams mapParams;
            if (MainWindow.Instance?.Radar?.Overlay?.ViewModel?.IsMapFreeEnabled ?? false)
            {
                // Map fixed location, click to pan map
                if (_mapPanPosition == default)
                {
                    _mapPanPosition = localPlayerMapPos;
                }
                mapParams = map.GetParameters(Radar, App.Config.UI.Zoom, ref _mapPanPosition);
            }
            else
            {
                // Map auto follow LocalPlayer
                _mapPanPosition = default;
                mapParams = map.GetParameters(Radar, App.Config.UI.Zoom, ref localPlayerMapPos);
            }

            // Draw Map
            map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);

            // Draw loot (if enabled)
            if (App.Config.Loot.Enabled)
            {
                if (Loot?.Reverse() is IEnumerable<LootItem> loot) // Draw important loot last (on top)
                {
                    foreach (var item in loot)
                    {
                        if (App.Config.Loot.HideCorpses && item is LootCorpse)
                            continue;
                        item.Draw(canvas, mapParams, localPlayer);
                    }
                }
            }

            // Draw players, exits, explosives, etc...
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Render Error: {ex}");
    }
}
```

**What's happening:**
1. **FPS counter** - Increment to track frame rate
2. **Check map change** - If map changed (e.g., new raid), load new map SVG
3. **Clear canvas** - Wipe previous frame
4. **Calculate map parameters** - Zoom level, pan position, rotation
5. **Draw map** - Render the base map (SVG layers)
6. **Draw loot** - Render loot items (in reverse order for Z-ordering)
7. **Draw players** - Render player markers
8. **Draw exits, explosives** - Render extraction points and grenades

**Rendering order (Z-order)**:
```
Bottom layer:
1. Map (SVG background)
2. Loot (least important first, most important last)
3. Containers
4. Exits
5. Explosives
6. Players
7. Local Player (always on top)
8. UI widgets (aimview, player info)
Top layer
```

---

### Coordinate Transformation

**The Big Challenge**: Converting 3D game coordinates to 2D radar coordinates.

#### Three Coordinate Systems

1. **Unity World Coordinates** (3D)
   - Game's internal coordinate system
   - Example: `(125.5, 10.2, -50.3)` - X, Y, Z
   - `Y` = height (vertical)
   - `X`, `Z` = horizontal position

2. **Map Coordinates** (2D)
   - Position on the map image (SVG)
   - Example: `(512.3, 789.1)` - X, Y on map
   - Different for each map (different sizes, rotations)

3. **Canvas Coordinates** (2D)
   - Pixels on your screen
   - Example: `(800, 600)` - screen position
   - Affected by zoom and pan

#### Transformation Pipeline

```
Unity Position (3D): (125.5, 10.2, -50.3)
    ↓ ToMapPos()
Map Position (2D): (512.3, 789.1)
    ↓ ToZoomedPos()
Canvas Position (2D): (800, 600)
    ↓
Draw circle at (800, 600)
```

**Example from LootItem.cs (line 241)**:
```csharp
var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
canvas.DrawCircle(point, size, paint);
```

**ToMapPos** (Unity → Map):
- Reads map configuration (scale, rotation, offset)
- Applies rotation matrix
- Scales coordinates
- Result: Position on map image

**ToZoomedPos** (Map → Canvas):
- Applies zoom level (e.g., 50%, 100%, 200%)
- Applies pan offset (if map is dragged)
- Result: Screen pixel position

---

### Map Rendering

Maps are stored as **SVG (Scalable Vector Graphics)** files with multiple layers.

**Example: Customs map**:
```
maps/
  └─ customs/
      ├─ base_layer.svg (buildings outline)
      ├─ height_0.svg (ground floor)
      ├─ height_5.svg (5m elevation)
      ├─ height_10.svg (10m elevation - second floor)
      └─ config.json (map scaling, rotation, spawn points)
```

**Height-based Layer Rendering**:
```csharp
map.Draw(canvas, localPlayer.Position.Y, mapParams.Bounds, mapCanvasBounds);
```

- Compares local player's Y position (height) to layer heights
- **Dims layers** that are far above/below player
- **Highlights layers** at player's current height
- Makes multi-level maps easier to read

**Example**:
```
Player on ground floor (Y = 0):
- Ground layer: Full opacity (1.0)
- Second floor layer: Dimmed (opacity 0.3)

Player on second floor (Y = 10):
- Ground layer: Dimmed (opacity 0.3)
- Second floor layer: Full opacity (1.0)
```

---

### Drawing Players

Players are drawn with different colors/styles based on type:

**Color coding** (from SKPaints.cs):
- **LocalPlayer**: Yellow/Gold (you)
- **Teammates**: Green
- **PMCs (hostile)**: Red
- **Player Scavs**: Orange
- **AI Scavs**: Gray
- **Bosses**: Purple/Pink (special color)

**Drawing logic** (simplified):
```csharp
foreach (var player in AllPlayers)
{
    if (player.HasExfild) continue; // Skip exfiled players
    if (!player.IsAlive && !showCorpses) continue; // Skip dead if corpses hidden

    var paint = player.IsFriendly ? SKPaints.PaintFriendly : SKPaints.PaintHostile;
    var pos = player.Position.ToMapPos(map).ToZoomedPos(mapParams);

    // Draw player marker (circle)
    canvas.DrawCircle(pos, radius, paint);

    // Draw view direction line
    var directionEnd = CalculateDirectionLine(pos, player.Rotation, length);
    canvas.DrawLine(pos, directionEnd, paint);

    // Draw player name (if enabled)
    if (showNames)
    {
        canvas.DrawText(player.Name, pos, paintText);
    }
}
```

---

### FPS Counter and Performance

**FPS Tracking** (Lines 235, 160-170):
```csharp
Interlocked.Increment(ref _fps); // Increment each frame

// Separate thread updates FPS display every second
private void UpdateFPSCounter()
{
    while (true)
    {
        var fps = Interlocked.Exchange(ref _fps, 0); // Read and reset counter
        // Update UI label with FPS value
        Thread.Sleep(1000); // Wait 1 second
    }
}
```

**Expected FPS**:
- **60+ FPS** on modern hardware
- **120+ FPS** on high-end systems
- **Lower FPS** if many players/loot items (hundreds of entities)

**Why FPS matters**:
- Smooth radar updates for tracking fast-moving players
- Responsive mouse-over detection
- Real-time grenade tracking

---

### Mouseover Detection

When you hover your mouse over an entity (player, loot, exit):

**Detection** (simplified):
```csharp
private IMouseoverEntity FindClosestMouseOverItem(SKPoint mousePos)
{
    IMouseoverEntity closest = null;
    float minDistance = float.MaxValue;

    foreach (var entity in MouseOverItems) // All players, loot, exits
    {
        var entityPos = entity.MouseoverPosition; // Pre-calculated screen position
        var distance = CalculateDistance(mousePos, entityPos);

        if (distance < minDistance && distance < threshold)
        {
            minDistance = distance;
            closest = entity;
        }
    }

    return closest;
}
```

**Display**:
```csharp
if (closestToMouse is IMouseoverEntity item)
{
    item.DrawMouseover(canvas, mapParams, localPlayer);
    // Draws tooltip with name, distance, etc.
}
```

---

### Static Properties for Data Access (Lines 48-140)

Notice the static properties in RadarViewModel:

```csharp
private static bool InRaid => Memory?.InRaid ?? false;
private static LocalPlayer LocalPlayer => Memory?.LocalPlayer;
private static IEnumerable<LootItem> Loot => Memory?.Loot?.FilteredLoot;
private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory?.Players;
private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory?.Explosives;
private static IReadOnlyCollection<IExitPoint> Exits => Memory?.Exits;
```

**Why static properties?**
- Render loop runs 60+ times per second
- Accessing data through properties instead of fields allows lazy evaluation
- `?.` null-conditional operator prevents crashes if data not ready

**Data flow**:
```
[DMA Thread] Updates Memory.Players, Memory.Loot, etc.
    ↓ (cross-thread access)
[Render Thread] Reads via static properties
    ↓
Draws on canvas at 60 FPS
```

---

### Summary of UI Rendering System

| Component | What It Does |
|-----------|-------------|
| **RadarViewModel** | Main render loop, coordinates all drawing |
| **SkiaSharp** | Hardware-accelerated 2D graphics (OpenGL) |
| **SKGLElement** | WPF control hosting the SkiaSharp canvas |
| **Radar_PaintSurface** | Render loop (called 60+ times/second) |
| **EftMap** | Map rendering (SVG layers, height-based) |
| **Coordinate Transform** | Unity 3D → Map 2D → Canvas 2D |
| **SKPaints** | Predefined colors for different entity types |
| **Mouseover Detection** | Find entity closest to mouse cursor |
| **FPS Counter** | Track rendering performance |

---

## 11. Complete System Architecture - How It All Works Together

Now that we've covered all the individual components, let's see how everything works together in the complete system.

---

### The Big Picture: Multi-Threaded Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          GAMING PC                                  │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │         EscapeFromTarkov.exe (Running in RAM)                │  │
│  │  - Player positions                                          │  │
│  │  - Loot items                                                │  │
│  │  - Game state                                                │  │
│  └────────────────────────┬─────────────────────────────────────┘  │
│                           │                                         │
│                           ↓ PCIe DMA Card reads memory              │
│  ┌────────────────────────────────────────────────────────────────┐│
│  │  FPGA DMA Card (Captain 75T / Screamer)                       ││
│  │  - Pretends to be audio card (anti-detection)                 ││
│  │  - Reads memory over PCIe bus                                 ││
│  └────────────────────────┬───────────────────────────────────────┘│
└─────────────────────────────┼───────────────────────────────────────┘
                              │ USB Cable
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│                          RADAR PC                                   │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │             Lone-EFT-DMA-Radar.exe (C# / .NET 10)            │  │
│  │                                                               │  │
│  │  Main Thread (UI)                                            │  │
│  │  ├─> WPF Window                                              │  │
│  │  ├─> SkiaSharp Render Loop (60+ FPS)                         │  │
│  │  └─> User input (hotkeys, mouse)                             │  │
│  │                                                               │  │
│  │  Memory Thread (DMA Worker)                                  │  │
│  │  ├─> Wait for EscapeFromTarkov.exe                           │  │
│  │  ├─> Wait for raid to start                                  │  │
│  │  ├─> Create LocalGameWorld                                   │  │
│  │  ├─> Start 3 worker threads                                  │  │
│  │  └─> Main loop: Refresh() every 133ms                        │  │
│  │                                                               │  │
│  │  Worker Thread T1 (Realtime - 125Hz / 8ms)                   │  │
│  │  └─> Update player positions/rotations (scatter/gather)      │  │
│  │                                                               │  │
│  │  Worker Thread T2 (Slow - 20Hz / 50ms)                       │  │
│  │  ├─> Update loot positions/items                             │  │
│  │  ├─> Validate player transforms                              │  │
│  │  └─> Update exit statuses (future)                           │  │
│  │                                                               │  │
│  │  Worker Thread T3 (Explosives - 33Hz / 30ms)                 │  │
│  │  ├─> Update grenade positions                                │  │
│  │  ├─> Check IsDestroyed flags                                 │  │
│  │  └─> Remove exploded grenades                                │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

---

### Thread Synchronization and Data Flow

**The Challenge**: 4 threads accessing shared data simultaneously without crashes.

#### Thread-Safe Collections

All data is stored in **concurrent (thread-safe) collections**:

```csharp
// Player list (accessed by Memory Thread + T1 + T2)
private readonly ConcurrentDictionary<ulong, AbstractPlayer> _players = new();

// Loot items (accessed by Memory Thread + T2 + Render Thread)
private readonly ConcurrentDictionary<ulong, LootItem> _loot = new();

// Explosives (accessed by Memory Thread + T3 + Render Thread)
private readonly ConcurrentDictionary<ulong, IExplosiveItem> _explosives = new();
```

**ConcurrentDictionary** features:
- Thread-safe reads/writes
- No locks needed for simple operations
- Multiple threads can read simultaneously
- `GetOrAdd()` atomically adds items if not present

#### Data Flow Timeline (Single Frame)

```
T=0ms: [Memory Thread] Checks if still in raid
T=0ms: [T1] Starts scatter read for player positions
T=0ms: [T2] Starts scatter read for loot items
T=0ms: [T3] Starts scatter read for grenade positions

T=5ms: [T1] Scatter completes → Updates player positions
T=5ms: [Render Thread] Reads player positions → Draws on canvas

T=8ms: [T1] Sleep until next cycle (waits 3ms)

T=10ms: [T2] Scatter completes → Updates loot positions
T=10ms: [Render Thread] Reads loot positions → Draws on canvas

T=16ms: [Render Thread] VSync - present frame to screen

T=30ms: [T3] Scatter completes → Updates grenade positions
T=30ms: [T3] Checks IsDestroyed → Removes exploded grenades

T=133ms: [Memory Thread] Calls game.Refresh() → Checks player list

--- Next cycle starts ---
T=0ms: Repeat...
```

---

### Performance Characteristics

| Component | Update Rate | DMA Reads/Sec | Purpose |
|-----------|------------|---------------|---------|
| **Player Positions** | 125Hz | ~125 × 10 players = 1,250/sec | Smooth tracking |
| **Loot Items** | 20Hz | ~20 × 100 items = 2,000/sec | Static objects |
| **Grenades** | 33Hz | ~33 × 5 grenades = 165/sec | Fast-moving objects |
| **UI Rendering** | 60Hz | 0 (reads from cache) | Display only |
| **Memory Thread** | 7.5Hz | ~8/sec (validation) | Coordination |

**Total DMA Reads**: ~3,500 reads/second (typical raid with 10 players, 100 loot items, 5 grenades)

**Why scatter/gather is critical**:
- Without scatter: 3,500 individual DMA transactions = slow, detectable
- With scatter: ~178 scatter operations/second (batched reads) = fast, stealthy

---

### Memory Optimization Techniques

#### 1. Scatter/Gather (Batch Reads)

Instead of:
```csharp
// 10 separate DMA reads (slow!)
for (int i = 0; i < 10; i++)
{
    var pos = Memory.ReadValue<Vector3>(playerAddr + (ulong)(i * 0x100));
    positions[i] = pos;
}
```

Do this:
```csharp
// 1 scatter operation (fast!)
using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
for (int i = 0; i < 10; i++)
{
    scatter.Prepare<Vector3>(playerAddr + (ulong)(i * 0x100), out var result);
}
scatter.Execute(); // Single DMA transaction
// All results now available
```

#### 2. Multi-Round Scatter (Dependency Chains)

When data depends on previous reads:

```csharp
using var map = Memory.CreateScatterMap();
var round1 = map.AddRound();
var round2 = map.AddRound();
var round3 = map.AddRound();

// Round 1: Read list count
round1.AddEntry<int>(listId, 0, listAddr, Offsets.Count);

// Round 2: Read item pointers (depends on count from Round 1)
for (int i = 0; i < count; i++)
    round2.AddEntry<ulong>(itemId, i, start + (ulong)(i * 0x8));

// Round 3: Read item data (depends on pointers from Round 2)
for (int i = 0; i < count; i++)
    round3.AddEntry<Vector3>(posId, i, itemAddr[i], Offsets.Position);

map.Execute(); // Executes all 3 rounds in sequence
```

#### 3. Object Pooling (Reduce Garbage Collection)

```csharp
// Instead of: new List<Vector3>() every frame
using var vertices = PooledList<Vector3>.Rent();
// ... use vertices ...
// Automatically returned to pool when disposed
```

**Why pooling?**
- Garbage Collection (GC) can pause all threads
- Pauses = dropped frames on radar
- Object pools = no GC = smooth performance

---

### Configuration and Offsets

**File**: SDK.cs (Offsets namespace)

Every EFT patch breaks the offsets. Example:

```csharp
// Offsets.ClientLocalGameWorld
public const uint RegisteredPlayers = 0x168;  // List<IPlayer>
public const uint LootList = 0x140;            // List<LootItem>
public const uint Grenades = 0x230;            // Grenade manager
public const uint MainPlayer = 0x1D0;          // LocalPlayer instance

// Offsets.Player
public const uint Profile = 0x8C0;             // Player profile
public const uint MovementContext = 0x60;      // Position/rotation
public const uint Corpse = 0x3E0;              // Corpse object (if dead)
```

**What happens when EFT updates?**
1. All offsets change
2. Radar stops working
3. Developer must reverse-engineer new offsets
4. Update SDK.cs
5. Rebuild and release new version

**This is why cheats/radars break every patch!**

---

### Anti-Cheat Evasion Techniques

**1. Scatter/Gather (Reduces DMA Traffic)**
- Fewer PCIe transactions = harder to detect via traffic analysis

**2. Memory Map Caching (mmap.txt)**
- First run: Scans entire RAM (slow, detectable)
- Subsequent runs: Uses cached valid pages (fast, stealthy)

**3. Read-Only Operations**
- `EnableMemoryWriting = false` in MemDMA constructor
- Never writes to game memory
- Writes are easier to detect than reads

**4. FPGA Firmware Customization (from CLAUDE.md)**
- DMA card pretends to be audio device (CMI8738) instead of network card
- Different PCIe class code (0x040300 vs 0x020000)
- Avoids known detection signatures

**5. Dynamic Update Rates**
- Can reduce update frequencies to lower DMA traffic if needed
- Trade-off: Lower FPS on radar vs harder to detect

---

### Failure Modes and Recovery

#### Game Crashed
```
[MemDMA.RunGameLoop]
    └─> try/catch: OperationCanceledException
        └─> Game crashed or closed
            └─> Fire OnProcessStopped event
                └─> Return to RunStartupLoop (wait for game)
```

#### Raid Ended
```
[LocalGameWorld.ThrowIfRaidEnded]
    └─> Read MainPlayer pointer
        └─> If invalid or player count = 0
            └─> throw new OperationCanceledException("Raid has ended!")
                └─> LocalGameWorld.Dispose()
                    └─> Stop all 3 worker threads
                        └─> Return to RunGameLoop
                            └─> Wait for next raid
```

#### DMA Hardware Disconnected
```
[Vmm.ThrowIfProcessNotRunning]
    └─> _vmm.MemReadValue fails
        └─> throw new VmmException("DMA Read Failed!")
            └─> Caught in MemoryPrimaryWorker
                └─> Sleep 1 second
                    └─> Retry infinite loop
```

---

### Summary: Complete Execution Flow

```
1. User launches Lone-EFT-DMA-Radar.exe
    ↓
2. App.xaml.cs static constructor
    ├─> Load config (Config-EFT.json)
    ├─> Setup dependency injection
    └─> Boost process priority
    ↓
3. App.OnStartup
    ├─> Show loading window
    ├─> ConfigureProgramAsync (parallel):
    │   ├─> TarkovDataManager (load boss names, loot prices)
    │   ├─> EftMapManager (load map SVGs)
    │   └─> MemoryInterface.ModuleInitAsync ⚠️
    │       └─> Create MemDMA instance
    │           └─> Connect to FPGA DMA card
    │               └─> Start MemoryPrimaryWorker thread
    └─> Show MainWindow (radar UI)
    ↓
4. MemoryPrimaryWorker (infinite loop)
    ├─> RunStartupLoop
    │   └─> Wait for EscapeFromTarkov.exe
    │       └─> Find UnityPlayer.dll base address
    └─> RunGameLoop
        ├─> LocalGameWorld.CreateGameInstance
        │   └─> Wait for raid to start
        │       └─> GameObjectManager.GetGameWorld
        │           └─> Find "GameWorld" GameObject
        │               └─> Create LocalGameWorld instance
        │                   ├─> RegisteredPlayers (all players)
        │                   ├─> LootManager (all loot)
        │                   ├─> ExitManager (exits)
        │                   ├─> ExplosivesManager (grenades)
        │                   └─> Create 3 worker threads
        ├─> game.Start()
        │   ├─> Start T1 (Player positions - 125Hz)
        │   ├─> Start T2 (Loot - 20Hz)
        │   └─> Start T3 (Grenades - 33Hz)
        └─> While in raid:
            ├─> game.Refresh() every 133ms (validate raid active)
            ├─> T1: Update player positions (scatter/gather)
            ├─> T2: Update loot items (scatter/gather)
            ├─> T3: Update grenades (scatter/gather)
            └─> Render Thread: Draw everything at 60 FPS
                ├─> Read data from concurrent dictionaries
                ├─> Transform coordinates (Unity → Map → Canvas)
                ├─> SkiaSharp OpenGL rendering
                └─> Present frame to screen
    ↓
5. Raid ends
    └─> Dispose LocalGameWorld
        ├─> Stop all 3 worker threads
        └─> Return to RunGameLoop (wait for next raid)
```

---

### Key Takeaways

1. **Multi-threaded architecture** for performance:
   - Memory thread (coordination)
   - 3 worker threads (different update rates for different data)
   - Render thread (60 FPS display)

2. **Scatter/gather pattern** is critical:
   - Batches multiple reads into one DMA transaction
   - 10-100x performance improvement
   - Reduces detection risk

3. **Thread-safe collections** prevent crashes:
   - ConcurrentDictionary for all shared data
   - Multiple threads reading/writing simultaneously

4. **Unity integration** requires understanding:
   - GameObjects and transforms
   - Pointer chains
   - C# List/Dictionary structures in memory

5. **Offsets break every patch**:
   - Hardcoded memory addresses
   - Must reverse-engineer after each EFT update
   - This is why radars/cheats go offline after patches

6. **Detection evasion** is multi-layered:
   - Hardware (FPGA firmware customization)
   - Software (scatter/gather, read-only, caching)
   - Timing (variable update rates)

---

## Topic #12: ESP (Extra Sensory Perception) Overlay System

Now we'll explore the **ESP overlay** - the transparent fullscreen window that renders player skeletons, bounding boxes, and loot directly on top of the game screen.

---

### What is ESP?

**ESP (Extra Sensory Perception)** overlays 3D game entities (players, loot) directly onto your screen by:

1. **Reading the game's camera view matrix** from memory
2. **Projecting 3D world coordinates → 2D screen coordinates** (WorldToScreen)
3. **Rendering skeletons/boxes/text** on a transparent overlay window
4. **High-frequency updates** (up to 1000 FPS capable)

Unlike the radar (top-down 2D map), ESP shows entities in their actual 3D positions as if you had wallhacks in-game.

---

### Architecture Overview

The ESP system consists of **three main components**:

1. **ESPManager** - Static manager that controls the ESP window lifecycle
2. **CameraManager** - Reads game camera's view matrix and FOV from memory
3. **ESPWindow** - Transparent fullscreen overlay that renders skeletons/boxes

```
Game Memory → CameraManager → ViewMatrix → ESPWindow → WorldToScreen → Render
```

---

### Component #1: CameraManager - Reading Game Camera

**File**: [CameraManager.cs](src/Tarkov/GameWorld/CameraManager.cs)

The **CameraManager** finds and tracks the game's camera objects in memory, then reads the **view matrix** needed for WorldToScreen projection.

#### Finding Cameras (Lines 37-126)

Unity stores all active cameras in a **CameraObjectManager** list. The code searches for two specific cameras:

```csharp
// Line 24
private const uint CameraObjectManager = 0x19EE080; // Hardcoded offset

// Lines 46-47
var addr = Memory.ReadPtr(Memory.UnityBase + CameraObjectManager, false);
var cameraManager = Memory.ReadPtr(addr, false);

// Lines 63-101
for (int i = 0; i < 100; i++)
{
    var camera = Memory.ReadPtr(cameraManager + (ulong)i * 0x8, false);
    if (camera == 0)
        continue;

    // Read GameObject name (Lines 69-75)
    Span<uint> nameChain = stackalloc uint[] { 0x48, 0x78 };
    var namePtr = Memory.ReadPtrChain(camera, false, nameChain);
    var name = Memory.ReadUtf8String(namePtr, 128, false);

    // Lines 92-101
    if (name == "FPS Camera")
    {
        _fpsCamera = camera; // Main first-person camera
    }
    else if (name == "BaseOpticCamera(Clone)")
    {
        _opticCamera = camera; // Picture-in-Picture scope camera
    }
}
```

**Why two cameras?**
- **FPS Camera**: Default first-person view (hipfire, iron sights)
- **BaseOpticCamera**: Used for Picture-in-Picture scopes (ACOG, ELCAN, etc.)

When you aim down sights with a PiP scope, the game switches to the optic camera for more accurate projection.

#### Reading View Matrix and FOV (Lines 167-227)

Every frame, the CameraManager reads:
- **ViewMatrix**: 4x4 transformation matrix for 3D → 2D projection
- **FOV**: Field of view in degrees
- **IsADS**: Is player aiming down sights?
- **IsScoped**: Is player using a magnified optic?

```csharp
// Lines 177-178
IsADS = localPlayer?.IsAiming ?? false;
IsScoped = IsADS && CheckIfScoped(localPlayer);

// Lines 187-215
bool useOpticCamera = IsADS && IsScoped && _opticCamera != 0;

if (useOpticCamera)
{
    // When scoped with PiP optic, use optic camera's ViewMatrix
    ViewMatrix = Memory.ReadValue<Matrix4x4>(_opticCamera + UnitySDK.UnityOffsets.Camera.ViewMatrix, false);
    FOV = Memory.ReadValue<float>(_fpsCamera + UnitySDK.UnityOffsets.Camera.FOV, false);
}
else
{
    // Regular FPS camera for hipfire and iron sights
    ViewMatrix = Memory.ReadValue<Matrix4x4>(_fpsCamera + UnitySDK.UnityOffsets.Camera.ViewMatrix, false);
    FOV = Memory.ReadValue<float>(_fpsCamera + UnitySDK.UnityOffsets.Camera.FOV, false);
}
```

**Why read both cameras?**

PiP scopes render the zoomed view separately from the main view. The **optic camera** has the correct view matrix for projecting the zoomed world, while the **FPS camera** retains the original FOV.

---

### Component #2: ESPManager - Window Lifecycle

**File**: [ESPManager.cs](src/UI/ESP/ESPManager.cs)

The **ESPManager** is a simple static manager that creates and controls the ESP overlay window.

```csharp
// Lines 10-23 - Initialize ESP window (singleton)
public static void Initialize()
{
    if (_espWindow is not null)
        return;

    _espWindow = new ESPWindow();
    _espWindow.Show(); // Shows the transparent overlay
}

// Lines 25-33 - Toggle visibility
public static void ToggleESP()
{
    if (_espWindow is not null)
    {
        if (_espWindow.Visibility == Visibility.Visible)
            _espWindow.Hide();
        else
            _espWindow.Show();
    }
}

// Lines 44-59 - Force fullscreen mode
public static void StartESP()
{
    if (_espWindow is null)
        Initialize();

    _espWindow.Show();
    _espWindow.WindowState = WindowState.Maximized; // Fullscreen
    _espWindow.Topmost = true; // Always on top
}
```

The ESP window is **always active** but can be hidden/shown on demand.

---

### Component #3: ESPWindow - Rendering Overlay

**File**: [ESPWindow.xaml.cs](src/UI/ESP/ESPWindow.xaml.cs) (882 lines)

This is the core of the ESP system - a **transparent fullscreen WPF window** that renders skeletons, boxes, and loot using **SkiaSharp** (same as radar).

#### Window Properties

```csharp
// Lines 13-30 - Transparent fullscreen overlay
public ESPWindow()
{
    InitializeComponent();

    // Transparent background
    AllowsTransparency = true;
    Background = System.Windows.Media.Brushes.Transparent;
    WindowStyle = WindowStyle.None; // No title bar
    ResizeMode = ResizeMode.NoResize;
    Topmost = true; // Always on top

    // Fullscreen
    WindowState = WindowState.Maximized;
    Width = SystemParameters.PrimaryScreenWidth;
    Height = SystemParameters.PrimaryScreenHeight;
}
```

**Click-through mode**: The window is **transparent to mouse clicks** so you can interact with the game underneath (not shown in code, configured in XAML).

#### High-Frequency Rendering (Lines 169-208)

Unlike the radar (capped at 60 FPS), ESP can run **up to 1000 FPS** for ultra-smooth skeleton tracking:

```csharp
// Lines 169-174
private System.Timers.Timer _highFreqTimer = new System.Timers.Timer
{
    Interval = 1, // 1 millisecond = 1000 FPS max
    AutoReset = true
};

// Lines 176-191 - High-speed timer callback
private void HighFrequencyRenderCallback(object sender, EventArgs e)
{
    long now = Stopwatch.GetTimestamp();
    long elapsed = now - _lastRenderTime;

    // FPS limiter (Lines 184-187)
    if (elapsed < _minFrameTime)
        return; // Skip frame if too fast

    _lastRenderTime = now;

    // Force canvas redraw
    Dispatcher.Invoke(() =>
    {
        skCanvas.InvalidateVisual();
    }, DispatcherPriority.Render);
}
```

**FPS limiting**: User can configure max FPS in settings. The timer fires every 1ms, but the code skips frames based on `_minFrameTime` to achieve target FPS (30/60/144/240/unlimited).

#### Main Render Loop (Lines 234-321)

The **OnPaintSurface** method renders every frame:

```csharp
// Lines 234-245
private void OnPaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
{
    var canvas = e.Surface.Canvas;
    canvas.Clear(SKColors.Transparent); // Clear to transparent

    // Don't render if not in raid
    if (!Memory.IsInGame() || !TarkovData.IsReady)
        return;

    var localPlayer = TarkovData.LocalPlayer;
    if (localPlayer is null)
        return;
```

**Rendering order** (Lines 268-315):

```csharp
// Line 268 - Update camera matrix from game memory
_cameraManager.Update(localPlayer);

// Lines 270-276 - Draw loot (if enabled)
if (App.Config.ESP.LootESP)
{
    DrawLoot(canvas, localPlayer);
}

// Lines 278-281 - Draw exits (if enabled)
if (App.Config.ESP.ExitESP)
{
    DrawExits(canvas);
}

// Lines 283-315 - Draw players
var players = TarkovData.AllPlayers;
foreach (var player in players)
{
    if (player.IsLocalPlayer)
        continue; // Don't draw yourself

    if (App.Config.ESP.SkeletonESP)
        DrawSkeleton(canvas, player, localPlayer);

    if (App.Config.ESP.BoundingBoxESP)
        DrawBoundingBox(canvas, player, localPlayer);

    if (App.Config.ESP.PlayerNameESP)
        DrawPlayerName(canvas, player, localPlayer);
}
```

---

### WorldToScreen Projection - The Magic Formula

The most critical part of ESP is **converting 3D world coordinates → 2D screen coordinates**.

#### The Math (Lines 648-667)

```csharp
private bool WorldToScreen2(Vector3 worldPos, out Vector2 screenPos, float distance)
{
    screenPos = Vector2.Zero;

    // Optimization: Use transposed view matrix for faster dot products
    var viewT = new TransposedViewMatrix(_cameraManager.ViewMatrix);

    // Transform world position by view matrix (3D → Camera Space)
    float camX = viewT.Dot0(worldPos) + viewT.M30;
    float camY = viewT.Dot1(worldPos) + viewT.M31;
    float camZ = viewT.Dot2(worldPos) + viewT.M32;

    // Perspective divide (Camera Space → NDC)
    if (camZ <= 0.01f) // Behind camera
        return false;

    float fovScale = (float)Math.Tan(_cameraManager.FOV * 0.0087266462599716478846184538424431); // deg→rad÷2
    float fovX = fovScale * _cameraManager.AspectRatio;
    float fovY = fovScale;

    // NDC → Screen coordinates
    screenPos.X = (_screenWidth * 0.5f) * (1f - camX / (fovX * camZ));
    screenPos.Y = (_screenHeight * 0.5f) * (1f + camY / (fovY * camZ));

    return true; // Successfully projected
}
```

**What's happening here?**

1. **View Matrix Transform**: Converts world coordinates to camera-relative coordinates (like looking through the camera lens)
2. **Perspective Divide**: Divides X/Y by Z distance to simulate perspective (objects farther away appear smaller)
3. **NDC to Screen**: Converts normalized coordinates to pixel coordinates (0,0 = top-left, width,height = bottom-right)

#### Optimized View Matrix (Lines 669-703)

The code uses a **transposed view matrix** for faster math:

```csharp
private readonly ref struct TransposedViewMatrix
{
    // Cache matrix components for fast dot products
    public readonly float M00, M01, M02, M30;
    public readonly float M10, M11, M12, M31;
    public readonly float M20, M21, M22, M32;

    public TransposedViewMatrix(Matrix4x4 m)
    {
        M00 = m.M11; M01 = m.M21; M02 = m.M31; M30 = m.M41;
        M10 = m.M12; M11 = m.M22; M12 = m.M32; M31 = m.M42;
        M20 = m.M13; M21 = m.M23; M22 = m.M33; M32 = m.M43;
    }

    // Fast dot product for X component
    public readonly float Dot0(Vector3 v) => v.X * M00 + v.Y * M01 + v.Z * M02;
    // Fast dot product for Y component
    public readonly float Dot1(Vector3 v) => v.X * M10 + v.Y * M11 + v.Z * M12;
    // Fast dot product for Z component
    public readonly float Dot2(Vector3 v) => v.X * M20 + v.Y * M21 + v.Z * M22;
}
```

**Why transpose?** It's faster to compute dot products with row vectors than column vectors in C#. This saves ~30% CPU time on the hottest code path.

---

### Drawing Skeletons (Lines 491-503)

Player skeletons are drawn by connecting **bone positions**:

```csharp
private void DrawSkeleton(SKCanvas canvas, AbstractPlayer player, LocalPlayer localPlayer)
{
    var paint = GetPlayerPaint(player); // Color based on team/type

    // Lines 57-88 - Bone connections array (defined at top of file)
    // Example: (Bones.HumanHead, Bones.HumanNeck) draws head→neck line
    foreach (var (from, to) in _boneConnections)
    {
        if (!player.BodyTransforms.TryGetValue(from, out var fromPos) ||
            !player.BodyTransforms.TryGetValue(to, out var toPos))
            continue; // Bone not available

        // Project both bone positions to screen
        if (!WorldToScreen2(fromPos, out var fromScreen, Vector3.Distance(localPlayer.Position, fromPos)))
            continue;
        if (!WorldToScreen2(toPos, out var toScreen, Vector3.Distance(localPlayer.Position, toPos)))
            continue;

        // Draw line connecting bones
        canvas.DrawLine(
            new SKPoint(fromScreen.X, fromScreen.Y),
            new SKPoint(toScreen.X, toScreen.Y),
            paint
        );
    }
}
```

**Bone connections** (Lines 57-88):

```csharp
private static readonly (Bones from, Bones to)[] _boneConnections = new[]
{
    // Head → Neck → Spine
    (Bones.HumanHead, Bones.HumanNeck),
    (Bones.HumanNeck, Bones.HumanSpine3),
    (Bones.HumanSpine3, Bones.HumanSpine2),
    (Bones.HumanSpine2, Bones.HumanPelvis),

    // Left arm
    (Bones.HumanSpine3, Bones.HumanLCollarbone),
    (Bones.HumanLCollarbone, Bones.HumanLShoulder),
    (Bones.HumanLShoulder, Bones.HumanLForearm1),
    (Bones.HumanLForearm1, Bones.HumanLPalm),

    // Right arm
    (Bones.HumanSpine3, Bones.HumanRCollarbone),
    (Bones.HumanRCollarbone, Bones.HumanRShoulder),
    (Bones.HumanRShoulder, Bones.HumanRForearm1),
    (Bones.HumanRForearm1, Bones.HumanRPalm),

    // Left leg
    (Bones.HumanPelvis, Bones.HumanLThigh2),
    (Bones.HumanLThigh2, Bones.HumanLCalf),
    (Bones.HumanLCalf, Bones.HumanLFoot),

    // Right leg
    (Bones.HumanPelvis, Bones.HumanRThigh2),
    (Bones.HumanRThigh2, Bones.HumanRCalf),
    (Bones.HumanRCalf, Bones.HumanRFoot)
};
```

This creates a stick-figure skeleton by connecting ~20 bones.

---

### Drawing Bounding Boxes (Lines 505-543)

Bounding boxes are calculated by finding the **min/max screen coordinates** of all bones:

```csharp
private void DrawBoundingBox(SKCanvas canvas, AbstractPlayer player, LocalPlayer localPlayer)
{
    float minX = float.MaxValue, minY = float.MaxValue;
    float maxX = float.MinValue, maxY = float.MinValue;

    // Project all bones to screen and find extents
    foreach (var (bone, worldPos) in player.BodyTransforms)
    {
        if (!WorldToScreen2(worldPos, out var screenPos, Vector3.Distance(localPlayer.Position, worldPos)))
            continue;

        minX = Math.Min(minX, screenPos.X);
        minY = Math.Min(minY, screenPos.Y);
        maxX = Math.Max(maxX, screenPos.X);
        maxY = Math.Max(maxY, screenPos.Y);
    }

    // Add padding
    float padding = 5f;
    minX -= padding; minY -= padding;
    maxX += padding; maxY += padding;

    // Draw rectangle
    var rect = new SKRect(minX, minY, maxX, maxY);
    canvas.DrawRect(rect, GetPlayerPaint(player));
}
```

**Result**: A 2D rectangle that tightly fits around the player's skeleton.

---

### Drawing Loot on ESP (Lines 323-437)

Loot rendering on ESP includes **cone filtering** - only showing loot that's somewhat visible on screen:

```csharp
private void DrawLoot(SKCanvas canvas, LocalPlayer localPlayer)
{
    var lootItems = TarkovData.Loot.Values;

    foreach (var loot in lootItems)
    {
        float distance = Vector3.Distance(localPlayer.Position, loot.Position);

        // Distance filtering
        if (distance > App.Config.ESP.LootMaxDistance)
            continue;

        // Price filtering
        if (loot.TotalPrice < App.Config.ESP.LootMinPrice)
            continue;

        // Project to screen
        if (!WorldToScreen2(loot.Position, out var screenPos, distance))
            continue; // Behind camera or off-screen

        // Cone filtering - only show loot within viewing angle (Lines 373-392)
        var dirToLoot = Vector3.Normalize(loot.Position - localPlayer.Position);
        var viewDir = _cameraManager.ViewMatrix.Forward; // Camera forward vector
        float angle = Vector3.Dot(dirToLoot, viewDir);

        if (angle < 0.3f) // ~73° cone (outside FOV)
            continue;

        // Draw loot text at projected position (Lines 415-432)
        var text = $"{loot.Name} [{distance:F0}m] ({loot.TotalPrice:N0}₽)";
        var paint = loot.IsImportant ? SKPaints.TextLootImportant : SKPaints.TextLoot;

        canvas.DrawText(text,
            new SKPoint(screenPos.X, screenPos.Y),
            paint
        );
    }
}
```

**Cone filtering** prevents loot behind you from cluttering the screen - only shows loot you're roughly facing.

---

### ESP vs Radar - Key Differences

| Feature | Radar | ESP |
|---------|-------|-----|
| **View** | Top-down 2D map | 3D overlay on screen |
| **Coordinates** | Unity world → Map 2D | Unity world → Screen 2D |
| **Update Rate** | 60 FPS (locked) | 30-1000 FPS (configurable) |
| **Rendering** | SkiaSharp CPU | SkiaSharp OpenGL |
| **Math** | Simple X/Z scaling | Complex view matrix projection |
| **Use Case** | Spatial awareness | Precise aiming/looting |

---

### Configuration Options

Users can configure ESP settings in `Config.json`:

```json
{
  "ESP": {
    "Enabled": true,
    "MaxFPS": 144,              // FPS limiter (30/60/144/240/unlimited)
    "SkeletonESP": true,        // Draw player skeletons
    "BoundingBoxESP": false,    // Draw 2D boxes around players
    "PlayerNameESP": true,      // Draw player names/distance
    "LootESP": true,            // Show loot on ESP
    "LootMaxDistance": 100,     // Hide loot beyond 100m
    "LootMinPrice": 50000,      // Only show loot worth 50k+
    "ExitESP": true             // Show extraction points
  }
}
```

---

### Performance Optimization

The ESP system uses several optimizations for high FPS:

1. **Transposed view matrix** (Lines 669-703): ~30% faster dot products
2. **Stackalloc for temporary arrays**: No heap allocations in hot path
3. **FPS limiting**: User can cap at 60/144 FPS to reduce CPU load
4. **Cone filtering**: Only process loot that's visible (Lines 373-392)
5. **Distance culling**: Skip distant entities early (Lines 286-288)

Without these, ESP would drop below 60 FPS with 20+ players on screen.

---

### How Cameras are Found

The **CameraObjectManager** offset (Line 24: `0x19EE080`) points to a Unity internal structure:

```
Memory.UnityBase + 0x19EE080 → Pointer to CameraManager
CameraManager + 0x0 → Pointer to Camera array
Camera array + (i * 0x8) → Pointer to Camera GameObject i
Camera GameObject + 0x48 → Pointer to GameObject
GameObject + 0x78 → Pointer to Name string
```

By iterating through 100 slots, the code finds **"FPS Camera"** and **"BaseOpticCamera(Clone)"**.

**Why this works**:
- Unity keeps all active cameras in a global list
- Camera names are consistent across game sessions
- FPS Camera is always present
- Optic camera only exists when using PiP scopes (ACOG, ELCAN, etc.)

---

### Why Two View Matrices?

Escape from Tarkov uses **Picture-in-Picture (PiP) scopes** for realism:

- **Without scope**: FPS Camera renders entire view
- **With PiP scope**:
  - Optic Camera renders zoomed-in scope view
  - FPS Camera renders peripheral vision outside scope

The ESP needs the **optic camera's view matrix** when scoped to project world positions accurately within the magnified view.

**Example scenario**:
1. Player aims down ACOG scope (4x magnification)
2. `IsADS = true`, `IsScoped = true` (Line 177-178)
3. ViewMatrix switches from FPS Camera → Optic Camera (Line 192)
4. Skeleton projection now aligns with zoomed view

Without this, skeletons would appear in wrong positions when using PiP scopes.

---

### Detection Vectors

ESP overlays are **more detectable** than radars because:

1. **Screen capture detection**: Anti-cheat can screenshot and detect overlays
2. **Window enumeration**: Scanning for transparent always-on-top windows
3. **Graphics hooks**: Detecting DirectX/OpenGL hooks (though this uses WPF, not hooks)
4. **Mouse transparency**: Detecting click-through windows

The radar is **safer** because it runs in a separate window on a different monitor (or even different PC via network mode). ESP runs **on the same screen** as the game.

---

### ESP Execution Flow

```
1. ESPManager.Initialize() → Create transparent fullscreen window
2. High-frequency timer starts (1ms interval)
3. Every frame:
   a. CameraManager.Update() → Read ViewMatrix from game memory
   b. OnPaintSurface() → Render loop begins
   c. For each player:
      - Get bone positions from player.BodyTransforms
      - WorldToScreen2(bonePos) → Convert 3D → 2D
      - DrawLine(bone1, bone2) → Draw skeleton lines
   d. For each loot item:
      - Cone filtering (only show loot you're facing)
      - WorldToScreen2(lootPos)
      - DrawText(loot name/price)
4. SkiaSharp renders to OpenGL → GPU → Screen
```

---

### Key Takeaways

1. **ESP = 3D overlay on game screen** using WorldToScreen projection
2. **CameraManager reads ViewMatrix** from game memory (FPS Camera or Optic Camera)
3. **WorldToScreen math** transforms 3D world coords → 2D screen pixels
4. **Skeletons are drawn** by connecting bone positions with lines
5. **High-frequency rendering** (up to 1000 FPS) for smooth tracking
6. **Cone filtering** only shows loot you're facing (reduces clutter)
7. **PiP scope support** switches view matrix when using magnified optics
8. **More detectable than radar** due to screen overlay nature

---

**ESP vs Radar Summary**:
- **Radar** = Strategic overview (where is everyone?)
- **ESP** = Tactical precision (exactly where to aim/loot)

Most users run **both simultaneously** - radar for awareness, ESP for precision.

---

**End of Walkthrough - Complete!**

You now understand how the entire Lone EFT DMA Radar works, from hardware DMA card to final rendering on screen (both radar and ESP). This knowledge applies to understanding any DMA-based radar or cheat system.
