using System.Text;
using Emulator.CPU;
using Microsoft.AspNetCore.SignalR;
using Server.Services;

namespace Server.Hubs;

public sealed class EmulatorHub : Hub
{
    private readonly EmulatorService _emulator;

    public EmulatorHub(EmulatorService emulator)
    {
        _emulator = emulator;
    }

    // ── Client → Server ───────────────────────────────────────────────────────

    /// <summary>User typed something in the terminal.</summary>
    public void SendInput(string text)
    {
        _emulator.EnqueueInput(Encoding.Latin1.GetBytes(text));
    }

    public Task Pause()  => _emulator.PauseAsync();
    public Task Resume() => _emulator.ResumeAsync();
    public Task Step()   => _emulator.StepAsync();
    public Task Reset()  => _emulator.ResetAsync();
    public Task HardReset() => _emulator.HardResetAsync();

    public Task SetSpeed(int hz) => _emulator.SetSpeedAsync(hz);

    public Task<byte[]> ReadMemory(int address, int length) =>
        Task.FromResult(_emulator.ReadMemory(address, length));

    public Task<string[]> ListDiskFiles(int drive) =>
        Task.FromResult(_emulator.ListDiskFiles(drive));

    public async Task UploadDisk(int drive, string filename, byte[] data)
    {
        if (data.Length != 256256)
        {
            await Clients.Caller.SendAsync("StatusMessage", "error",
                $"Invalid disk image size {data.Length}. Expected 256256 bytes.");
            return;
        }
        await _emulator.MountDiskAsync(drive, filename, data);
    }

    public Task<byte[]> DownloadDisk(int drive) => _emulator.DownloadDiskAsync(drive);

    public Task<CpuStateDto?> GetCpuState() =>
        Task.FromResult(_emulator.GetCpuState());

    // ── Connection events ─────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var state = _emulator.GetCpuState();
        if (state != null)
            await Clients.Caller.SendAsync("CpuStateUpdate", state);
        await base.OnConnectedAsync();
    }
}
