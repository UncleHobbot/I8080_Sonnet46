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

// Auto-load CP/M binary and disk image on startup
var emulator = app.Services.GetRequiredService<EmulatorService>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

var contentRoot = app.Environment.ContentRootPath;
var romsDir = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "roms"));
var disksDir = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "disks"));

var cpmBinaryPath = Path.Combine(romsDir, "cpm22.sys");
var diskAPath = Path.Combine(disksDir, "cpm22_system.dsk");

if (File.Exists(cpmBinaryPath))
{
    byte[] cpmBinary = File.ReadAllBytes(cpmBinaryPath);
    byte[]? diskA = File.Exists(diskAPath) ? File.ReadAllBytes(diskAPath) : null;

    logger.LogInformation("Loading CP/M binary: {path} ({size} bytes)", cpmBinaryPath, cpmBinary.Length);
    if (diskA != null)
        logger.LogInformation("Loading disk A: {path} ({size} bytes)", diskAPath, diskA.Length);
    else
        logger.LogWarning("No disk image at {path}. CP/M will boot but disk I/O will fail.", diskAPath);

    emulator.LoadCpm(cpmBinary, diskA);
}
else
{
    logger.LogWarning("CP/M binary not found at {path}. Emulator will not start.", cpmBinaryPath);
    logger.LogWarning("Place cpm22.sys in the roms/ directory and restart.");
}

app.Run();
