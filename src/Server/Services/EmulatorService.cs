using System.Diagnostics;
using System.Text;
using Emulator.Bios;
using Emulator.CPU;
using Emulator.Cpm;
using Emulator.Disk;
using Emulator.Memory;
using Microsoft.AspNetCore.SignalR;
using Server.Hubs;

namespace Server.Services;

public sealed class EmulatorService : IHostedService, IDisposable
{
    private readonly IHubContext<EmulatorHub> _hub;
    private readonly ILogger<EmulatorService> _logger;

    private CpmSystem? _system;
    private CancellationTokenSource _cts = new();
    private Task? _cpuTask;
    private Task? _stateTask;

    // CPU control
    private volatile bool _paused = true;
    private volatile bool _stepping;
    private int _speedHz = 2_000_000; // default 2 MHz

    // Output batching
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _outputLock = new();
    private Task? _outputFlushTask;

    public bool IsRunning => !_paused;

    public EmulatorService(IHubContext<EmulatorHub> hub, ILogger<EmulatorService> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EmulatorService starting...");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_cpuTask != null)
            await _cpuTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                          .ConfigureAwait(false);
    }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Start the emulator. No external binary required — CCP, BDOS, and all programs
    /// are implemented in .NET. Optionally provide a disk image for drive A.
    /// </summary>
    public void StartEmulator(byte[]? diskAImage = null)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();

        var input = new TerminalInputQueue();
        _system = new CpmSystem(input, WriteOutput);
        _system.DiskActivity += OnDiskActivity;
        _system.Initialize();

        if (diskAImage != null)
        {
            var diskA = DiskImage.LoadFromBytes("A.DSK", diskAImage);
            _system.MountDisk(0, diskA);
        }
        else
        {
            // Create an empty formatted disk on drive A so the CCP has a writable disk
            var blankDisk = DiskImage.CreateBlank("A.DSK");
            _system.MountDisk(0, blankDisk);
        }

        _paused = false;
        _cpuTask   = Task.Run(() => CpuLoop(_cts.Token));
        _stateTask = Task.Run(() => StateLoop(_cts.Token));

        _logger.LogInformation("CP/M system initialized (pure .NET), CPU running.");
    }

    // ── CPU loop ─────────────────────────────────────────────────────────────

    private async Task CpuLoop(CancellationToken ct)
    {
        if (_system == null) return;

        // Propagate the run token to the BIOS so blocking CONIN/CCP calls can be cancelled
        _system.SetRunToken(ct);

        const int BATCH = 10_000;
        var sw = Stopwatch.StartNew();
        long nextTarget = 0;

        while (!ct.IsCancellationRequested)
        {
            if (_paused && !_stepping)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                continue;
            }

            int ran = 0;
            if (_stepping)
            {
                ran = _system.Cpu.ExecuteOne();
                _stepping = false;
                _paused   = true;
                await BroadcastStateAsync().ConfigureAwait(false);
            }
            else
            {
                while (ran < BATCH && !ct.IsCancellationRequested && !_paused)
                    ran += _system.Cpu.ExecuteOne();
            }

            if (ran > 0 && !_stepping)
            {
                nextTarget += ran;
                long expectedMs = (nextTarget * 1000L) / _speedHz;
                long actualMs   = sw.ElapsedMilliseconds;
                long sleepMs    = expectedMs - actualMs;
                if (sleepMs > 1)
                    await Task.Delay((int)sleepMs, ct).ConfigureAwait(false);
            }
        }
    }

    // ── State broadcast loop (10 Hz) ─────────────────────────────────────────

    private async Task StateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
            if (!_paused) await BroadcastStateAsync().ConfigureAwait(false);
        }
    }

    private async Task BroadcastStateAsync()
    {
        if (_system == null) return;
        var state = _system.GetCpuState(!_paused, _speedHz);
        await _hub.Clients.All.SendAsync("CpuStateUpdate", state).ConfigureAwait(false);
    }

    // ── Terminal output batching ──────────────────────────────────────────────

    private void WriteOutput(string text)
    {
        lock (_outputLock)
        {
            _outputBuffer.Append(text);
            _outputFlushTask ??= Task.Run(FlushOutputAsync);
        }
    }

    private async Task FlushOutputAsync()
    {
        await Task.Delay(8).ConfigureAwait(false);
        string text;
        lock (_outputLock)
        {
            text = _outputBuffer.ToString();
            _outputBuffer.Clear();
            _outputFlushTask = null;
        }
        if (text.Length > 0)
            await _hub.Clients.All.SendAsync("ReceiveOutput", text).ConfigureAwait(false);
    }

    // ── Control methods (called from Hub) ────────────────────────────────────

    public void EnqueueInput(byte[] bytes) => _system?.Input.Enqueue(bytes);

    public async Task PauseAsync()
    {
        _paused = true;
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task ResumeAsync()
    {
        _paused = false;
        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task StepAsync()
    {
        _paused   = true;
        _stepping = true;
        await Task.Delay(20).ConfigureAwait(false);
    }

    public async Task ResetAsync()
    {
        // Warm boot: cancel current CONIN block, restart CCP
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _system?.Input.Clear();

        if (_system != null)
        {
            _system.SetRunToken(_cts.Token);
            _system.Cpu.PC = (ushort)(MemoryMap.BIOS_BASE + 3); // WBOOT
            _paused   = false;
            _cpuTask   = Task.Run(() => CpuLoop(_cts.Token));
            _stateTask = Task.Run(() => StateLoop(_cts.Token));
        }

        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public async Task HardResetAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _system?.Input.Clear();

        if (_system != null)
        {
            _system.Initialize();
            _system.SetRunToken(_cts.Token);
            _paused   = false;
            _cpuTask   = Task.Run(() => CpuLoop(_cts.Token));
            _stateTask = Task.Run(() => StateLoop(_cts.Token));
        }

        await BroadcastStateAsync().ConfigureAwait(false);
    }

    public Task SetSpeedAsync(int hz)
    {
        _speedHz = Math.Clamp(hz, 100_000, 20_000_000);
        return Task.CompletedTask;
    }

    public byte[] ReadMemory(int address, int length)
    {
        if (_system == null) return Array.Empty<byte>();
        address = Math.Clamp(address, 0, 0xFFFF);
        length  = Math.Clamp(length, 1, Math.Min(1024, 0x10000 - address));
        return _system.Memory.Dump(address, length);
    }

    public async Task MountDiskAsync(int drive, string filename, byte[] data)
    {
        if (_system == null) return;
        var disk = DiskImage.LoadFromBytes(filename, data);
        _system.MountDisk(drive, disk);
        await _hub.Clients.All.SendAsync("StatusMessage", "info",
            $"Disk '{filename}' mounted as drive {(char)('A' + drive)}:").ConfigureAwait(false);
    }

    public Task<byte[]> DownloadDiskAsync(int drive)
    {
        // Flush dirty files before download
        _system?.FlushDrive(drive);
        var disk = _system?.Disks.GetDisk(drive);
        return Task.FromResult(disk?.ToArray() ?? Array.Empty<byte>());
    }

    public string[] ListDiskFiles(int drive)
    {
        var cpmDisk = _system?.GetCpmDisk(drive);
        if (cpmDisk == null) return Array.Empty<string>();
        return cpmDisk.GetFileList().ToArray();
    }

    public CpuStateDto? GetCpuState() => _system?.GetCpuState(!_paused, _speedHz);

    private void OnDiskActivity(int drive, bool isWrite)
    {
        _ = _hub.Clients.All.SendAsync("DiskActivity", drive, isWrite);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
