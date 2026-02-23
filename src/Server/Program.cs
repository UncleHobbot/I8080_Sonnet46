using Server.Hubs;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddSingleton<EmulatorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmulatorService>());
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024; // 4MB for disk uploads
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
builder.Services.AddCors(options =>
    options.AddPolicy("DevCors", p =>
        p.WithOrigins("http://localhost:5173", "http://localhost:3000")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

var app = builder.Build();

app.UseCors("DevCors");
app.UseDefaultFiles();
app.UseStaticFiles(); // serves frontend build from wwwroot

app.MapHub<EmulatorHub>("/hubs/emulator");

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Auto-start the emulator. Optionally load a disk image from disks/ directory.
var emulator = app.Services.GetRequiredService<EmulatorService>();
var logger   = app.Services.GetRequiredService<ILogger<Program>>();

var contentRoot = app.Environment.ContentRootPath;
var disksDir    = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "disks"));
var diskAPath   = Path.Combine(disksDir, "cpm22_system.dsk");

byte[]? diskA = null;
if (File.Exists(diskAPath))
{
    diskA = File.ReadAllBytes(diskAPath);
    logger.LogInformation("Loading disk A: {path} ({size} bytes)", diskAPath, diskA.Length);
}
else
{
    logger.LogInformation("No disk image at {path}. Starting with empty disk A.", diskAPath);
}

// Start the pure-.NET CP/M emulator (no external binary required)
emulator.StartEmulator(diskA);

app.Run();
