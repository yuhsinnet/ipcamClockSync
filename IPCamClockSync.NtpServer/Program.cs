using IPCamClockSync.NtpServer;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<NtpServerOptions>(builder.Configuration.GetSection("NtpServer"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
