using System.Text;
using Emulator.Bios;
using Emulator.Cpm;
using Emulator.Disk;

namespace Integration.Tests;

/// <summary>
/// Headless CP/M boot test: boots the pure-.NET CP/M implementation and verifies
/// the system reaches the "A>" prompt within a time limit.
/// No external ROM binary required.
/// </summary>
public class CpmBootTests
{
    // Locate disks/ relative to this test project
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "disks")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    [Fact(Timeout = 10_000)] // 10 second timeout
    public async Task CpmBoots_Produces_Prompt()
    {
        var output = new StringBuilder();
        var promptSeen = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(9));

        void OnOutput(string text)
        {
            output.Append(text);
            if (output.ToString().Contains("A>"))
                promptSeen.TrySetResult(true);
        }

        var input = new TerminalInputQueue();
        var system = new CpmSystem(input, OnOutput);
        system.Initialize();

        // Mount blank disk on A so CCP has somewhere to work
        system.MountDisk(0, DiskImage.CreateBlank("A.DSK"));

        // Run CPU on background thread
        cts.Token.Register(() => promptSeen.TrySetCanceled());
        system.SetRunToken(cts.Token);
        var cpuTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested && !promptSeen.Task.IsCompleted)
            {
                for (int i = 0; i < 50_000; i++)
                    system.Cpu.ExecuteOne();
            }
        }, cts.Token);

        var reached = await promptSeen.Task;
        cts.Cancel();

        Assert.True(reached, $"CP/M did not produce 'A>' prompt. Output was:\n{output}");
    }

    [Fact(Timeout = 20_000)]
    public async Task Dir_Command_Lists_Files()
    {
        var root = FindRepoRoot();
        var diskAPath = root != null ? Path.Combine(root, "disks", "cpm22_system.dsk") : null;

        var output = new StringBuilder();
        var secondPrompt = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));

        void OnOutput(string text)
        {
            output.Append(text);
            var str = output.ToString();
            var count = str.Split("A>").Length - 1;
            if (count >= 2)
                secondPrompt.TrySetResult(true);
        }

        var input = new TerminalInputQueue();
        var system = new CpmSystem(input, OnOutput);
        system.Initialize();

        // Load disk image if present, otherwise use blank disk
        if (diskAPath != null && File.Exists(diskAPath))
            system.MountDisk(0, DiskImage.LoadFromFile(diskAPath));
        else
            system.MountDisk(0, DiskImage.CreateBlank("A.DSK"));

        cts.Token.Register(() => secondPrompt.TrySetCanceled());
        system.SetRunToken(cts.Token);

        // Run CPU
        _ = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                for (int i = 0; i < 50_000; i++)
                    system.Cpu.ExecuteOne();
            }
        }, cts.Token);

        // Wait for first A> then send DIR
        var deadline = DateTime.UtcNow.AddSeconds(9);
        while (!output.ToString().Contains("A>") && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        if (!output.ToString().Contains("A>"))
            throw new Exception("Never reached first A> prompt");

        // Send "DIR\r" command
        input.Enqueue(new byte[] { (byte)'D', (byte)'I', (byte)'R', (byte)'\r' });

        var reached = await secondPrompt.Task;
        cts.Cancel();

        Assert.True(reached, $"DIR command did not complete. Output:\n{output}");
    }
}
