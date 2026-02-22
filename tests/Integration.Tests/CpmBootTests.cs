using System.Text;
using Emulator.Bios;
using Emulator.Cpm;
using Emulator.Disk;

namespace Integration.Tests;

/// <summary>
/// Headless CP/M boot test: loads real cpm22.sys + disk image and verifies
/// the system reaches the "A>" prompt within a time limit.
/// </summary>
public class CpmBootTests
{
    // Locate roms/disks relative to this test project
    private static string RepoRoot()
    {
        // Walk up from test output dir to find the repo root (contains roms/)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "roms")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find repo root with roms/ dir");
    }

    [Fact(Timeout = 10_000)] // 10 second timeout
    public async Task CpmBoots_Produces_Prompt()
    {
        var root = RepoRoot();
        var cpmBinPath = Path.Combine(root, "roms", "cpm22.sys");
        var diskAPath  = Path.Combine(root, "disks", "cpm22_system.dsk");

        Skip.If(!File.Exists(cpmBinPath), "cpm22.sys not found - skipping (requires ROM files)");
        Skip.If(!File.Exists(diskAPath),  "cpm22_system.dsk not found - run MkDisk first");

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
        var system = new CpmSystem(File.ReadAllBytes(cpmBinPath), input, OnOutput);
        system.Initialize();
        system.MountDisk(0, DiskImage.LoadFromFile(diskAPath));

        // Run CPU on background thread
        cts.Token.Register(() => promptSeen.TrySetCanceled());
        var cpuTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested && !promptSeen.Task.IsCompleted)
            {
                for (int i = 0; i < 50_000; i++)
                    system.Cpu.ExecuteOne();
            }
        }, cts.Token);

        var reached = await promptSeen.Task.ConfigureAwait(false);
        cts.Cancel();

        Assert.True(reached, $"CP/M did not produce 'A>' prompt. Output was:\n{output}");
    }

    [Fact(Timeout = 20_000)]
    public async Task Dir_Command_Lists_Files()
    {
        var root = RepoRoot();
        var cpmBinPath = Path.Combine(root, "roms", "cpm22.sys");
        var diskAPath  = Path.Combine(root, "disks", "cpm22_system.dsk");

        Skip.If(!File.Exists(cpmBinPath) || !File.Exists(diskAPath), "ROM/disk files not found");

        var output = new StringBuilder();
        var secondPrompt = new TaskCompletionSource<bool>();
        int promptCount = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));

        void OnOutput(string text)
        {
            output.Append(text);
            // Wait for A> after the DIR output (second prompt)
            var str = output.ToString();
            var count = str.Split("A>").Length - 1;
            if (count >= 2)
                secondPrompt.TrySetResult(true);
        }

        var input = new TerminalInputQueue();
        var system = new CpmSystem(File.ReadAllBytes(cpmBinPath), input, OnOutput);
        system.Initialize();
        system.MountDisk(0, DiskImage.LoadFromFile(diskAPath));

        cts.Token.Register(() => secondPrompt.TrySetCanceled());

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
        var firstPrompt = new CancellationTokenSource(9_000);
        while (!output.ToString().Contains("A>") && !firstPrompt.IsCancellationRequested)
            await Task.Delay(50).ConfigureAwait(false);

        if (!output.ToString().Contains("A>"))
            throw new Exception("Never reached first A> prompt");

        // Send "DIR\r" command
        foreach (char c in "DIR\r")
            input.Enqueue((byte)c);

        var reached = await secondPrompt.Task.ConfigureAwait(false);
        cts.Cancel();

        Assert.True(reached, $"DIR command did not complete. Output:\n{output}");
        Assert.Contains("COM", output.ToString()); // at least one .COM file listed
    }
}
